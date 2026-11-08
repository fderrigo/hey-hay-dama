import json, sys, re
# Prints, for each Avventurina move, depth and score from its comment, to locate where a game was lost.
for line in open(sys.argv[1], encoding='utf-8'):
    g = json.loads(line)
    if len(sys.argv) > 2 and str(g['n']) != sys.argv[2]:
        continue
    print('partita', g['n'], g['white'], 'vs', g['black'], g['winner'], g['reason'], g['plies'])
    out = []
    for f in g['frames'][1:]:
        if f.get('by') == 'avventurina':
            m = re.search(r'profondità (\d+), valutazione (-?\d+)', f.get('comment') or '')
            if m:
                pieces = sum(1 for x in f['board'] if x)
                out.append(f"{f['ply']}:{m.group(2)}/d{m.group(1)}/p{pieces}")
            else:
                out.append(f"{f['ply']}:forz")
    print(' '.join(out))
