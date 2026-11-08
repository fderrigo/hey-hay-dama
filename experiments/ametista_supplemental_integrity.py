"""Read-only final integrity audit; creates only a new audit report, never games."""
import datetime
import hashlib
import json
import pathlib
import sys
import urllib.request


def require(condition, message):
    if not condition:
        raise ValueError(message)


def main():
    root=pathlib.Path(sys.argv[1]).resolve()
    base=root/'arena/ametista/2026-11-07'
    output=base/'supplemental-final-integrity.json'
    require(not output.exists(),'Refusing to overwrite final audit')
    inputs={}
    def raw(path):
        path=pathlib.Path(path)
        data=path.read_bytes();inputs[str(path.relative_to(root))]=hashlib.sha256(data).hexdigest();return data
    def read(path):return json.loads(raw(path).decode('utf-8-sig'))
    frozen=read(base/'supplemental-confirmation-freeze.json')
    session=read(base/'session.json')
    checked=[]
    for item in frozen['files']+session['baselineFiles']:
        actual=hashlib.sha256(raw(root/item['path'])).hexdigest()
        require(actual==item['sha256'],'Frozen/original file changed: '+item['path'])
        checked.append(item['path'])
    for name in ('final-comparison.json','supplemental-confirmation-report.json'):
        report=read(base/name)
        require(report['complete'] is True,'Incomplete statistical report')
        for path,digest in report['inputSha256'].items():
            require(hashlib.sha256(raw(root/path)).hexdigest()==digest,'Report input changed: '+path)
    selection=read(base/'supplemental-confirmation-selection.json')
    registry=root/'src/Dama.Web/App_Data/competition'
    ids=set();records=[];health={};memory_peak=0
    for entry in selection['schedule']:
        directory=root/entry['output'];cfg=read(root/entry['config'])
        games=[json.loads(line) for line in raw(directory/'games.jsonl').splitlines() if line.strip()]
        require(len(games)==2,'Wrong pair size')
        for game in games:
            match_id=game['matchId'];require(match_id not in ids,'Repeated game');ids.add(match_id)
            record=read(registry/(match_id+'.json'))
            require(game['completed'] is True and game['error'] is None,'Incomplete replay')
            require(record['id']==match_id and record['source']=='benchmark' and record['status']=='finished','Wrong registry state')
            require(record['winner']==game['winner'] and record['plies']==game['plies'] and record['reason']==game['reason'],'Registry/replay result mismatch')
            for color in ('white','black'):
                require(record[color]['id']=='ai:'+game[color] and record[color]['kind']=='ai','Wrong registry participant')
            require(record['type']=='ai-ai','Wrong match type')
            peak=max(game['whitePeakBytes'],game['blackPeakBytes']);memory_peak=max(memory_peak,peak)
            require(peak<=frozen['maximumProcessBytes'],'Recorded memory exceeds configured process limit')
            records.append(dict(matchId=match_id,variant=entry['variant'],seed=entry['seed'],winner=game['winner'],plies=game['plies']))
        for player in ('a','b'):
            url=cfg[player]['endpoint'].rsplit('/',1)[0]+'/health'
            if url not in health:
                with urllib.request.urlopen(url,timeout=5) as response:health[url]=json.load(response)
            for key,value in cfg[player]['expectedHealth'].items():
                require(health[url].get(key)==value,'Service health identity mismatch')
            if player=='a':require(health[url].get('threads')==1,'Wrong engine thread count')
    require(len(ids)==72,'Expected all 72 supplemental matches')
    first_ids=set()
    for folder in ('confirmation-opale','confirmation-ametista'):
        original_games=[json.loads(line) for line in raw(base/folder/'games.jsonl').splitlines() if line.strip()]
        require(len(original_games)==36,'First confirmation changed size')
        for game in original_games:
            match_id=game['matchId']
            require(match_id not in ids and match_id not in first_ids,'First/supplemental IDs overlap')
            first_ids.add(match_id)
            record=read(registry/(match_id+'.json'))
            require(record['id']==match_id and record['source']=='benchmark' and record['status']=='finished','First confirmation registry state changed')
            require(record['winner']==game['winner'] and record['plies']==game['plies'] and record['reason']==game['reason'],'First confirmation registry result changed')
            for color in ('white','black'):
                require(record[color]['id']=='ai:'+game[color],'First confirmation participant changed')
    interruptions=['37c6bb0886c64890ac3d2eb6d322244f','42ddb5eadf39490c9f2927b33d0a2f6e','34cfd538503446bd8aa77cc30228ebe5','497fffcd785a4ed58ec869bdd3b90c71']
    for match_id in interruptions:
        record=read(registry/(match_id+'.json'))
        require(record['status']=='interrupted' and record['winner'] is None,'Prior interruption was changed')
    for path,digest in inputs.items():
        require(hashlib.sha256((root/path).read_bytes()).hexdigest()==digest,'Input changed during audit: '+path)
    result=dict(verifiedAt=datetime.datetime.now(datetime.timezone.utc).isoformat(),allFrozenAndOriginalFilesMatch=True,originalOpaleFiles=len(session['baselineFiles']),filesChecked=len(checked),firstAndSecondReportInputsUnchanged=True,registeredCompletedSupplementalGames=len(ids),firstConfirmationRegisteredGames=len(first_ids),preservedInterruptions=interruptions,recordedPeakBytes=memory_peak,processMemoryLimit=frozen['maximumProcessBytes'],health=health,games=records,inputSha256=inputs,realGamesCreated=0,limitations=['Health is a current self-report, not an independent proof of historical loaded binaries.','Recorded peak memory is sampled process memory, not total machine memory.','This audit checks registry consistency; legal replay is provided by the separate pair audits.','The frozen runner binary is archived; exact matching historical source snapshot was not found.'])
    with output.open('x',encoding='utf-8') as stream:json.dump(result,stream,indent=2);stream.write('\n')
    print(json.dumps({k:v for k,v in result.items() if k not in ('inputSha256','games','health')}))


if __name__=='__main__':main()
