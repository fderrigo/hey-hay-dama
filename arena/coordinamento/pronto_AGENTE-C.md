# AGENTE-C — Opale C

- ID stabile nel registro: `opale`.
- Versione del primo girone: `opale-c-v2`, nome rete `Opale C manual v2`.
- Pesi: `models/Opale.C/Models/opale-c-v2.json`.
- SHA256: `47905feda728fb39c07e8c3a23ff903b422ca0189bcc1d0b4c2a4461db6318b8`.
- Formato: `opale-c-relu/v1`, 128 input, 5640 unita ReLU, 39398 parametri manuali.
- Servizio: `http://127.0.0.1:9013/choose-move`; salute: `http://127.0.0.1:9013/health`.
- Avvio dalla radice: `dotnet models/Opale.C/artifacts/publish/Opale.C.dll --urls http://127.0.0.1:9013`.
- Scelta deterministica: massimo di `-Evaluate(afterstate, nextTurn)`; temperatura zero, nessun RNG o stato di partita.
- Nessun training, ottimizzatore, ricerca delle continuazioni o memoria di partite.
- Il servizio rispetta `thinkTimeMs` della richiesta con margine di 25 ms; nessun cambio di parametri durante il girone.
- Verifica: 17 controlli mirati e 2484 posizioni sintetiche; protocollo HTTP, ID opachi, 32 richieste concorrenti, pacchetto pubblicato verificati.
- Ruolo concordato: audit indipendente degli accoppiamenti/verbali/registro e fixture tattiche; nessun runner di partite avviato da AGENTE-C.
- Congelamento: i pesi iniziali restano identici all'hash sopra fino alla finestra di miglioramento manuale concordata.

## Versione congelata per conferma: manual-v3

- SHA256: `3e25fd78df09920a87f2b0c6cdcc53084c5dcfbf7747dd64363a314fe8dfc1e9`.
- Pesi: `models/Opale.C/Models/opale-c-v3.json`.
- Endpoint aggiornato: `http://127.0.0.1:9014/choose-move`; health sulla stessa porta.
- Servizio v3 avviato e verificato; v2 resta su9013 per eventuali verifiche del baseline, esclusa dalla conferma.
- Avvio: `dotnet models/Opale.C/artifacts/v3-build/bin/Opale.C/release/Opale.C.dll --weights C:/Users/filip/source/repos/weight-space-games/models/Opale.C/Models/opale-c-v3.json --urls http://127.0.0.1:9014`.
- Distinzione: rete deterministica di sola scacchiera, 128 input, massimo dei percorsi di presa fino a2 con precedenze. Niente storia o RNG. 5648 unita,39484 parametri.
- Unica modifica: OR di promozione sui percorsi ammessi, guadagno210 dal materiale preesistente310-100, coefficienti .90/.15 invariati. Nessun codice/peso degli altri copiato.
- Casi propri: 7886715a ply40 e a2e056b9 ply39, prese avversarie promuoventi sottovalutate.
- Verifica:22 controlli,2484 posizioni sintetiche con arbitro;16 posizioni oltre2 prese escluse esplicitamente. HTTP ID opachi,malformati,32 richieste concorrenti superati. Nessuna partita in queste verifiche.
- Limiti invariati: triple oltre orizzonte,nessuna prevenzione generale dei cicli. Migliore win rate da verificare nel girone comune.
