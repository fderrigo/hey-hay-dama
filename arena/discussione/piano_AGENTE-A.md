# Piano AGENTE-A — Klinamite v2

1. Creare un servizio proprio in `models/Klinamite`, protocollo `dama-model/v1`, senza modificare il motore condiviso o i modelli degli altri agenti.
2. Sostituire la rete a un solo strato con una rete feed-forward multi-livello: 160 input (pedine/dame proprie e avversarie, case vuote), condizioni ReLU, OR saturati per vittima, uscita lineare.
3. Scrivere direttamente pesi e bias tramite un costruttore deterministico. Materiale 100/300; avanzamento, centro, sostegno e mobilità con coefficienti posizionali moderati e simmetrici.
4. Valutare ogni posizione risultante dal lato che deve muovere e scegliere `-Evaluate(next, -turn)`. Una presa disponibile a chi muove vale più di una minaccia di chi attende.
5. Codificare en-prise per pedina/dama, con attaccante pedina/dama, vittima e atterraggio libero. Una pedina non rileva mai una dama. Per ogni vittima usare `ReLU(somma)-ReLU(somma-1)`; penalità moderata per pezzi propri esposti e forte per pezzi avversari esposti al tratto.
6. Verificare con fixture: rotazione e scambio colori, pedina contro dama, due attaccanti sulla stessa vittima, due vittime alternative, doppia presa reale e presa da una posizione che lascia un pezzo esposto. Non usare esiti per regolare automaticamente alcun peso.
7. Integrare il nuovo servizio nella sola configurazione di Klinamite, compilare, eseguire i test e provare richieste reali del protocollo per entrambi i colori. Nessuna partita di confronto senza una successiva autorizzazione esplicita.
8. Registrare ogni cambiamento in `arena/discussione/changelog_AGENTE-A.md`, indicando il consiglio di AGENTE-B o AGENTE-C che lo ha ispirato. Al minuto 40 fermarsi e aggiungere il riepilogo di cinque righe.

Limite dichiarato: l'OR per vittima non trasforma automaticamente prese alternative in un massimo e non risolve da solo il sacrificio con casa di partenza vuotata. Non presentare questi casi come risolti senza un motivo dedicato.
