# AGENTE-B — Agata

- Versione primo girone: `agata-v7`. Rete sparsa ReLU a 160 input, 2753 neuroni, 18814 connessioni, pesi scritti a mano da `NetworkBuilder`.
- Pesi: `models/Agata/Agata/Models/agata-v7.json`.
- SHA256: `61b2fe29bbe97e3cbb1f54266112885927f2555f233194cf0cc63f09ab4e370d`
- Servizio: `http://127.0.0.1:9012/choose-move`. Sulla porta 9002 gira ancora una copia v1: non usarla.
- Scelta: softmax a temperatura 10 sui valori dopo ogni mossa legale. Nessuna ricerca.
- Riproducibilità: `Agata__Seed=731941`. Il generatore è unico per processo, quindi la ripetibilità vale solo con partite in sequenza e stesso ordine di richieste.
- Avvio: `dotnet Agata.dll --urls http://127.0.0.1:9012` da `artifacts/agata-service`, con `Agata__Seed=731941` e `Agata__Temperature=10`.
- Verifica: 37 test superati; pesi identici al builder.
- Impegno: nessuna modifica ai pesi del primo girone dopo il congelamento; nessun ciclo automatico esiti→pesi.
- ID partecipante nel registro: `agata` (stabile). Versione `v7` e hash in questo manifest e nei verbali.

## Cosa distingue Agata

Rete ReLU sparsa a canali scritti a mano, con presenza di motivo saturata (`AtLeastOne`), non massimo di materiale sui percorsi. Nessuna memoria della partita: niente `recentMoves`, niente anti-ritorno. Scelta stocastica softmax T=10. Dama a meno di 2,1 pedine in finale.

## Revisioni previste

- v8, promozione libera. Caso: patta vs klinamite, 141 semimosse, pedina in h7 con g8 libera per 30 turni; 183 turni con promozione libera, 20 giocate.
- v9, solo se c'è tempo: prezzo di vittima dama e di promozione in presa. Casi: ply 66 seed 733961 (20-15) e ply 13 seed 731943 (29-26).

## Freeze v9

Versione `v9`, SHA256 `88555cd90598d43b1c398a8e4b8d2906b290a5d144f1f91d3bc06ce33fca00e6`, 4525 neuroni, 25994 connessioni. Endpoint `http://127.0.0.1:9012/choose-move`, seed 731941, T=10. Sostituisce le righe v7 sopra.
