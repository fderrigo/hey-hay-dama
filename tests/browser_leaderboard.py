"""Check the leaderboard using one real human-human match, recorded as test.
Run against Dama.Web: python tests/browser_leaderboard.py --url http://localhost:5317
"""
import argparse
import json
from pathlib import Path
from uuid import uuid4
from playwright.sync_api import sync_playwright, expect

parser = argparse.ArgumentParser()
parser.add_argument('--url', default='http://localhost:5317')
args = parser.parse_args()
out = Path('artifacts/leaderboard')
out.mkdir(parents=True, exist_ok=True)
suffix = uuid4().hex[:6]
white, black = f'Test Alba {suffix}', f'Test Notte {suffix}'
errors = []

with sync_playwright() as p:
    browser = p.chromium.launch(channel='chrome', headless=True)
    context = browser.new_context(viewport={'width': 1440, 'height': 1000}, reduced_motion='reduce', extra_http_headers={'X-Match-Source': 'test'})
    page = context.new_page()
    page.on('pageerror', lambda error: errors.append(str(error)))
    page.goto(args.url, wait_until='networkidle')
    page.get_by_role('button', name='Con un amico').click()
    page.locator('#friend-access').select_option('local')
    page.locator('#nickname').fill(white)
    page.locator('#friend-nickname').fill(black)
    page.locator('#start-button').click()
    expect(page.locator('#board-status')).to_contain_text('(Bianco): scegli una tua pedina')
    page.locator('[data-square="21"]').click()
    page.locator('[data-square="17"]').click()
    expect(page.locator('#move-count')).to_have_text('1')
    page.locator('[data-square="9"]').click()
    page.locator('[data-square="13"]').click()
    expect(page.locator('#move-count')).to_have_text('2')
    page.get_by_role('button', name='Abbandona la partita').click()
    page.locator('#confirm-button').click()
    expect(page.locator('#resign-button')).to_be_hidden()
    game_url = page.url
    game_id = game_url.split('game=')[1]

    page.get_by_role('button', name='Classifica', exact=True).click()
    expect(page.locator('#leaderboard-dialog')).to_be_visible()
    page.locator('#leaderboard-source').select_option('test')
    page.get_by_role('button', name='Umano vs umano', exact=True).click()
    winning_row = page.locator('#leaderboard-rows tr').filter(has_text=black)
    expect(winning_row.locator('.standing-points')).to_have_text('2')
    expect(page.locator('#leaderboard-matches')).to_contain_text(f'Vince {black}')
    expect(page.locator('#leaderboard-matches .source-badge').first).to_have_text('Test')
    page.locator('#leaderboard-dialog').screenshot(path=str(out / 'desktop.png'))
    page.get_by_role('button', name='AI vs AI', exact=True).click()
    expect(page.locator('#leaderboard-rows')).not_to_contain_text(black)
    page.get_by_role('button', name='Umano vs AI', exact=True).click()
    expect(page.locator('#leaderboard-rows')).not_to_contain_text(black)
    page.get_by_role('button', name='Generale', exact=True).click()
    expect(page.locator('#leaderboard-rows')).to_contain_text(black)
    page.locator('#leaderboard-source').select_option('benchmark')
    expect(page.locator('#leaderboard-rows')).not_to_contain_text(black)
    page.locator('#leaderboard-source').select_option('web')
    expect(page.locator('#leaderboard-rows')).not_to_contain_text(black)
    page.keyboard.press('Escape')
    expect(page.locator('#leaderboard-dialog')).not_to_be_visible()
    page.reload(wait_until='networkidle')
    expect(page.locator('#nickname')).to_have_value(white)

    mobile_context = browser.new_context(viewport={'width': 390, 'height': 844}, is_mobile=True, has_touch=True,
                                         reduced_motion='reduce', extra_http_headers={'X-Match-Source': 'test'})
    mobile = mobile_context.new_page()
    mobile.on('pageerror', lambda error: errors.append(str(error)))
    mobile.goto(game_url, wait_until='networkidle')
    mobile.get_by_role('button', name='Classifica', exact=True).tap()
    mobile.locator('#leaderboard-source').select_option('test')
    mobile.get_by_role('button', name='Umano vs umano', exact=True).tap()
    expect(mobile.locator('#leaderboard-rows')).to_contain_text(black)
    assert mobile.locator('body').evaluate('(el) => el.scrollWidth') == 390
    assert mobile.locator('#leaderboard-dialog').bounding_box()['width'] <= 390
    mobile.locator('#leaderboard-dialog').screenshot(path=str(out / 'mobile.png'))
    mobile.get_by_role('button', name='Chiudi classifica').tap()
    expect(mobile.locator('#leaderboard-dialog')).not_to_be_visible()
    assert not errors, errors
    report = {'gameId': game_id, 'source': 'test', 'type': 'human-human', 'passed': [
        'nickname entry and persistence', 'real human moves and resignation', 'two points for a win',
        'all match type filters', 'all source filters', 'desktop and mobile', 'keyboard dismissal'], 'browserErrors': errors}
    (out / 'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report))
    browser.close()
