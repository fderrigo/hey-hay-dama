"""Synthetic HTTP contract checks only; does not create or play matches."""
import copy
import json
import urllib.request
import urllib.error

url = 'http://127.0.0.1:9015/choose-move'
base = {'protocolVersion': 'dama-model/v1', 'rules': 'italian-draughts-8x8', 'player': 'white',
    'position': {'board': [-1]*12+[0]*8+[1]*12, 'turn': 'white', 'ply': 0, 'quietKingPlyCount': 0},
    'legalMoves': [{'id': 'opaque-'+str(i), 'path': path} for i, path in enumerate(
        [[21,17],[21,18],[22,18],[22,19],[23,19],[23,20],[24,20]])], 'thinkTimeMs': 300}

def send(body):
    req = urllib.request.Request(url, data=json.dumps(body).encode(), headers={'Content-Type':'application/json'})
    try:
        with urllib.request.urlopen(req, timeout=10) as response:
            return response.status, json.load(response)
    except urllib.error.HTTPError as error:
        return error.code, json.load(error)

for side in ('white', 'black'):
    body = copy.deepcopy(base)
    body['player'] = body['position']['turn'] = side
    if side == 'black':
        for move in body['legalMoves']: move['path'] = [33-x for x in move['path']]
    status, reply = send(body)
    assert status == 200 and reply['protocolVersion'] == 'dama-model/v1'
    assert reply['moveId'] in {m['id'] for m in body['legalMoves']}

invalid = []
body = copy.deepcopy(base); body['legalMoves'] = [None]; invalid.append(body)
body = copy.deepcopy(base); body['rules'] = 'english'; invalid.append(body)
body = copy.deepcopy(base); body['position']['board'] = [1]*32; invalid.append(body)
body = copy.deepcopy(base); body['legalMoves'][0]['path'] = [21,1]; invalid.append(body)
body = copy.deepcopy(base); body['legalMoves'][1]['id'] = body['legalMoves'][0]['id']; invalid.append(body)
body = copy.deepcopy(base); body['thinkTimeMs'] = 0; invalid.append(body)
for body in invalid: assert send(body)[0] == 400
print('8 protocol checks passed; no matches created')
