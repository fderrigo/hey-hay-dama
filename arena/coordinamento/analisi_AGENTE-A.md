# Analisi AGENTE-A — girone diagnostico

- 18 partite complete, 18 esiti naturali, zero errori di modello. Audit C: 1823 mosse e scacchiere verificate.
- Klinamite v2: 3 vittorie, 6 patte, 3 sconfitte, 12 punti. Campione troppo piccolo per concludere parità o superiorità.
- Tutte le sconfitte (`1b8e30ce`, `3ceb6fce`, `147f3798`) terminano per blocco. Nei replay sono visibili prese multiple seguite da controprese multiple.
- Tutte le patte Klinamite sono per ripetizione. In `5117c871` e `f253bc0f` Klinamite era in vantaggio materiale, rispettivamente +4 e +2.
- Gli ultimi cicli mostrano spesso il ritorno immediato dello stesso pezzo sulla casa occupata due semimosse prima.
- La promozione durante una presa è già applicata da `Policy.After` tramite `Promotes`; aggiungere una fixture per impedire regressioni.

Revisione dopo l'audit offline: in `3ceb6fce` ply21 la doppia `23x14x7` era l'unica mossa legale. Il motivo casa-vuotata è già rilevato da v2 e la modifica tattica proposta è scartata; il danno nasce a ply19, fuori da quell'orizzonte. In `1b8e30ce` ply29 anche `22x15` era una presa obbligatoria.

Piano v3: input di ritorno quieto immediato della dama, derivato da `recentMoves` con fallback neutro per ID opachi, e gate di vantaggio materiale interno alla rete. In `5117c871` ply83 il ritorno `13-10` è pari a `13-18` (536); in `f253bc0f` ply90 `18-22` vale 328 contro 324. Peso manuale `-5`, quindi sufficiente in entrambi i casi ma molto sotto una pedina. In `de610040` ply135 il ritorno è pari alle alternative ma Klinamite è in svantaggio: il gate deve lasciare la scelta invariata. Nessuna ricerca o ottimizzazione automatica; v2 conservato.
