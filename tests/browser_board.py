"""Board geometry and promotion regression, with human-only HTTP fixtures.

Run: python tests/browser_board.py --url http://localhost:5317
No model is invoked and no real game is created.
"""
import argparse
import json
from pathlib import Path

from playwright.sync_api import expect, sync_playwright


def game_fixture():
    board = [0] * 32
    for square, piece in [(5, 1), (21, 1), (12, -1), (28, -1)]:
        board[square - 1] = piece
    moves = [
        {'id': f'{start}-{end}', 'from': start, 'to': end, 'path': [start, end],
         'captures': [], 'promotes': promotes}
        for start, end, promotes in [(5, 1, True), (28, 32, True), (1, 5, False), (32, 28, False)]
    ]
    game = {
        'id': 'c' * 32, 'access': 'local', 'createdAt': '2026-11-06T00:00:00Z',
        'updatedAt': '2026-11-06T00:00:00Z',
        'position': {'board': board, 'turn': 'white', 'ply': 0, 'quietKingPlyCount': 0, 'repetitionCount': 1},
        'white': {'id': 'human', 'name': 'Bianco', 'isHuman': True, 'connected': True},
        'black': {'id': 'human', 'name': 'Nero', 'isHuman': True, 'connected': True},
        'status': 'active', 'result': None, 'history': [], 'legalMoves': moves[:1],
        'permissions': {'isOwner': True, 'white': True, 'black': True, 'canMove': True,
                        'canAdvance': False, 'canJoin': False, 'canUndo': False},
    }

    def respond(route):
        request = route.request
        if request.method == 'POST' and request.url.endswith('/api/games'):
            assert request.post_data_json['white'] == request.post_data_json['black'] == 'human'
            route.fulfill(json={'token': 'd' * 64, 'game': game})
            return
        if request.method == 'POST':
            assert request.url.endswith('/move'), 'Only human moves are allowed in this check'
            ply = game['position']['ply']
            move = moves[ply]
            assert request.post_data_json == {'expectedPly': ply, 'moveId': move['id']}
            piece = board[move['from'] - 1]
            board[move['from'] - 1] = 0
            board[move['to'] - 1] = piece * 2 if move['promotes'] else piece
            game['history'].append({
                'ply': ply + 1, 'player': game['position']['turn'], 'moveId': move['id'],
                'notation': move['id'], 'path': move['path'], 'captures': [],
                'promotes': move['promotes'], 'thinkingMs': 0, 'comment': None,
            })
            game['position'].update(ply=ply + 1, turn='black' if ply % 2 == 0 else 'white')
            game['updatedAt'] = f'2026-11-06T00:00:0{ply + 1}Z'
            game['legalMoves'] = moves[ply + 1:ply + 2]
        route.fulfill(json=game)

    return game, moves, respond


def geometry(page):
    return page.locator('#board').evaluate('''board => {
        const rect = element => {
            const r = element.getBoundingClientRect();
            return {x:r.x, y:r.y, width:r.width, height:r.height};
        };
        return {
            board:rect(board), cells:[...board.children].map(rect),
            crowns:[...board.querySelectorAll('.piece.king')].map(piece =>
                ({piece:rect(piece), crown:rect(piece.querySelector('svg'))})),
            pageWidth:document.documentElement.scrollWidth, viewport:innerWidth
        };
    }''')


def assert_geometry(page, initial=None):
    dimensions = geometry(page)
    board = dimensions['board']
    assert abs(board['width'] - board['height']) < 0.5, dimensions
    assert len(dimensions['cells']) == 64
    for index, cell in enumerate(dimensions['cells']):
        assert abs(cell['width'] - cell['height']) < 0.5, (index, cell)
        assert abs(cell['width'] - (board['width'] - 2) / 8) < 0.5, (index, cell)
        assert abs(cell['x'] - board['x'] - 1 - index % 8 * cell['width']) < 0.5
        assert abs(cell['y'] - board['y'] - 1 - index // 8 * cell['height']) < 0.5
    for pair in dimensions['crowns']:
        piece, crown = pair['piece'], pair['crown']
        assert 0 < crown['width'] < piece['width'] and 0 < crown['height'] < piece['height']
        assert abs(crown['x'] + crown['width'] / 2 - piece['x'] - piece['width'] / 2) < 1
        assert abs(crown['y'] + crown['height'] / 2 - piece['y'] - piece['height'] / 2) < 1
    assert dimensions['pageWidth'] <= dimensions['viewport'], dimensions
    if initial:
        assert abs(board['height'] - initial['board']['height']) < 0.5, dimensions
        assert abs(board['width'] - initial['board']['width']) < 0.5, dimensions
    return dimensions


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--url', default='http://localhost:5317')
    args = parser.parse_args()
    out = Path('artifacts/board-fix')
    out.mkdir(parents=True, exist_ok=True)
    results, errors = [], []
    with sync_playwright() as playwright:
        browser = playwright.chromium.launch(channel='chrome', headless=True)
        for width, height in [(1440, 1000), (1024, 768), (768, 1024), (390, 844), (320, 720)]:
            context = browser.new_context(viewport={'width': width, 'height': height}, reduced_motion='reduce')
            page = context.new_page()
            page.on('pageerror', lambda error: errors.append(str(error)))
            game, moves, respond = game_fixture()
            page.route('**/api/games**', respond)
            page.goto(args.url, wait_until='networkidle')
            page.get_by_role('button', name='Con un amico').click()
            page.locator('#friend-access').select_option('local')
            page.locator('#start-button').click()
            expect(page.locator('.piece')).to_have_count(4)
            initial = assert_geometry(page)
            for ply, move in enumerate(moves, start=1):
                page.locator(f'[data-square="{move["from"]}"]').click()
                page.locator(f'[data-square="{move["to"]}"]').click()
                expect(page.locator('#move-count')).to_have_text(str(ply))
                expect(page.locator(f'[data-square="{move["to"]}"] .piece.king')).to_have_count(1)
                assert_geometry(page, initial)
            page.get_by_role('button', name='Ruota', exact=True).click()
            expect(page.locator('#board')).to_have_attribute('aria-label', 'Damiera italiana, vista dal Nero')
            assert_geometry(page, initial)
            page.locator('.table-area').screenshot(path=str(out / f'promotion-{width}.png'))

            # Multiple crowns across every occupied row must not change any grid track.
            game['position']['board'] = [-2] * 12 + [0] * 8 + [2] * 12
            game['permissions']['canMove'] = False
            game['legalMoves'] = []
            page.reload(wait_until='networkidle')
            expect(page.locator('.piece.king')).to_have_count(24)
            assert_geometry(page, initial)
            page.locator('.table-area').screenshot(path=str(out / f'kings-{width}.png'))
            results.append({'viewport': f'{width}x{height}', 'boardSize': initial['board']['width'],
                            'passed': ['white promotion', 'black promotion', 'king moves', 'rotation', '24 kings', 'no overflow']})
            context.close()
        browser.close()
    assert not errors, errors
    report = {'checks': results, 'browserErrors': errors, 'modelCalls': 0}
    (out / 'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report))


if __name__ == '__main__':
    main()
