const dialog = document.querySelector('#leaderboard-dialog');
const source = document.querySelector('#leaderboard-source');
const content = document.querySelector('#leaderboard-content');
const summary = document.querySelector('#leaderboard-summary');
const rows = document.querySelector('#leaderboard-rows');
const matches = document.querySelector('#leaderboard-matches');
const typeNames = { 'human-human': 'Umano vs umano', 'ai-ai': 'AI vs AI', 'human-ai': 'Umano vs AI' };
const sourceNames = { web: 'Web', test: 'Test', benchmark: 'Benchmark' };
const dates = new Intl.DateTimeFormat('it-IT', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' });
let type = 'all';
let requestNumber = 0;

function node(tag, text, className) {
  const element = document.createElement(tag);
  if (text !== undefined) element.textContent = text;
  if (className) element.className = className;
  return element;
}

async function load() {
  const request = ++requestNumber;
  content.setAttribute('aria-busy', 'true');
  try {
    const response = await fetch(`/api/leaderboard?${new URLSearchParams({ source: source.value, type })}`, { signal: AbortSignal.timeout(10000) });
    if (!response.ok) throw new Error('Classifica non disponibile. Premi Aggiorna per riprovare.');
    const data = await response.json();
    if (request !== requestNumber) return;
    content.hidden = false;
    summary.textContent = `${data.totalStarted} partite registrate · ${data.totalFinished} concluse · ${data.inProgress} in corso${data.interrupted ? ` · ${data.interrupted} interrotte` : ''}`;
    rows.replaceChildren(...data.entries.map(player => {
      const row = node('tr');
      const identity = node('td');
      const name = node('span', player.name, 'standing-name');
      const kind = node('span', player.kind === 'ai' ? 'AI' : 'Umano', 'participant-kind');
      identity.append(name, kind);
      row.append(node('td', player.rank, 'standing-rank'), identity, node('td', player.points, 'standing-points'),
        node('td', player.played), node('td', player.wins), node('td', player.draws), node('td', player.losses), node('td', player.inProgress));
      return row;
    }));
    document.querySelector('#leaderboard-empty').hidden = data.entries.length !== 0;
    matches.replaceChildren(...data.recentMatches.map(match => {
      const row = node('li', undefined, 'recorded-match');
      const matchup = node('div', undefined, 'recorded-players');
      matchup.append(node('strong', match.white?.name || 'Posto libero'), node('span', 'vs'), node('strong', match.black?.name || 'Posto libero'));
      const detail = node('div', undefined, 'recorded-detail');
      const time = node('time', dates.format(new Date(match.startedAt)));
      time.dateTime = match.startedAt;
      detail.append(node('span', sourceNames[match.source], `source-badge source-${match.source}`), node('span', typeNames[match.type]), time);
      const outcome = match.status === 'finished' ? match.winner === 0 ? 'Patta' : `Vince ${match.winner === 1 ? match.white.name : match.black.name}`
        : match.status === 'waiting' ? 'In attesa' : match.status === 'interrupted' ? 'Interrotta' : 'In corso';
      row.append(matchup, node('span', outcome, 'recorded-result'), detail);
      return row;
    }));
  } catch (error) {
    if (request !== requestNumber) return;
    content.hidden = true;
    summary.textContent = error.name === 'TimeoutError' ? 'Il server non risponde. Premi Aggiorna per riprovare.'
      : 'Classifica non disponibile. Premi Aggiorna per riprovare.';
  } finally { if (request === requestNumber) content.setAttribute('aria-busy', 'false'); }
}

document.querySelectorAll('[data-open="leaderboard-dialog"]').forEach(button => button.addEventListener('click', load));
document.querySelectorAll('[data-match-type]').forEach(button => button.addEventListener('click', () => {
  type = button.dataset.matchType;
  document.querySelectorAll('[data-match-type]').forEach(tab => tab.setAttribute('aria-pressed', tab === button));
  load();
}));
source.addEventListener('change', load);
document.querySelector('#leaderboard-refresh').addEventListener('click', load);
setInterval(() => { if (dialog.open && !document.hidden) load(); }, 5000);
