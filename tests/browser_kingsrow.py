"""Read-only roster check and synthetic victory UI fixtures; creates no games."""
import json
from pathlib import Path
from playwright.sync_api import sync_playwright, expect

out = Path('artifacts/kingsrow')
errors, checks = [], []
with sync_playwright() as p:
    browser = p.chromium.launch(channel='chrome', headless=True)
    for mobile in (False, True):
        context = browser.new_context(extra_http_headers={'X-Match-Source': 'test'},
            viewport={'width': 390 if mobile else 1280, 'height': 844 if mobile else 900},
            reduced_motion='reduce' if mobile else 'no-preference')
        page = context.new_page()
        page.on('pageerror', lambda error: errors.append(str(error)))
        page.goto('http://localhost:5317', wait_until='networkidle')
        page.locator('#opponent').select_option('kingsrow')
        expect(page.locator('#opponent-description')).to_contain_text('Motore esperto esterno')
        expect(page.locator('#start-button')).to_be_enabled()
        checks.append('Kingsrow selectable mobile=' + str(mobile))
        for winner in ('white', 'black', 'draw'):
            gid = ('a' if winner == 'white' else 'b' if winner == 'black' else 'c') * 32
            game = {'id': gid, 'access': 'local', 'source': 'test', 'updatedAt': '2026-11-07T10:00:00Z',
                'white': {'id': 'human', 'name': 'Alice', 'isHuman': True, 'connected': True},
                'black': {'id': 'kingsrow', 'name': 'Kingsrow Italian', 'isHuman': False, 'connected': True},
                'position': {'board': [0]*32, 'turn': 'white', 'ply': 80, 'quietKingPlyCount': 0, 'repetitionCount': 1},
                'status': 'finished', 'result': {'winner': winner, 'reason': 'repetition' if winner == 'draw' else 'blocked'},
                'legalMoves': [], 'history': [],
                'permissions': {'isOwner': False, 'white': False, 'black': False, 'canMove': False, 'canAdvance': False, 'canJoin': False, 'canUndo': False}}
            page.route('**/api/games/' + gid, lambda route, request, data=game: route.fulfill(json=data))
            page.goto('http://localhost:5317/?game=' + gid, wait_until='networkidle')
            expect(page.locator('#result-dialog')).to_be_visible()
            expect(page.locator('#result-title')).to_have_text('Alice' if winner == 'white' else 'Kingsrow Italian' if winner == 'black' else 'Patta.')
            expect(page.locator('.result-confetti i')).to_have_count(0 if mobile or winner == 'draw' else 28)
            assert page.evaluate('document.documentElement.scrollWidth <= innerWidth')
            if winner == 'black': page.screenshot(path=str(out / ('victory-mobile.png' if mobile else 'victory-desktop.png')))
            page.keyboard.press('Escape')
            expect(page.locator('#result-dialog')).not_to_be_visible()
            page.reload(wait_until='networkidle')
            expect(page.locator('#result-dialog')).not_to_be_visible()
            checks.append('victory='+winner+', mobile='+str(mobile)+', close/reload/no repetition')
        context.close()
    browser.close()
assert not errors, errors
(out/'browser-verification.json').write_text(json.dumps({'checks': checks, 'errors': errors}, indent=2), encoding='utf8')
print(json.dumps({'checks': len(checks), 'errors': errors}))
