import json, sys
# Prints the boards of a game between two plies, with the move and the comment of the engine.
path, n, lo, hi = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), int(sys.argv[4])
sym = {0: '.', 1: 'b', 2: 'B', -1: 'n', -2: 'N'}
for line in open(path, encoding='utf-8'):
    g = json.loads(line)
    if g['n'] != n: continue
    for f in g['frames']:
        if not (lo <= f['ply'] <= hi): continue
        b = f['board']
        print(f"ply {f['ply']} mossa {f.get('moveId')} di {f.get('by')} {f.get('comment') or ''}")
        for r in range(8):
            row = ['  '] * 8
            for k in range(4):
                c = (r % 2) + 2 * k
                s = r * 4 + k
                row[c] = sym[b[s]] + ' '
            print(f"  {8-r} " + ''.join(row) + f"   {r*4+1:2}-{r*4+4:2}")
        print('    a b c d e f g h')
