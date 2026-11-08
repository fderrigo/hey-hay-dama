"""Human-only browser regression checks. All real matches use source=test.

Run against a running server: python tests/browser_humans.py --url http://localhost:5317
No model moves or model-versus-model matches are requested.
"""
import argparse
import json
import re
from pathlib import Path
from playwright.sync_api import sync_playwright, expect

parser = argparse.ArgumentParser()
parser.add_argument('--url', default='http://localhost:5317')
args = parser.parse_args()
checks, errors, games = [], [], []

with sync_playwright() as p:
    browser = p.chromium.launch(channel='chrome', headless=True)
    contexts = []

    def new_page(mobile=False, roster_failure=False):
        context = browser.new_context(
            extra_http_headers={'X-Match-Source': 'test'}, reduced_motion='reduce',
            viewport={'width': 390 if mobile else 1280, 'height': 844 if mobile else 900},
            is_mobile=mobile, has_touch=mobile)
        contexts.append(context)
        page = context.new_page()
        page.on('pageerror', lambda error: errors.append(str(error)))
        if roster_failure:
            page.route('**/api/models', lambda route: route.fulfill(
                status=503, content_type='application/json', body='{"error":"Roster unavailable"}'))
        return page

    def create(page, access, side='white'):
        page.goto(args.url, wait_until='networkidle')
        page.locator('[data-mode="human-human"]').click()
        page.locator('#friend-access').select_option(access)
        page.locator(f'input[name="color"][value="{side}"]').check()
        page.locator('#nickname').fill('Test Human Host')
        if access == 'local':
            page.locator('#friend-nickname').fill('Test Human Friend')
            expect(page.locator('#top-name')).not_to_contain_text('Opale')
        page.locator('[data-square="9"]').click()
        expect(page.locator('#toast')).to_contain_text('Inizia partita')
        with page.expect_response(lambda r: r.request.method == 'POST' and r.url.endswith('/api/games')) as response:
            page.locator('#start-button').click()
        data = response.value.json()
        assert response.value.status == 200, data
        assert data['game']['source'] == 'test'
        games.append((page.context, data['game']['id'], data['token']))
        return data['game']['id']

    def move(page, start, end, ply):
        expect(page.locator(f'[data-square="{start}"]')).to_have_class(re.compile('can-select'))
        page.locator(f'[data-square="{start}"]').click()
        expect(page.locator('#board-status')).to_contain_text('Pedina selezionata')
        page.locator(f'[data-square="{end}"]').click()
        expect(page.locator('#move-count')).to_have_text(str(ply), timeout=8000)

    try:
        for mobile, local_side in ((False, 'white'), (True, 'white'), (False, 'black')):
            page = new_page(mobile=mobile)
            create(page, 'local', local_side)
            white_name = 'Test Human Host' if local_side == 'white' else 'Test Human Friend'
            own_color = 'Bianco' if local_side == 'white' else 'Nero'
            other_color = 'Nero' if local_side == 'white' else 'Bianco'
            expect(page.locator('#board-status')).to_contain_text(f'{white_name} (Bianco)')
            expect(page.locator('#bottom-name')).to_have_text(f'Test Human Host · {own_color}')
            expect(page.locator('#top-name')).to_have_text(f'Test Human Friend · {other_color}')
            page.locator('[data-square="9"]').click()
            expect(page.locator('#toast')).to_contain_text(f'Tocca a {white_name} (Bianco)')
            move(page, 21, 17, 1)
            move(page, 9, 13, 2)
            page.reload(wait_until='networkidle')
            expect(page.locator('#friend-access')).to_have_value('local')
            expect(page.locator('#friend-nickname')).to_be_visible()
            expect(page.locator('#nickname')).to_have_value('Test Human Host')
            expect(page.locator('#friend-nickname')).to_have_value('Test Human Friend')
            expect(page.locator(f'input[name="color"][value="{local_side}"]')).to_be_checked()
            with page.expect_response(lambda r: r.request.method == 'POST' and r.url.endswith('/api/games')) as response:
                page.locator('#start-button').click()
                page.locator('#confirm-button').click()
            data = response.value.json()
            assert data['game']['access'] == 'local' and data['game']['source'] == 'test'
            games.append((page.context, data['game']['id'], data['token']))
            expect(page.locator('#share-dialog')).not_to_be_visible()
            expect(page.locator('#board-status')).to_contain_text(f'{white_name} (Bianco)')
            Path('artifacts').mkdir(exist_ok=True)
            page.screenshot(path=f'artifacts/humans-{ "mobile" if mobile else "desktop" }.png', full_page=True)
            assert page.locator('body').evaluate('(el) => el.scrollWidth <= innerWidth'), 'Horizontal overflow'
            checks.append(f'local create, both turns, reload and new local match; mobile={mobile}, owner={local_side}')

        for side in ('white', 'black'):
            owner = new_page()
            game_id = create(owner, 'online', side)
            expect(owner.locator('#share-dialog')).to_be_visible()
            expect(owner.locator('#share-description')).to_contain_text('Prendi posto')
            invitation = owner.locator('#share-url').input_value()
            assert 'token' not in invitation.lower()
            owner.keyboard.press('Escape')
            guest = new_page(roster_failure=True)
            guest.goto(invitation, wait_until='networkidle')
            expect(guest.locator('#join-button')).to_be_visible()
            guest.locator('#join-nickname').fill('Test Human Guest')
            guest.locator('#join-button').click()
            expect(guest.locator('#join-area')).to_be_hidden()
            white, black = (owner, guest) if side == 'white' else (guest, owner)
            move(white, 21, 17, 1)
            move(black, 9, 13, 2)
            owner.reload(wait_until='networkidle')
            expect(owner.locator('#friend-access')).to_have_value('online')
            expect(owner.locator(f'input[name="color"][value="{side}"]')).to_be_checked()
            spectator = new_page(roster_failure=True)
            spectator.goto(invitation, wait_until='networkidle')
            expect(spectator.locator('#board-status')).to_contain_text('Sei spettatore')
            expect(spectator.locator('#join-area')).to_be_hidden()
            expect(spectator.locator('.can-select')).to_have_count(0)
            checks.append(f'invite/join/moves/spectator despite roster error; owner={side}')
        assert not errors, errors
    finally:
        # Conclude created matches through the ordinary API; retain their test records.
        for context, game_id, token in games:
            headers = {'X-Game-Token': token, 'X-Match-Source': 'test'}
            response = context.request.get(f'{args.url}/api/games/{game_id}', headers=headers)
            if response.ok:
                game = response.json()
                if game['status'] == 'waiting':
                    context.request.post(f'{args.url}/api/games/{game_id}/join',
                                         data={'nickname': 'Test Cleanup Guest'},
                                         headers={'X-Match-Source': 'test'})
                    game = context.request.get(f'{args.url}/api/games/{game_id}', headers=headers).json()
                if game['status'] == 'active':
                    ended = context.request.post(f'{args.url}/api/games/{game_id}/resign',
                                                 data={'expectedPly': game['position']['ply']}, headers=headers)
                    assert ended.ok, f'Could not conclude test match {game_id}'
        browser.close()

report = {'success': True, 'checks': checks, 'matches': [g[1] for g in games], 'source': 'test', 'pageErrors': errors}
Path('artifacts').mkdir(exist_ok=True)
Path('artifacts/human-play-verification.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report, indent=2))
