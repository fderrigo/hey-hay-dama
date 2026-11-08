# AGENTE-C: piano della sessione comune

Baseline congelata: Opale C manual-v2, SHA256 47905feda728fb39c07e8c3a23ff903b422ca0189bcc1d0b4c2a4461db6318b8.

Diagnosi verificata: 18 partite, 1823 mosse, 9 aperture abbinate. Ogni motore 3 vittorie, 6 patte e 3 sconfitte. Opale: 581 stati dopo la propria mossa verificati; due sottostime di prese triple, in una patta e una vittoria. Nessuna causalita dimostrata con le sconfitte.

## Modifiche candidate, esclusivamente nella finestra 30-45

1. Promozione nella presa futura, suggerita da AGENTE-B. Aggiungere il delta dama-pedina (310-100=210) ai motivi di presa che terminano sulla riga di promozione. Mantenere precedenze di lunghezza e pezzo catturante; preservare separatamente il canale materiale catturato per le verifiche. Coefficienti immediato .90 e differito .15 invariati. Non modificare pesi in base ai risultati.
2. SCARTATO prima dell'implementazione: ritorno quieto di dama, discusso con AGENTE-A. Per mantenere distinta la direzione di sviluppo richiesta dall'utente, Opale conserva 128 input di sola scacchiera, senza recentMoves, memoria o casualita. Klinamite esplora la storia, Agata i bonus di avanzamento. Il caso de610040, ply127 (14-11 e 19-15 entrambi 824.1) resta un limite documentato, non risolto.

Non aggiungere triple o trappole da casa vuotata in questa revisione senza una verifica sufficiente. Non promettere che rompere un ciclo converta una patta in vittoria.

## Verifica e congelamento

- Fixture regole: promozione semplice/doppia, nessuna promozione aggiuntiva per una dama, precedenza di presa piu lunga rispetto a quella promuovente.
- Nessun cambiamento al protocollo o alla codifica degli input; simmetria colore/rotazione e ID opachi restano verificati.
- Ricostruzione di replay gia registrati: sola diagnosi, nessuna nuova partita o aggiornamento automatico dei pesi.
- Build isolata e HTTP sintetico. Il runner unico di AGENTE-A gioca le sole partite concordate, tutte registrate.
- Versione, SHA256, endpoint e istruzioni di avvio pubblicati entro minuto45; servizio congelato durante conferma.
