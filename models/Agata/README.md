# Agata

Rete neurale per la dama italiana, compatibile con `dama-model/v1`. I pesi sono scritti per costruzione: niente training, fine-tuning o dati di partite.

## Avvio

Serve .NET SDK 10. Nessuna dipendenza esterna per il servizio.

```
dotnet run --project models/Agata/Agata -c Release
```

Ascolta su `http://127.0.0.1:9002/choose-move`. Facoltativi: `Agata__Seed` per partite ripetibili, `Agata__Temperature` (0 = sempre la mossa di valore massimo), `Agata__ApiKeyEnvironmentVariable` per richiedere un token.

## Configurazione per `Arena:Models`

```json
{
  "Id": "agata",
  "Name": "Agata",
  "Description": "Rete con pesi costruiti direttamente, senza training e senza ricerca.",
  "Kind": "http",
  "Endpoint": "http://127.0.0.1:9002/choose-move",
  "MaxThinkTimeMs": 1000,
  "Enabled": true
}
```

## Architettura

- **Input:** 160 valori, 5 piani da 32 caselle (pedine e dame proprie, pedine e dame avversarie, vuoto), sempre dal punto di vista di chi muove.
- **Rete:** v9, 4.525 neuroni ReLU e 25.994 connessioni, feed-forward sparsa con collegamenti che saltano strati. Uscita lineare: valore in centesimi di pedina.
- **Decisione:** valuta la posizione dopo ogni mossa di `legalMoves` e campiona da una softmax sui valori (temperatura 10). Non è deterministica, salvo seed fisso.
- **Conoscenza:** tutta in `Agata/Models/agata-v9.json`. `agata-v1.json`, `agata-v3.json`, `agata-v7.json` e `agata-v8.json` restano come riferimento. Il codice esegue solo il forward pass.

Componenti distinti dalla rete: l'applicazione di una mossa ricevuta (copia percorso e prese inviati dal gioco) e il campionamento. Nessuna ricerca delle continuazioni, tabella di aperture o finali, servizio esterno.

## Come sono costruiti i pesi

`WeightBuilder/NetworkBuilder.cs` genera il file. Rigenerandolo si ottiene lo stesso contenuto, e un test lo verifica.

| Strato | Funzione |
| --- | --- |
| 1 | Condizioni sulle caselle: una ReLU realizza un AND di caselle presenti e assenti (pedina sostenuta, bloccata, via libera, passi, prese da 1 a 3 pezzi, riprese). |
| 2 | Prese sicure: presa senza ripresa. |
| 3 | Canali per lato: materiale, avanzamento, centro, guardia, sostegno, mobilità. |
| 4 | OR saturante, fasi di gioco dal conteggio dei pezzi, assenza di mosse. |
| 5 | Porte di fase: ogni canale passa solo nella sua fase. |
| Uscita | Combinazione lineare con valori scelti ragionando sul gioco. |

## Versioni

Ogni versione è scritta a mano in `NetworkBuilder`, senza training né ottimizzazione automatica. `Agata.Confronto` misura pesi congelati: non cambia alcun peso.

| Versione | Cambio | Esito |
| --- | --- | --- |
| v3 | Prese come incrementi, riprese dalla casa vuotata, motivo `own:trap` | Adottata, superata da v7 |
| v4 | `own:landingTrap` e `promote` | Scartata: `landingTrap` peggiora, `promote` non cambia |
| v5 | `own:trap` solo su presa singola | Scartata: pari a v3 |
| v6 | Bonus per pezzi nemici rimasti | Scartata: nessun effetto a una semimossa, le prese sono forzate |
| v7 | `own:tradeOffered`: chi è avanti vuole i cambi | Adottata, superata da v9 |
| v8 | `advance` e `runner` ridotti: pedina in settima sotto la dama, promozione guadagna almeno 25 | Adottata, superata da v9 |
| v9 | `captureKing` e `capturePromote`: la presa vale per cosa prende | In uso |

`agata-v2.json`, prodotta con aggiornamenti dei pesi durante il gioco, non fa parte del motore.

Misure in `results/confronto/misure.jsonl`: colori appaiati, aperture casuali di 4 semimosse con seed fisso, 98 partite per rivale, temperatura 10.

## Test

```
dotnet test models/Agata/Agata.Tests -c Release
```

46 test: pesi identici al generatore, rete feed-forward, simmetria fra colori, significato dei neuroni, applicazione mosse confrontata con `Dama.Engine`, softmax e protocollo (entrambi i colori, presa multipla, percorsi con stessi estremi, promozione, concorrenza, errori, token).

`e2e.py` copre la sezione 8 delle specifiche sul server di gioco reale, con modelli aggiunti solo tramite variabili d'ambiente.

## Limiti

Nel [girone esterno del 7 novembre 2026](../../arena/kingsrow/2026-11-07/resoconto.md),
Agata v9 invariata (temperatura 10, seed 731941) ha perso 18 partite su 18
contro Kingsrow Italian 1.19e, nove per colore, senza patte o interruzioni.
Kingsrow impiega ricerca e un libro di aperture; questo confronto ha vincoli
diversi dalla sfida originaria e non comporta alcun training di Agata.

- **Niente calcolo in profondità:** vede una sola mossa più gli schemi di presa codificati. `own:trap` copre una sola forma di sacrificio.
- **Niente memoria:** non usa ripetizioni né contatore delle 80 semimosse. Può ripetere posizioni e mancare vittorie.
- **Indipendenza parziale:** alcuni valori dei pesi vengono da un valutatore precedente, poi eliminato. Prima di scriverli erano già noti risultati parziali di quel valutatore contro Ambra.
