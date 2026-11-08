const $ = (selector) => document.querySelector(selector);
const $$ = (selector) => [...document.querySelectorAll(selector)];
const icon = (name) => `<svg aria-hidden="true"><use href="#i-${name}"/></svg>`;
const initialBoard = Array.from({ length: 32 }, (_, i) => i < 12 ? -1 : i >= 20 ? 1 : 0);
const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)');
const state = { game: null, models: [], mode: 'human-model', token: null, busy: false, flipped: false, path: [], paused: false, modelError: null, sound: false, modelTimer: null, audio: null };
let toastTimer;
let pendingConfirmation;
const announcedResults = new Set();
const memory = new Map();
const storage = {
  get(key) { try { return localStorage.getItem(key); } catch { return memory.get(key); } },
  set(key, value) { memory.set(key, value); try { localStorage.setItem(key, value); } catch { /* The current tab remains playable in restricted browsers. */ } }
};

async function api(path, body, token = state.token) {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 35000);
  try {
    const headers = { Accept: 'application/json' };
    if (token) headers['X-Game-Token'] = token;
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    const response = await fetch(`/api${path}`, { method: body === undefined ? 'GET' : 'POST', headers, body: body === undefined ? undefined : JSON.stringify(body), signal: controller.signal });
    const data = await response.json().catch(() => ({}));
    if (!response.ok) {
      const error = new Error(data.error || (response.status === 429 ? 'Molte richieste in poco tempo. Attendi qualche secondo.' : 'Il server non ha completato la richiesta. Riprova.'));
      error.status = response.status;
      throw error;
    }
    return data;
  } catch (error) {
    if (error.name === 'AbortError') throw new Error('Il server non risponde in tempo. La partita resta salvata; puoi riprovare.');
    if (error instanceof TypeError) throw new Error('Connessione al tavolo interrotta. Controlla la rete e riprova.');
    throw error;
  } finally { clearTimeout(timeout); }
}

function toast(message, error = false) {
  clearTimeout(toastTimer);
  $('#toast').textContent = message;
  $('#toast').classList.toggle('error', error);
  $('#toast').hidden = false;
  toastTimer = setTimeout(() => { $('#toast').hidden = true; }, error ? 6500 : 3500);
}

function coordinates(square) {
  const index = square - 1;
  const row = Math.floor(index / 4);
  return { row, column: 2 * (index % 4) + row % 2 };
}
function algebraic(square) { const { row, column } = coordinates(square); return `${String.fromCharCode(65 + column)}${8 - row}`; }
function matchesPath(move) { return state.path.every((square, i) => move.path[i] === square); }
function availableMoves() { return state.game?.legalMoves || []; }

function renderBoard() {
  const board = [...(state.game?.position.board || initialBoard)];
  const preview = state.path.length > 1 ? availableMoves().find(matchesPath) : null;
  const pendingCaptures = new Set(preview ? preview.captures.slice(0, state.path.length - 1) : []);
  if (preview) { const movingPiece = board[state.path[0] - 1]; board[state.path[0] - 1] = 0; board[state.path.at(-1) - 1] = movingPiece; }
  const canMove = state.game?.permissions.canMove && !state.busy;
  const last = state.game?.history.at(-1);
  const nextSquares = new Set(state.path.length ? availableMoves().filter(matchesPath).map(m => m.path[state.path.length]) : []);
  const selectable = new Set(canMove ? availableMoves().map(m => m.from) : []);
  const focused = document.activeElement?.dataset.square;
  const tabSquare = focused ? Number(focused) : state.path[0] || [...selectable][0] || 21;
  const fragment = document.createDocumentFragment();
  for (let visualRow = 0; visualRow < 8; visualRow++) {
    for (let visualColumn = 0; visualColumn < 8; visualColumn++) {
      const row = state.flipped ? 7 - visualRow : visualRow;
      const column = state.flipped ? 7 - visualColumn : visualColumn;
      if ((row + column) % 2 !== 0) { const light = document.createElement('div'); light.className = 'square light'; fragment.append(light); continue; }
      const square = row * 4 + Math.floor(column / 2) + 1;
      const piece = board[square - 1];
      const cell = document.createElement('button');
      cell.type = 'button';
      cell.className = 'square dark';
      cell.dataset.square = square;
      cell.tabIndex = square === tabSquare ? 0 : -1;
      cell.classList.toggle('last-move', !!last && (last.path[0] === square || last.path.at(-1) === square));
      cell.classList.toggle('selected', state.path.at(-1) === square);
      cell.classList.toggle('pending-capture', pendingCaptures.has(square));
      cell.classList.toggle('path-step', state.path.slice(1).includes(square));
      cell.classList.toggle('destination', !!canMove && nextSquares.has(square));
      cell.classList.toggle('can-select', selectable.has(square));
      const pieceName = piece === 0 ? 'vuota' : `${Math.abs(piece) === 2 ? 'dama' : 'pedina'} ${piece > 0 ? 'bianca' : 'nera'}`;
      cell.setAttribute('aria-label', `${algebraic(square)}, ${pieceName}${nextSquares.has(square) && canMove ? ', destinazione consentita' : ''}${state.path[0] === square ? ', selezionata' : ''}`);
      const number = document.createElement('span'); number.className = 'square-number'; number.textContent = square; number.setAttribute('aria-hidden', 'true'); cell.append(number);
      if (piece) {
        const token = document.createElement('span');
        token.className = `piece ${piece > 0 ? 'ivory' : 'ebony'}${Math.abs(piece) === 2 ? ' king' : ''}`;
        token.setAttribute('aria-hidden', 'true');
        if (Math.abs(piece) === 2) token.innerHTML = icon('crown');
        cell.append(token);
      }
      fragment.append(cell);
    }
  }
  $('#board').replaceChildren(fragment);
  $('#board').setAttribute('aria-label', `Damiera italiana, vista dal ${state.flipped ? 'Nero' : 'Bianco'}`);
  $('#files').replaceChildren(...(state.flipped ? [...'HGFEDCBA'] : [...'ABCDEFGH']).map(value => { const span = document.createElement('span'); span.textContent = value; return span; }));
  $('#ranks').replaceChildren(...(state.flipped ? [...'12345678'] : [...'87654321']).map(value => { const span = document.createElement('span'); span.textContent = value; return span; }));
  if (focused) $(`#board [data-square="${focused}"]`)?.focus({ preventScroll: true });
}

function humanName(color) {
  const game = state.game;
  if (!game) {
    const ownColor = $('input[name="color"]:checked').value;
    if (state.mode === 'human-human') return color === ownColor ? ($('#nickname').value.trim() || 'Primo giocatore')
      : $('#friend-access').value === 'local' ? ($('#friend-nickname').value.trim() || 'Secondo giocatore') : 'Posto libero';
    return color === ownColor ? 'Tu' : ($('#opponent').selectedOptions[0]?.textContent || 'Opale');
  }
  const player = game[color];
  return player.isHuman && game.access !== 'local' && game.permissions[color] ? 'Tu' : player.name;
}

function renderPlayers() {
  for (const place of ['top', 'bottom']) {
    const color = (place === 'bottom') !== state.flipped ? 'white' : 'black';
    const game = state.game;
    const player = game?.[color];
    const count = (game?.position.board || initialBoard).filter(p => color === 'white' ? p > 0 : p < 0).length;
    const strip = $(`#${place}-player`);
    const colorName = color === 'white' ? 'Bianco' : 'Nero';
    const name = player?.name || humanName(color);
    const ownSeat = game?.access !== 'local' && player?.isHuman && game.permissions[color];
    $(`#${place}-name`).textContent = `${name} · ${colorName}${ownSeat ? ' (tu)' : ''}`;
    $(`#${place}-detail`).textContent = `${color === 'white' ? 'Pedine chiare' : 'Pedine scure'} · ${game ? (player.isHuman ? (player.connected ? 'Giocatore' : 'In attesa di un amico') : 'Modello') : 'Partita da iniziare'}`;
    $(`#${place}-count`).textContent = count;
    strip.querySelector('.player-count .mini-piece').className = `mini-piece ${color === 'white' ? 'ivory' : 'ebony'}`;
    const avatar = strip.querySelector('.player-avatar');
    avatar.className = `player-avatar ${color === 'white' ? 'light-avatar' : 'dark-avatar'}`;
    avatar.innerHTML = icon(player?.isHuman || !game && color === 'white' ? 'crown' : 'spark');
    $(`#${place}-turn`).hidden = !(game?.status === 'active' && game.position.turn === color);
    $(`#${place}-turn`).textContent = state.busy && game?.position.turn === color && !player.isHuman ? 'Riflette…' : 'Turno attuale';
  }
}

function renderHistory() {
  const history = state.game?.history || [];
  $('#move-count').textContent = history.length;
  if (history.length === 0) {
    $('#history').innerHTML = '<div class="history-empty"><span class="empty-diagram" aria-hidden="true"><i></i><i></i><i></i><i></i></span><p>La storia della partita<br>comincia con una mossa.</p></div>';
  } else {
    const rows = [];
    for (let i = 0; i < history.length; i += 2) {
      const row = document.createElement('div'); row.className = 'history-row';
      const turn = document.createElement('span'); turn.textContent = `${i / 2 + 1}.`; row.append(turn);
      for (let j = i; j < i + 2; j++) {
        const item = document.createElement('span');
        item.textContent = history[j]?.notation || '—';
        if (j === history.length - 1) item.className = 'latest';
        if (history[j]) item.title = history[j].path.map(algebraic).join(' → ');
        row.append(item);
      }
      rows.push(row);
    }
    $('#history').replaceChildren(...rows);
    $('#history').scrollTop = $('#history').scrollHeight;
  }
  for (const color of ['white', 'black']) {
    const captures = history.filter(m => m.player === color).reduce((sum, m) => sum + m.captures.length, 0);
    const target = $(`#captured-by-${color}`);
    target.setAttribute('aria-label', `${captures} pezzi catturati dal ${color === 'white' ? 'Bianco' : 'Nero'}`);
    if (!captures) target.innerHTML = '<span class="empty-capture">—</span>';
    else target.replaceChildren(...Array.from({ length: captures }, () => { const p = document.createElement('span'); p.className = `mini-piece ${color === 'white' ? 'ebony' : 'ivory'}`; p.setAttribute('aria-hidden', 'true'); return p; }));
  }
}

function renderStatus() {
  const game = state.game;
  let status = 'Il tavolo è pronto. La prima mossa è tua.';
  let title = "L'inizio di una bella sfida.";
  let caption = 'Scegli il tuo avversario e accomodati al tavolo.';
  let help = 'Seleziona una pedina e poi una delle caselle illuminate.';
  let badge = 'Da iniziare';
  if (game) {
    $('#match-mode').textContent = `${humanName('white')} contro ${humanName('black')}`;
    if (game.status === 'waiting') {
      title = 'Un posto ti aspetta.';
      caption = game.permissions.canJoin ? 'Prendi posto per iniziare questa partita.' : 'Invia il link a un amico. Il tavolo è riservato a voi.';
      const openColor = !game.white.connected ? 'Bianco' : 'Nero';
      status = `In attesa del ${openColor}. ${game.permissions.canJoin ? 'Premi “Prendi posto” per giocare.' : 'Invia il link al secondo giocatore.'}`; badge = 'Tavolo aperto';
      help = `Il posto libero è il ${openColor}. La partita comincia quando l'amico preme “Prendi posto”.`;
    } else if (game.result) {
      const draw = game.result.winner === 'draw';
      const mine = !draw && game.permissions[game.result.winner] && game.access !== 'local';
      title = draw ? 'Una sfida alla pari.' : mine ? 'La partita è tua.' : `Vince ${humanName(game.result.winner)}.`;
      const reasons = { blocked: 'Tutte le pedine avversarie sono state catturate o bloccate.', repetition: 'La stessa posizione si è ripetuta quattro volte.', 'forty-moves': 'Quaranta mosse per parte di sole dame, senza prese.', resigned: 'La partita si è conclusa per abbandono.' };
      caption = reasons[game.result.reason] || 'La partita è conclusa.';
      status = draw ? 'Patta. C’è sempre una prossima partita.' : `${game.result.winner === 'white' ? 'Il Bianco' : 'Il Nero'} vince. Una nuova sfida ti aspetta.`;
      badge = draw ? 'Patta' : 'Conclusa'; help = 'Le mosse restano qui. Puoi condividere la partita o iniziare una nuova sfida.';
    } else {
      const mine = game.permissions.canMove;
      const current = humanName(game.position.turn);
      const modelTurn = !game[game.position.turn].isHuman;
      title = mine ? 'A te la mossa.' : `${current} ${modelTurn ? 'riflette' : 'è al tratto'}.`;
      caption = mine ? 'Osserva il tavolo. La prossima scelta è tua.' : modelTurn ? 'Ogni posizione nasconde una possibilità.' : 'La partita si aggiorna automaticamente.';
      status = mine ? 'Il tuo turno. Seleziona una pedina.' : `${current} ${modelTurn ? 'sta scegliendo la prossima mossa' : 'sta giocando'}…`;
      badge = 'In partita';
      if (game.access === 'local') {
        const colorName = game.position.turn === 'white' ? 'Bianco' : 'Nero';
        title = `Tocca a ${current} (${colorName}).`;
        status = `${current} (${colorName}): scegli una tua pedina, poi una casella illuminata.`;
        help = 'Usate lo stesso mouse a turno. Il Bianco muove per primo; il colore non cambia quando ruotate la damiera.';
      }
      if (mine && game.legalMoves[0]?.captures.length) { status = 'Presa obbligatoria. Segui le caselle illuminate.'; help = 'Le prese hanno la precedenza. In una presa multipla scegli un atterraggio alla volta.'; }
      if (state.path.length > 1) status = 'Continua la presa: scegli il prossimo atterraggio.';
      if (state.paused && modelTurn) { title = 'Un momento di pausa.'; status = 'Sfida in pausa. Riprendi quando vuoi.'; }
      if (!game.permissions.white && !game.permissions.black && !game.permissions.isOwner) { status = `Sei spettatore · Turno del ${game.position.turn === 'white' ? 'Bianco' : 'Nero'}`; help = 'Stai osservando la partita in diretta. Le mosse vengono aggiornate automaticamente.'; }
      if (game.history.at(-1)?.comment) help = game.history.at(-1).comment;
    }
  }
  if (!game) status = 'Partita non iniziata. Premi “Inizia partita” prima di muovere le pedine.';
  else if (game.permissions.canMove && state.path.length === 1) status = 'Pedina selezionata: clicca una casella illuminata per muoverla.';
  $('#board-status').textContent = state.modelError ? 'Il modello non ha completato il turno. Puoi riprovare.' : status;
  $('#match-title').textContent = title;
  $('#match-caption').textContent = caption;
  $('#help-text').textContent = help;
  $('#live-badge').textContent = badge;
  $('#live-badge').classList.toggle('live', game?.status === 'active');
  $('#status-dot').classList.toggle('thinking', !!state.busy && game && !game[game.position.turn].isHuman);
  $('.table-area').classList.toggle('result-glow', !!game?.result);
  $('#share-button').disabled = !game;
  $('#undo-button').disabled = state.busy || !game?.permissions.canUndo;
  $('#undo-button').title = game?.permissions.canUndo ? 'Torna alla tua ultima decisione' : 'Disponibile nelle tue partite contro un modello';
  $('#join-area').hidden = !game?.permissions.canJoin;
  $('#resign-button').hidden = !game || game.status !== 'active' || !(game.white.isHuman && game.permissions.white || game.black.isHuman && game.permissions.black);
  $('#resign-button').disabled = state.busy;
  $('#pause-button').hidden = !game || game.white.isHuman || game.black.isHuman || !game.permissions.isOwner || game.status !== 'active';
  $('#pause-button').innerHTML = `${icon(state.paused ? 'play' : 'pause')}<span>${state.paused ? 'Riprendi' : 'Pausa'}</span>`;
  $('#retry-area').hidden = !state.modelError;
  $('#model-error').textContent = state.modelError || '';
  $('#start-button').disabled = state.busy || state.models.length === 0 && state.mode !== 'human-human';
  $('#start-button span').textContent = game ? 'Nuova partita' : 'Inizia partita';
  $('#mobile-start').hidden = !!game;
  $('#mobile-start').disabled = $('#start-button').disabled;
  $('#mobile-start').textContent = state.mode === 'human-model' ? `Gioca con ${$('#opponent').selectedOptions[0]?.textContent || 'Opale'}` : 'Apri il tavolo';
}

function animateMove(move) {
  if (reducedMotion.matches) return;
  const target = $(`#board [data-square="${move.path.at(-1)}"] .piece`);
  if (!target) return;
  const destination = $(`#board [data-square="${move.path.at(-1)}"]`).getBoundingClientRect();
  const keyframes = move.path.map((square, index) => {
    const rect = $(`#board [data-square="${square}"]`).getBoundingClientRect();
    return { transform: `translate(${rect.x - destination.x}px, ${rect.y - destination.y}px)`, offset: index / (move.path.length - 1) };
  });
  target.animate(keyframes, { duration: Math.min(850, 220 * (move.path.length - 1)), easing: 'cubic-bezier(.22,.68,.3,1)' });
}

function applyGame(game, animate = true) {
  if (state.game?.id === game.id && Date.parse(game.updatedAt) < Date.parse(state.game.updatedAt)) return;
  const previousPly = state.game?.id === game.id ? state.game.position.ply : -1;
  state.game = game;
  state.path = [];
  renderBoard(); renderPlayers(); renderHistory(); renderStatus();
  if (animate && previousPly >= 0 && game.position.ply === previousPly + 1) { animateMove(game.history.at(-1)); playSound(!!game.history.at(-1).captures.length, !!game.result); }
  announceResult(game);
  scheduleModel();
}

function announceResult(game) {
  const dialog = $('#result-dialog');
  const key = `dama:announced-result:${game.id}`;
  if (!game.result) {
    if (dialog.open) dialog.close();
    announcedResults.delete(key);
    try { sessionStorage.removeItem(key); } catch { /* In-memory tracking still works. */ }
    return;
  }
  if (announcedResults.has(key)) return;
  try { if (sessionStorage.getItem(key)) return; } catch { /* Storage is optional. */ }
  const draw = game.result.winner === 'draw';
  const white = game.result.winner === 'white';
  dialog.dataset.outcome = draw ? 'draw' : white ? 'white' : 'black';
  $('#result-kicker').textContent = draw ? 'La partita finisce qui' : 'Ha vinto';
  // Use the actual participant name, including for the local player's own victory.
  $('#result-title').textContent = draw ? 'Patta.' : game[game.result.winner].name;
  $('#result-color').textContent = draw ? 'Una partita alla pari' : `${white ? 'Bianco' : 'Nero'} · ${white ? 'Pedine chiare' : 'Pedine scure'}`;
  const reasons = {
    blocked: 'Le pedine avversarie sono state catturate o non possono più muovere.',
    resigned: 'L’avversario ha abbandonato la partita.',
    repetition: 'La stessa posizione si è ripetuta quattro volte.',
    'forty-moves': 'Quaranta mosse per parte di sole dame, senza prese.'
  };
  $('#result-detail').textContent = reasons[game.result.reason] || 'La partita è conclusa.';
  const confetti = $('.result-confetti');
  confetti.replaceChildren();
  if (!draw && !reducedMotion.matches) {
    for (let i = 0; i < 28; i++) {
      const piece = document.createElement('i');
      piece.style.setProperty('--x', `${(i * 37) % 100}%`);
      piece.style.setProperty('--delay', `${(i % 7) * .09}s`);
      piece.style.setProperty('--spin', `${i % 2 ? 320 : -280}deg`);
      confetti.append(piece);
    }
  }
  announcedResults.add(key);
  try { sessionStorage.setItem(key, '1'); } catch { /* Do not require storage to celebrate. */ }
  dialog.showModal();
}

function scheduleModel() {
  if (state.modelTimer || state.busy || state.paused || state.modelError || !state.game?.permissions.canAdvance) return;
  state.modelTimer = setTimeout(() => { state.modelTimer = null; act('advance'); }, 700);
}

async function act(action, moveId) {
  if (state.busy || !state.game) return;
  clearTimeout(state.modelTimer); state.modelTimer = null;
  state.busy = true; state.path = []; renderBoard(); renderPlayers(); renderStatus();
  let applied = false;
  try {
    const game = await api(`/games/${state.game.id}/${action}`, { expectedPly: state.game.position.ply, ...(moveId ? { moveId } : {}) });
    state.modelError = null; state.busy = false; applied = true;
    applyGame(game);
  } catch (error) {
    if (action === 'advance' && error.status !== 409) state.modelError = error.message;
    else toast(error.message, true);
    if (error.status === 409) await refresh();
  } finally { state.busy = false; if (!applied) renderBoard(); renderPlayers(); renderStatus(); scheduleModel(); }
}

function selectSquare(square) {
  if (state.busy) return;
  if (!state.game) { toast('Prima premi “Inizia partita”. Il Bianco farà la prima mossa.'); return; }
  if (!state.game.permissions.canMove) { toast($('#board-status').textContent); return; }
  if (state.path.length) {
    const continuations = availableMoves().filter(matchesPath).filter(m => m.path[state.path.length] === square);
    if (continuations.length) {
      state.path.push(square);
      const complete = continuations.find(m => m.path.length === state.path.length);
      if (complete) { act('move', complete.id); return; }
      renderBoard(); renderStatus(); return;
    }
  }
  const selectable = availableMoves().some(m => m.from === square);
  if (!selectable) {
    const colorName = state.game.position.turn === 'white' ? 'Bianco' : 'Nero';
    toast(availableMoves()[0]?.captures.length ? 'La presa è obbligatoria: scegli una pedina evidenziata.'
      : `Tocca a ${humanName(state.game.position.turn)} (${colorName}). Scegli una pedina evidenziata, poi la destinazione.`);
  }
  state.path = selectable && state.path[0] !== square ? [square] : [];
  renderBoard(); renderStatus();
}

function updateSetup() {
  const mode = state.mode;
  $$('.mode-button').forEach(button => { const on = button.dataset.mode === mode; button.classList.toggle('selected', on); button.setAttribute('aria-pressed', on); });
  $('#opponent-field').hidden = mode === 'human-human';
  $('#opponent-field label').textContent = mode === 'model-model' ? 'Modello con il Bianco' : 'Il tuo avversario';
  $('#second-model-field').hidden = mode !== 'model-model';
  $('#friend-field').hidden = mode !== 'human-human';
  $('#color-field').hidden = mode === 'model-model';
  $('#nickname-field').hidden = mode === 'model-model';
  $('#friend-nickname-field').hidden = !(mode === 'human-human' && $('#friend-access').value === 'local');
  const ownColorName = $('input[name="color"]:checked').value === 'white' ? 'Bianco' : 'Nero';
  $('#nickname-field label').textContent = mode === 'human-human' ? `Giocatore con il ${ownColorName}` : 'Il tuo nickname';
  $('#friend-nickname-field label').textContent = `Giocatore con il ${ownColorName === 'Bianco' ? 'Nero' : 'Bianco'}`;
  $('#friend-field .field-note').textContent = $('#friend-access').value === 'local'
    ? 'Inserite due nomi diversi e premete “Inizia partita”. Muove per primo il Bianco.'
    : 'Avvia il tavolo e invia il link. L’amico deve aprirlo e premere “Prendi posto”.';
  $('#opponent-description').textContent = state.models.find(m => m.id === $('#opponent').value)?.description || 'Nessun modello abilitato dal gestore.';
  if (!state.game) { $('#match-mode').textContent = mode === 'human-human' ? 'Una sfida tra amici' : mode === 'model-model' ? 'Due intelligenze, un tavolo' : `Tu contro ${$('#opponent').selectedOptions[0]?.textContent || 'Opale'}`; renderPlayers(); }
  renderStatus();
}

async function createGame() {
  if (state.busy) return;
  clearTimeout(state.modelTimer); state.modelTimer = null;
  state.busy = true; renderStatus();
  const color = $('input[name="color"]:checked').value;
  const request = state.mode === 'human-human' ? { white: 'human', black: 'human', access: $('#friend-access').value, humanSide: color }
    : state.mode === 'model-model' ? { white: $('#opponent').value, black: $('#second-model').value, access: 'online', humanSide: 'white' }
      : { white: color === 'white' ? 'human' : $('#opponent').value, black: color === 'black' ? 'human' : $('#opponent').value, access: 'online', humanSide: color };
  request.source = 'web';
  if (state.mode !== 'model-model') {
    const ownName = $('#nickname').value.trim();
    const friendName = $('#friend-nickname').value.trim();
    storage.set('dama:nickname', ownName); storage.set('dama:friendNickname', friendName);
    request[color === 'white' ? 'whiteNickname' : 'blackNickname'] = ownName;
    if (request.access === 'local') request[color === 'white' ? 'blackNickname' : 'whiteNickname'] = friendName;
  }
  try {
    const created = await api('/games', request, null);
    state.token = created.token; state.paused = false; state.modelError = null;
    storage.set(`dama:token:${created.game.id}`, created.token); storage.set('dama:last', created.game.id);
    storage.set(`dama:color:${created.game.id}`, color);
    state.flipped = state.mode !== 'model-model' && color === 'black';
    history.replaceState(null, '', `?game=${created.game.id}`);
    state.busy = false; applyGame(created.game, false);
    if (innerWidth < 701) $('.table-area').scrollIntoView({ behavior: reducedMotion.matches ? 'instant' : 'smooth', block: 'start' });
    if (created.game.status === 'waiting') share();
  } catch (error) { toast(error.message, true); }
  finally { state.busy = false; renderStatus(); scheduleModel(); }
}

async function refresh() {
  if (!state.game) return;
  try {
    const game = await api(`/games/${state.game.id}`);
    if (game.updatedAt !== state.game.updatedAt || game.status !== state.game.status) applyGame(game);
  } catch (error) { if (error.status === 404) toast(error.message, true); }
}

function confirm(title, message, label, action) {
  $('#confirm-title').textContent = title; $('#confirm-message').textContent = message; $('#confirm-button').textContent = label;
  pendingConfirmation = action; $('#confirm-dialog').showModal();
}
function share() {
  if (!state.game) return;
  $('#share-url').value = `${location.origin}/?game=${state.game.id}`;
  $('#share-title').textContent = state.game.status === 'waiting' ? 'C’è posto al tavolo.' : 'La partita si condivide.';
  $('#share-description').textContent = state.game.status === 'waiting'
    ? `${state.game.white.connected ? state.game.white.name : state.game.black.name} gioca con il ${state.game.white.connected ? 'Bianco' : 'Nero'}. Invia questo link all’amico: giocherà con il ${state.game.white.connected ? 'Nero' : 'Bianco'} dopo aver premuto “Prendi posto”.`
    : 'Condividi questo indirizzo per invitare uno spettatore.';
  $('#copy-link').textContent = 'Copia'; $('#share-dialog').showModal();
}
function playSound(capture = false, finished = false) {
  if (!state.sound || !state.audio) return;
  try {
    const context = state.audio; const now = context.currentTime;
    const oscillator = context.createOscillator(); const gain = context.createGain();
    oscillator.type = 'sine'; oscillator.frequency.setValueAtTime(finished ? 523.25 : capture ? 340 : 240, now); oscillator.frequency.exponentialRampToValueAtTime(finished ? 659.25 : 100, now + .12);
    gain.gain.setValueAtTime(.0001, now); gain.gain.exponentialRampToValueAtTime(.09, now + .005); gain.gain.exponentialRampToValueAtTime(.0001, now + (finished ? .5 : .17));
    oscillator.connect(gain); gain.connect(context.destination); oscillator.start(now); oscillator.stop(now + .55);
  } catch { /* Sound is optional; the game remains usable. */ }
}

$('#board').addEventListener('click', event => { const square = event.target.closest('[data-square]'); if (square) selectSquare(Number(square.dataset.square)); });
$('#board').addEventListener('keydown', event => {
  const cell = event.target.closest('[data-square]'); if (!cell) return;
  if (event.key === 'Escape') { state.path = []; renderBoard(); return; }
  const direction = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -4, ArrowDown: 4 }[event.key];
  if (direction === undefined) return;
  event.preventDefault();
  const next = Math.max(1, Math.min(32, Number(cell.dataset.square) + direction * (state.flipped ? -1 : 1)));
  $$('#board [data-square]').forEach(c => { c.tabIndex = Number(c.dataset.square) === next ? 0 : -1; });
  $(`#board [data-square="${next}"]`).focus();
});
$$('.mode-button').forEach(button => button.addEventListener('click', () => { state.mode = button.dataset.mode; updateSetup(); }));
$('#opponent').addEventListener('change', updateSetup);
$('#friend-access').addEventListener('change', updateSetup);
$$('input[name="color"]').forEach(input => input.addEventListener('change', updateSetup));
['#nickname', '#friend-nickname'].forEach(selector => $(selector).addEventListener('input', () => { if (!state.game) renderPlayers(); }));
$('#mobile-start').addEventListener('click', createGame);
$('#start-button').addEventListener('click', () => {
  if (state.game?.status === 'active' && state.game.position.ply > 0) confirm('Un nuovo inizio?', 'Questa partita resta salvata al suo indirizzo. Vuoi aprire un nuovo tavolo?', 'Nuova partita', createGame);
  else createGame();
});
$('#flip-button').addEventListener('click', () => { state.flipped = !state.flipped; renderBoard(); renderPlayers(); });
$('#undo-button').addEventListener('click', () => act('undo'));
$('#pause-button').addEventListener('click', () => { state.paused = !state.paused; clearTimeout(state.modelTimer); state.modelTimer = null; renderStatus(); scheduleModel(); });
$('#retry-button').addEventListener('click', () => { state.modelError = null; state.paused = false; act('advance'); });
$('#resign-button').addEventListener('click', () => confirm('Lasci il tavolo?', 'La partita verrà assegnata al tuo avversario. Potrai iniziare subito una nuova sfida.', 'Abbandona', () => act('resign')));
$('#confirm-button').addEventListener('click', () => { $('#confirm-dialog').close(); pendingConfirmation?.(); pendingConfirmation = null; });
$('#share-button').addEventListener('click', share);
$('#copy-link').addEventListener('click', async () => {
  try { await navigator.clipboard.writeText($('#share-url').value); $('#copy-link').textContent = 'Copiato'; }
  catch { $('#share-url').select(); toast('Seleziona e copia il link mostrato.'); }
});
$('#join-button').addEventListener('click', async () => {
  if (state.busy) return; state.busy = true; $('#join-button').disabled = true;
  try {
    const nickname = $('#join-nickname').value.trim();
    const joined = await api(`/games/${state.game.id}/join`, { nickname });
    storage.set('dama:nickname', nickname); $('#nickname').value = nickname;
    state.token = joined.token; storage.set(`dama:token:${joined.game.id}`, joined.token); storage.set('dama:last', joined.game.id);
    state.flipped = joined.game.permissions.black; state.busy = false; applyGame(joined.game, false);
  } catch (error) { toast(error.message, true); await refresh(); }
  finally { state.busy = false; $('#join-button').disabled = false; renderStatus(); scheduleModel(); }
});
$('#sound-button').addEventListener('click', async () => {
  state.sound = !state.sound;
  if (state.sound) { try { state.audio ??= new AudioContext(); await state.audio.resume(); } catch { state.sound = false; toast('I suoni non sono disponibili in questo browser.'); } }
  $('#sound-button').innerHTML = icon(state.sound ? 'sound' : 'mute');
  $('#sound-button').setAttribute('aria-pressed', state.sound); $('#sound-button').setAttribute('aria-label', state.sound ? 'Disattiva i suoni' : 'Attiva i suoni'); playSound();
});
$$('[data-open]').forEach(button => button.addEventListener('click', () => $(`#${button.dataset.open}`).showModal()));
$$('[data-close]').forEach(button => button.addEventListener('click', () => button.closest('dialog').close()));
$$('dialog').forEach(dialog => dialog.addEventListener('click', event => { if (event.target === dialog) { const rect = dialog.getBoundingClientRect(); if (event.clientX < rect.left || event.clientX > rect.right || event.clientY < rect.top || event.clientY > rect.bottom) dialog.close(); } }));

async function initialize() {
  $('#nickname').value = storage.get('dama:nickname') || `Giocatore-${Math.random().toString(36).slice(2, 6)}`;
  $('#join-nickname').value = $('#nickname').value;
  $('#friend-nickname').value = storage.get('dama:friendNickname') || `Ospite-${Math.random().toString(36).slice(2, 6)}`;
  renderBoard(); renderStatus();
  try {
    const response = await api('/models', undefined, null); state.models = response.models;
    for (const selector of ['#opponent', '#second-model']) {
      $(selector).replaceChildren(...state.models.map(model => new Option(model.name, model.id)));
      $(selector).disabled = state.models.length === 0;
    }
    if (state.models.length > 1) $('#second-model').selectedIndex = 1;
    $('#model-cards').replaceChildren(...state.models.map(model => { const card = document.createElement('section'); card.className = 'model-card'; const title = document.createElement('h3'); title.textContent = model.name; const text = document.createElement('p'); text.textContent = model.description; card.append(title, text); return card; }));
  } catch (error) { toast(error.message, true); $('#opponent').replaceChildren(new Option('Connessione non disponibile', '')); }
  updateSetup();
  // Human rooms and invitations do not depend on loading the model roster.
  const queryId = new URLSearchParams(location.search).get('game');
  const gameId = queryId || storage.get('dama:last');
  if (gameId && /^[a-f0-9]{32}$/.test(gameId)) {
    state.token = storage.get(`dama:token:${gameId}`);
    try {
      const game = await api(`/games/${gameId}`);
      state.flipped = game.access === 'local' && game.permissions.isOwner
        ? storage.get(`dama:color:${gameId}`) === 'black' : game.permissions.black && !game.permissions.white;
      state.mode = game.white.isHuman && game.black.isHuman ? 'human-human' : !game.white.isHuman && !game.black.isHuman ? 'model-model' : 'human-model';
      state.paused = state.mode === 'model-model';
      $('#friend-access').value = game.access;
      if (game.permissions.white || game.permissions.black) {
        const side = state.flipped ? 'black' : 'white';
        $(`input[name="color"][value="${side}"]`).checked = true;
        if (game[side].isHuman) $('#nickname').value = game[side].name;
        if (game.access === 'local' && state.mode === 'human-human')
          $('#friend-nickname').value = game[side === 'white' ? 'black' : 'white'].name;
      }
      history.replaceState(null, '', `?game=${game.id}`); applyGame(game, false); updateSetup();
    } catch (error) { state.token = null; if (queryId) toast(error.message, true); }
  }
}
setInterval(() => { if (state.game && !state.busy && !document.hidden) refresh(); }, 1800);
document.addEventListener('visibilitychange', () => { if (!document.hidden && !state.busy) refresh(); });
initialize();
