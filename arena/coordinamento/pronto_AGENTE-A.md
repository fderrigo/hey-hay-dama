# AGENTE-A — Klinamite

- ID stabile nel registro: `klinamite`; versione conferma: `klinamite-v3`.
- Rete multi-livello a 161 input: i 160 canali v2 sono invariati; il nuovo input rappresenta il ritorno quieto immediato di dama.
- Pesi conferma: `models/Klinamite/Models/klinamite-v3.json`.
- SHA256 conferma: `5ac514e24fd97371011f901beec0ea27d47fbebb62c7a561412bb42e57100e21`.
- Servizio: `http://127.0.0.1:9003/choose-move`, attivo con v3.
- Scelta deterministica: massimo di `-Evaluate(next, -turn, history)`, nessuna temperatura.
- Verifica: 16 test specifici superati; build della soluzione senza errori o avvisi; fixture HTTP con/senza storia e ID opachi riuscite.
- Diagnosi: v2, 160 input, SHA256 `f81a72698e8b0f5ad65d53d3e67ab2d1007676041d51bf4a3d4490cee4d6f97b`, checkpoint conservato.
- Impegno: nessuna modifica ai pesi dopo questo freeze; nessun ciclo automatico esiti→pesi.

## Cosa distingue Klinamite

Rete ReLU multilivello con OR saturo per vittima e massimo del materiale fra prese alternative, fino a triple anche miste. Scelta deterministica. La revisione v3 proposta è l'unica memoria storica della sessione: ritorno quieto immediato di dama da `recentMoves`, gate materiale interno e fallback neutro per ID opachi.

## Revisione prevista

- v3 anti-ritorno, peso manuale -5. Casi propri: `5117c871` ply83 (ritorno in parità) e `f253bc0f` ply90 (ritorno +4); controfixture `de610040` ply135 in svantaggio. Modifica casa-vuotata scartata perché già presente e non causale nel replay esaminato.
