"""End-to-end checks against a running Dama.Web. pip install playwright; uses installed Chrome.
Run: python tests/browser_smoke.py --url http://localhost:5317
Screenshots and the report are written to ignored artifacts/.
"""
import argparse
import json
from pathlib import Path
from playwright.sync_api import sync_playwright, expect

parser = argparse.ArgumentParser()
parser.add_argument('--url', default='http://localhost:5317')
args = parser.parse_args()
out = Path('artifacts')
out.mkdir(exist_ok=True)
errors = []
checks = []

with sync_playwright() as p:
    browser = p.chromium.launch(channel='chrome', headless=True)
    context = browser.new_context(extra_http_headers={'X-Match-Source': 'test'}, viewport={'width': 1440, 'height': 1000}, reduced_motion='reduce')
    page = context.new_page()
    page.on('pageerror', lambda error: errors.append(str(error)))
    page.on('console', lambda message: errors.append(message.text) if message.type == 'error' else None)
    page.goto(args.url, wait_until='networkidle')
    expect(page.get_by_role('button', name='Inizia partita')).to_be_enabled()
    expect(page.locator('.piece')).to_have_count(24)
    page.screenshot(path=str(out / 'desktop.png'), full_page=True)
    assert page.locator('body').evaluate('(el) => el.scrollWidth') == 1440
    checks.append('desktop initial board, model roster, no overflow')

    page.get_by_role('button', name='Inizia partita').click()
    expect(page.locator('#board-status')).to_contain_text('Il tuo turno')
    page.locator('[data-square="21"]').click()
    expect(page.locator('.destination')).to_have_count(2)
    page.locator('[data-square="17"]').click()
    expect(page.locator('#move-count')).to_have_text('2', timeout=10000)
    expect(page.locator('#board-status')).to_contain_text('Il tuo turno')
    page.screenshot(path=str(out / 'desktop-playing.png'), full_page=True)
    game_url = page.url
    page.reload(wait_until='networkidle')
    expect(page.locator('#move-count')).to_have_text('2')
    expect(page.locator('#board-status')).to_contain_text('Il tuo turno')
    page.get_by_role('button', name='Annulla', exact=True).click()
    expect(page.locator('#move-count')).to_have_text('0')
    expect(page.locator('.piece')).to_have_count(24)
    checks.append('human-model move, automated model response, reload and undo')

    page.get_by_role('button', name='Ruota', exact=True).click()
    expect(page.locator('#board')).to_have_attribute('aria-label', 'Damiera italiana, vista dal Nero')
    page.get_by_role('button', name='Regole', exact=True).click()
    expect(page.locator('#rules-dialog')).to_be_visible()
    page.keyboard.press('Escape')
    expect(page.locator('#rules-dialog')).not_to_be_visible()
    page.get_by_role('button', name='Attiva i suoni').click()
    expect(page.get_by_role('button', name='Disattiva i suoni')).to_have_attribute('aria-pressed', 'true')
    checks.append('rotation, keyboard dialog dismissal and sound toggle')

    page.get_by_role('button', name='Con un amico').click()
    page.locator('#friend-access').select_option('local')
    page.get_by_role('button', name='Nuova partita', exact=True).click()
    expect(page.locator('#board-status')).to_contain_text('(Bianco): scegli una tua pedina')
    page.locator('[data-square="21"]').click()
    page.locator('[data-square="17"]').click()
    expect(page.locator('#move-count')).to_have_text('1')
    page.locator('[data-square="9"]').click()
    page.locator('[data-square="13"]').click()
    expect(page.locator('#move-count')).to_have_text('2')
    checks.append('two humans on the same device, both colours')

    page.locator('#friend-access').select_option('online')
    page.get_by_role('button', name='Nuova partita', exact=True).click()
    page.locator('#confirm-button').click()
    expect(page.locator('#share-dialog')).to_be_visible()
    invitation = page.locator('#share-url').input_value()
    page.keyboard.press('Escape')
    friend_context = browser.new_context(extra_http_headers={'X-Match-Source': 'test'}, viewport={'width': 1280, 'height': 900}, reduced_motion='reduce')
    friend = friend_context.new_page()
    friend.on('pageerror', lambda error: errors.append(str(error)))
    friend.goto(invitation, wait_until='networkidle')
    friend.get_by_role('button', name='Prendi posto').click()
    expect(friend.locator('#board')).to_have_attribute('aria-label', 'Damiera italiana, vista dal Nero')
    expect(page.locator('#board-status')).to_contain_text('Il tuo turno', timeout=8000)
    page.locator('[data-square="21"]').click()
    page.locator('[data-square="17"]').click()
    expect(friend.locator('#board-status')).to_contain_text('Il tuo turno', timeout=8000)
    friend.locator('[data-square="9"]').click()
    friend.locator('[data-square="13"]').click()
    expect(page.locator('#move-count')).to_have_text('2', timeout=8000)
    spectator_context = browser.new_context(extra_http_headers={'X-Match-Source': 'test'})
    spectator = spectator_context.new_page()
    spectator.goto(invitation, wait_until='networkidle')
    expect(spectator.locator('#board-status')).to_contain_text('Sei spettatore')
    expect(spectator.get_by_role('button', name='Prendi posto')).not_to_be_visible()
    expect(spectator.locator('.can-select')).to_have_count(0)
    checks.append('online invite, isolated second human and read-only spectator')

    page.get_by_role('button', name='Tra modelli').click()
    page.locator('#opponent').select_option('opale')
    page.locator('#second-model').select_option('opale')
    page.get_by_role('button', name='Nuova partita', exact=True).click()
    page.locator('#confirm-button').click()
    expect(page.locator('#move-count')).not_to_have_text('0', timeout=10000)
    page.get_by_role('button', name='Pausa', exact=True).click()
    expect(page.locator('#board-status')).to_contain_text('pausa')
    page.screenshot(path=str(out / 'model-match.png'), full_page=True)
    expect(page.get_by_role('button', name='Riprendi', exact=True)).to_be_visible()
    checks.append('model-model match, automatic turns and pause control')

    mobile_context = browser.new_context(extra_http_headers={'X-Match-Source': 'test'}, viewport={'width': 390, 'height': 844}, is_mobile=True, has_touch=True, reduced_motion='reduce')
    mobile = mobile_context.new_page()
    mobile.on('pageerror', lambda error: errors.append(str(error)))
    mobile.goto(args.url, wait_until='networkidle')
    assert mobile.locator('body').evaluate('(el) => el.scrollWidth') == 390
    mobile.get_by_role('button', name='Inizia partita').click()
    mobile.locator('[data-square="21"]').tap()
    mobile.locator('[data-square="17"]').tap()
    expect(mobile.locator('#move-count')).to_have_text('2', timeout=10000)
    mobile.screenshot(path=str(out / 'mobile.png'), full_page=True)
    checks.append('mobile 390px layout, touch move and model reply')

    # A deterministic FID fixture isolates the multi-hop UI from opponent choices.
    fixture_context = browser.new_context(extra_http_headers={'X-Match-Source': 'test'}, viewport={'width': 1280, 'height': 900}, reduced_motion='reduce')
    fixture_page = fixture_context.new_page()
    fixture_page.on('pageerror', lambda error: errors.append(str(error)))
    board = [0] * 32
    for square, piece in [(18, 1), (24, 2), (14, -1), (7, -1), (20, -1)]:
        board[square - 1] = piece
    fixture = {
        'id': 'a' * 32, 'access': 'local', 'createdAt': '2026-11-06T00:00:00Z', 'updatedAt': '2026-11-06T00:00:00Z',
        'position': {'board': board, 'turn': 'white', 'ply': 0, 'quietKingPlyCount': 0, 'repetitionCount': 1},
        'white': {'id': 'human', 'name': 'Bianco', 'isHuman': True, 'connected': True},
        'black': {'id': 'human', 'name': 'Nero', 'isHuman': True, 'connected': True},
        'status': 'active', 'result': None, 'history': [],
        'legalMoves': [{'id': '18-11-4', 'from': 18, 'to': 4, 'path': [18, 11, 4], 'captures': [14, 7], 'promotes': True}],
        'permissions': {'isOwner': True, 'white': True, 'black': True, 'canMove': True, 'canAdvance': False, 'canJoin': False, 'canUndo': False}
    }

    def fixture_api(route):
        request = route.request
        if request.method == 'POST' and request.url.endswith('/api/games'):
            route.fulfill(json={'token': 'b' * 64, 'game': fixture})
        elif request.method == 'POST' and request.url.endswith('/move'):
            assert request.post_data_json == {'expectedPly': 0, 'moveId': '18-11-4'}
            for square in [18, 14, 7]:
                board[square - 1] = 0
            board[3] = 2
            fixture['position'].update(turn='black', ply=1)
            fixture['updatedAt'] = '2026-11-06T00:00:01Z'
            fixture['legalMoves'] = [{'id': '20-23', 'from': 20, 'to': 23, 'path': [20, 23], 'captures': [], 'promotes': False}]
            fixture['history'] = [{'ply': 1, 'player': 'white', 'moveId': '18-11-4', 'notation': '18x11x4', 'path': [18, 11, 4], 'captures': [14, 7], 'promotes': True, 'thinkingMs': 0, 'comment': None}]
            route.fulfill(json=fixture)
        else:
            route.fulfill(json=fixture)

    fixture_page.route('**/api/games**', fixture_api)
    fixture_page.goto(args.url, wait_until='networkidle')
    fixture_page.get_by_role('button', name='Con un amico').click()
    fixture_page.locator('#friend-access').select_option('local')
    fixture_page.get_by_role('button', name='Inizia partita').click()
    fixture_page.locator('[data-square="18"]').click()
    fixture_page.locator('[data-square="11"]').click()
    expect(fixture_page.locator('[data-square="11"] .piece.ivory')).to_have_count(1)
    expect(fixture_page.locator('[data-square="18"] .piece')).to_have_count(0)
    expect(fixture_page.locator('[data-square="14"]')).to_have_class('square dark pending-capture')
    fixture_page.locator('[data-square="4"]').click()
    expect(fixture_page.locator('[data-square="4"] .piece.king')).to_have_count(1)
    expect(fixture_page.locator('#move-count')).to_have_text('1')
    expect(fixture_page.locator('.piece')).to_have_count(3)
    checks.append('multi-capture staged preview, complete path submission and promotion')
    assert not errors, errors
    report = {'passed': checks, 'browserErrors': errors, 'url': args.url}
    (out / 'browser-report.json').write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
    print(json.dumps(report, ensure_ascii=True))
    browser.close()
