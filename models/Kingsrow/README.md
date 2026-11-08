# Kingsrow Italian in Hey Hay Dama

Adattatore gratuito C#/.NET 10 per il motore esterno Kingsrow Italian x64,
attraverso la DLL CheckerBoard e il contratto HTTP `dama-model/v1`.
Non richiede Python, MCP, account, chiavi API o servizi a consumo.

Kingsrow è un riferimento esterno: usa ricerca e conoscenza precalcolata e
non rispetta i vincoli della sfida originale delle reti a pesi manuali.
Non trasferiamo i suoi pesi, le sue mosse o i suoi database nei tre modelli.

## Preparazione (Windows x64)

1. Scaricare gratuitamente Kingsrow Italian x64 dal [sito dell'autore](https://www.edgilbert.org/ItalianCheckers/KingsRowItalian.htm).
2. Installarlo localmente oppure estrarre il pacchetto con [innoextract](https://constexpr.org/innoextract/).
3. Conservare insieme `kr_italian64.dll`, `kr_italian.odb`, `weights_ital_v2.bin`,
   `weights_nk_ital_v2.bin` e una copia di `egdb64.dll` del pacchetto.
   I database aggiuntivi dei finali non sono necessari e non sono stati usati nel girone.
4. Dalla radice della repository, avviare:

```powershell
dotnet run --project models/Kingsrow -- --urls http://127.0.0.1:9015 --Kingsrow:Dll C:/percorso/engines/kr_italian64.dll
```

In un secondo terminale avviare il web con `--Arena:Models:4:Enabled true`
oltre alle configurazioni degli altri modelli. L'indice 4 corrisponde alla voce
`kingsrow` in `src/Dama.Web/appsettings.json`. Il motore rimane disabilitato
per impostazione predefinita perché un clone non include la DLL e il container
Linux non può caricarla. Il servizio Windows può essere raggiunto dal web
anche su un altro host modificando l'endpoint amministrativo.

La DLL viene caricata soltanto dal percorso configurato dall'amministratore.
Le richieste sono serializzate: non si presume che la libreria nativa sia thread-safe.
Il risultato deve coincidere con una transizione legale dell'arbitro condiviso;
non viene mai sostituito con una mossa di un altro motore.
Il tempo di ricerca richiesto è il budget HTTP meno 100 ms, minimo 50 ms.
Ogni chiamata segnala un nuovo contesto al motore; ripetizioni e patte sono
gestite dall'arbitro, senza una cronologia completa fornita a Kingsrow.
Questo limite può influire sulla forza nei finali ed è parte del protocollo della prova.

## Verifica e girone

Con tutti e quattro i servizi avviati:

```powershell
dotnet run --project experiments/Dama.KingsrowMatch -- --check
dotnet run --project experiments/Dama.KingsrowMatch -- arena/kingsrow/nuova-esecuzione
dotnet run --project experiments/Dama.KingsrowMatch -- --audit arena/kingsrow/2026-11-07
```

Il primo comando usa soltanto posizioni sintetiche, senza partite o punteggi.
Il secondo avvia **54 partite reali registrate**: 18 contro ciascun motore
iscritto in `arena/coordinamento/iscrizioni`, 9 per colore. Richiede autorizzazione
esplicita al confronto; per il girone del 7 novembre l'utente l'ha fornita.
Le prime sei semimosse sono aperture pseudocasuali, con nove seed comuni e
colori invertiti. Kingsrow cerca per 200 ms; gli altri hanno budget di 1000 ms.
Il limite di 400 semimosse produce un'interruzione senza punti, non una patta.
Ogni stato e mossa sono conservati in `games.jsonl`; nessun apprendimento.

I controlli aggiuntivi `python tests/kingsrow_protocol.py` e
`python tests/browser_kingsrow.py` non creano partite: verificano il contratto,
gli input errati, il selettore web e il risultato finale tramite fixture sintetiche.
Il controllo browser richiede Playwright e Chrome installati; tutti i suoi
contesti dichiarano `X-Match-Source: test`.

## Provenienza e licenza

Il download ufficiale si dichiara gratuito. Non è stata trovata nel pacchetto
un'autorizzazione esplicita a redistribuire i binari: **non sono inclusi nel repository**.
La gratuità del software non equivale a una licenza open source. Questa
integrazione locale non costituisce una verifica dei permessi per offrire
Kingsrow come servizio pubblico; prima di tale pubblicazione vanno chiariti.
Non è stato effettuato alcun deploy.

Pacchetto della prova: `KingsrowItalianSetup64.1.19e.exe`, scaricato dal sito
dell'autore il 7 novembre 2026, SHA256
`08e860d2d6f83d3a175ccc05a1ccfbdddafe48d1b8c874314def67e5fc37c480`.
La DLL si identifica come `Kingsrow Italian(x64) 1.19e`, gametype `22`, SHA256
`91fbbd16ce36c2997493318b3ee9872bd564930d3fd6c2586d1bb82442e05de3`.
La compatibilità della disposizione CheckerBoard è stata confrontata con
l'[implementazione pubblica di pydraughts](https://github.com/AttackingOrDefending/pydraughts/tree/main/draughts/engines/checkerboard_extra).
