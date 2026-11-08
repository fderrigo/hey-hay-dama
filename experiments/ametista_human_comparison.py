"""Descriptive paired analysis of the explicitly exploratory human-data extension."""
import datetime
import hashlib
import json
from pathlib import Path
import sys


def require(ok, message):
    if not ok:raise ValueError(message)


def main():
    root=Path(sys.argv[1]).resolve();base=root/'arena/ametista/2026-11-07'
    output=base/'human-comparison-report.json'
    require(not output.exists(),'Refusing to overwrite report')
    hashes={}
    def raw(path):
        data=path.read_bytes();hashes[str(path.relative_to(root))]=hashlib.sha256(data).hexdigest();return data
    def read(path):return json.loads(raw(path).decode('utf-8-sig'))
    extension=read(base/'human-control-extension.json');read(base/'human-play-plan.json')
    seeds=extension['seeds'];require(seeds==[570401,570402,570403],'Unexpected opening set')
    configs={};results={};pairs={};openings={};ids=set();game_rows=[]
    for label,folder in (('v18','human-exploratory-v18'),('v14','human-control-v14')):
        cfg=read(base/(folder+'.json'));configs[label]=cfg
        require(cfg['source']=='test' and cfg['seeds']==seeds,'Wrong source/calendar')
        require(cfg['logicalProcessor']==0,'Wrong logical processor')
        for side in ('a','b'):
            require(cfg[side]['searchMs']==1000 and cfg[side].get('protocolReserveMs',100)==100,'Wrong budget')
        require(cfg['a']['id']=='ametista' and cfg['b']['id']=='kingsrow','Wrong participants')
        manifest=read(base/folder/'manifest.json')
        require(manifest['openingPlies']==6 and manifest['training'] is False,'Unexpected runner mode')
        actual=manifest['config']
        for key in ('source','seeds','logicalProcessor'):require(actual[key]==cfg[key],'Manifest/config mismatch')
        for player in ('a','b'):
            for key in ('id','version','sha256','endpoint','processId','searchMs','protocolReserveMs','expectedHealth'):
                require(actual[player].get(key)==cfg[player].get(key),'Manifest player mismatch')
        audit=read(base/folder/'audit.json')
        require(all(audit.get(k) is True for k in ('allRecorded','legalReplay','fullSchedule','pairedOpenings')),'Incomplete pair audit')
        require(audit['games']==audit['completed']==6,'Expected six completed matches')
        games=[json.loads(line) for line in raw(base/folder/'games.jsonl').splitlines() if line.strip()]
        require(len(games)==6,'Wrong game count');observed=set();points={s:0 for s in seeds}
        total=dict(games=6,wins=0,draws=0,losses=0)
        for g in games:
            require(g['completed'] is True and g['error'] is None,'Incomplete match')
            require(type(g['winner']) is int and g['winner'] in (-1,0,1),'Invalid outcome')
            require({g['white'],g['black']}=={'ametista','kingsrow'},'Wrong match participants')
            seed=g['openingSeed'];require(seed in seeds,'Unexpected opening')
            side=1 if g['white']=='ametista' else -1;key=(seed,side)
            require(key not in observed,'Repeated opening/color');observed.add(key)
            mid=g['matchId'];require(mid not in ids,'Repeated match ID');ids.add(mid)
            record=read(root/'src/Dama.Web/App_Data/competition'/(mid+'.json'))
            require(record['id']==mid and record['status']=='finished' and record['source']=='test','Registry mismatch')
            require(record['winner']==g['winner'] and record['plies']==g['plies'],'Registry result mismatch')
            for color in ('white','black'):require(record[color]['id']=='ai:'+g[color],'Registry participant mismatch')
            prefix=[(f['ply'],f['board'],f['turn']) for f in g['frames'] if f['ply']<=6]
            require([f[0] for f in prefix]==list(range(7)),'Incomplete opening replay')
            require(openings.setdefault(seed,prefix)==prefix,'Paired openings differ')
            outcome='draws' if g['winner']==0 else 'wins' if g['winner']==side else 'losses'
            total[outcome]+=1;points[seed]+=1 if outcome=='draws' else 2 if outcome=='wins' else 0
            board=prefix[-1][1]
            game_rows.append(dict(variant=label,matchId=mid,seed=seed,color='white' if side==1 else 'black',outcome=outcome,plies=g['plies'],openingWhiteMen=board.count(1),openingBlackMen=board.count(-1)))
        require(observed=={(s,c) for s in seeds for c in (-1,1)},'Incomplete paired schedule')
        total.update(winRate=total['wins']/6,score=(2*total['wins']+total['draws'])/12)
        results[label]=total;pairs[label]={s:points[s]/4 for s in seeds}
    a,b=configs['v14'],configs['v18']
    for key in ('endpoint','processId','sha256','version'):require(a['b'][key]==b['b'][key],'Different Kingsrow conditions')
    require(a['a']['sha256']==b['a']['sha256'],'Manual model differs')
    require(b['a']['expectedHealth']['humanSha256']==read(base/'human-data/learned-v1/report.json')['modelSha256'],'Learned model mismatch')
    for path,digest in hashes.items():require(hashlib.sha256((root/path).read_bytes()).hexdigest()==digest,'Input changed during analysis')
    result=dict(analyzedAt=datetime.datetime.now(datetime.timezone.utc).isoformat(),complete=True,realGamesCreated=0,gamesAnalyzed=12,kind='post-declaration exploratory paired control',declarationContext=extension['declarationContext'],variants=results,pairedScoreDifference=results['v18']['score']-results['v14']['score'],pairs=[dict(seed=s,baseline=pairs['v14'][s],candidate=pairs['v18'][s],difference=pairs['v18'][s]-pairs['v14'][s]) for s in seeds],games=game_rows,automaticallyPromoted=False,superiorityClaimed=False,inputSha256=hashes,limitations=['Only three opening pairs; no general improvement or superiority inference.','Control extension declared after observing first two candidate losses; not independent preregistered confirmation.','Control follows candidate in time; shared load and runtime warmup differ.','Equal material at the opening does not establish theoretical balance.','Offline outcome-prediction improvement and playing strength are separate measurements.'])
    with output.open('x',encoding='utf-8') as stream:json.dump(result,stream,indent=2);stream.write('\n')
    print(json.dumps({k:v for k,v in result.items() if k not in ('inputSha256','games')}))


if __name__=='__main__':main()
