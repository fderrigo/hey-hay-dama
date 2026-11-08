# Avventurina

Motore di dama italiana con ricerca, scritto il 7 novembre 2026 per la sfida contro Kingsrow Italian 1.19e
(`arena/kingsrow-challenge`). Agata resta invariata in `models/Agata`: Avventurina è un modello nuovo, con un altro nome.

Regole della sfida concordate fra gli agenti fino alle 14:10 UTC: coefficienti scritti a mano, nessun training né
ottimizzazione automatica; nessun dato, codice, libro, database o risposta di Kingsrow riutilizzati. Le tabelle dei
finali sono calcolate dalle sole regole. Dopo le 14:10 l'utente ha ammesso ogni mezzo tranne copiare Kingsrow:
ricerca multi-thread e ottimizzazione automatica sono disponibili come opzioni, dichiarate come budget diverso.

## Struttura

- `Avventurina.Core`: posizione a bitboard (32 bit per tipo di pezzo), generatore delle mosse con le priorità di
  presa italiane, valutazione, ricerca, tabelle dei finali.
- `Avventurina`: servizio HTTP `dama-model/v1` (porta 9021 da `appsettings.json`).
- `Avventurina.Lab`: verifiche e misure. Registra le partite nella classifica condivisa (`Dama.Competition`).
- `results/`: partite di sviluppo e di conferma (`games.jsonl`, `manifest.json`, `summary.json`), più due
  script di lettura (`traccia.py`, `board.py`).
- `DIARIO.md`: cronologia delle prove, comprese quelle scartate.

## Motore

- Ricerca alfa-beta con variante principale, approfondimento iterativo, tabella di trasposizione (2^21 voci),
  killer e history, riduzione delle mosse tardive con ricerca di verifica.
- Nodi con prese: mai valutazione statica (le prese sono obbligatorie). All'orizzonte: una semimossa in più se
  l'avversario minaccia una presa; altrimenti si provano le mosse che offrono una presa (sacrifici), seguite dalla
  catena di prese forzata.
- Ripetizioni e regola delle 40 mosse di dama valutate patta usando la storia reale della partita, tenuta dal
  servizio per ID partita.
- Valutazione: materiale (dama da 170 a 200 secondo la fase), prima dama, cambi quando si è avanti, avanzamento,
  guardia della riga di fondo, pedine sostenute e bloccate, centro, equilibrio fra le ali, dame centrali e negli
  angoli, pedine libere di promuovere, mobilità. Invariante verificato: la promozione guadagna sempre (`check`).
- Tabelle dei finali fino a 5 pezzi: analisi retrograda per firma di materiale e lato al tratto, 85 tabelle,
  374 milioni di posizioni, circa 8,5 minuti, file mappati in memoria. Directory predefinita
  `%TEMP%\avventurina-tb`, costruite al primo avvio se mancano.

## Uso

```
dotnet run --project models/Avventurina/Avventurina -c Release -- --Avventurina:TablebasePieces=5
dotnet run --project models/Avventurina/Avventurina.Lab -c Release -- check 1000
dotnet run --project models/Avventurina/Avventurina.Lab -c Release -- match --out <dir> --games 12 --seed <n> --source test --think 1100
```

`check` confronta il generatore con l'arbitro `Dama.Engine.Rules` su cammini casuali. `match` gioca contro un
servizio rivale (predefinito Kingsrow su 9025) prendendo un file di lock per tutta la partita; ogni partita è
registrata alla creazione e alla conclusione.

Opzioni del servizio (`Avventurina:*`): `MarginMs`, `TableBits`, `Tablebase`, `TablebaseDirectory`,
`TablebasePieces`, più i parametri usati nelle prove di sviluppo (`Balance`, `Simplify`, `DrawValue`, `NullMove`,
`Futility`, `LogLmr`, `SacPlies`, `IterationLimit`, pesi della valutazione). I valori predefiniti nel codice sono
quelli della versione congelata a7.

Opzioni aggiunte dopo le 14:10: `Threads` (lazy SMP su tabella condivisa senza lock; predefinito 1) e
`TunedWeights` (file di 184 pesi per la valutazione lineare di `Tuned.cs`; nessun file usato nelle conferme).
Il Lab ha i comandi `pbench <file> <thread> <ms>` (profondità raggiunta), `features` e `label` (dati per
l'ottimizzazione).
