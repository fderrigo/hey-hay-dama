# Specifiche di integrazione dei modelli

Versione del protocollo: **`dama-model/v1`**.

Questo documento definisce il contratto che l'applicazione implementa per collegare avversari alla dama italiana. Il gestore registra i modelli nella configurazione ASP.NET Core; i giocatori possono scegliere soltanto quelli abilitati. Le credenziali e gli indirizzi dei servizi non vengono inviati al browser.

## 1. Requisito minimo

**Requisito dell'esperimento:** il modello deve essere una rete neurale costruita da zero, con architettura, pesi e bias scritti direttamente. Non sono ammessi training, fine-tuning, ottimizzazione automatica dei parametri o pesi preaddestrati. I pesi possono essere prodotti da un costruttore deterministico, senza apprendimento. Durante la partita le valutazioni provengono dalla rete; minimax e altri motori di ricerca possono essere usati soltanto come riferimenti separati nei benchmark. Codifica, legalità, simulazione della singola mossa candidata e riconoscimento dei risultati terminali appartengono alle regole del gioco.

Un modello esterno deve esporre un endpoint **HTTP POST che riceve e restituisce JSON UTF-8**. Riceve una posizione e l'elenco completo delle mosse legali, e restituisce l'identificativo di una di queste mosse entro il tempo configurato.

Il servizio può essere scritto in qualsiasi linguaggio. Non serve implementare ASP.NET Core dentro il modello. L'applicazione di gioco, le regole, la gestione delle partite e il server web sono in ASP.NET Core; il client è JavaScript vanilla. Il protocollo verifica mosse e formato: il rispetto dei requisiti neurali deve essere verificato sul sorgente e sui pesi del modello dal gestore.

Il server di gioco è l'autorità sulle regole. Il modello **non modifica la scacchiera**, non dichiara il risultato e non decide quali mosse siano legali. Una presa multipla è una sola mossa completa. La stessa interfaccia vale per umano–modello e modello–modello.

## 2. Configurazione riservata al gestore

Configurare `Arena:Models` in `src/Dama.Web/appsettings.json`, in un file di configurazione ASP.NET Core dell'ambiente o mediante variabili d'ambiente. Riavviare il server dopo una modifica. Non esiste un endpoint pubblico per registrare modelli o cambiare questi valori.

```json
{
  "Arena": {
    "Models": [
      {
        "Id": "opale",
        "Name": "Opale",
        "Description": "Rete con pesi progettati direttamente",
        "Kind": "builtin",
        "Strategy": "network",
        "WeightsPath": "Models/opale-v1.json",
        "MaxThinkTimeMs": 1000,
        "Enabled": true
      },
      {
        "Id": "mio-modello",
        "Name": "Il mio modello",
        "Description": "Servizio esterno configurato dal gestore",
        "Kind": "http",
        "Endpoint": "http://127.0.0.1:9001/choose-move",
        "ApiKeyEnvironmentVariable": "DAMA_MODEL_TOKEN",
        "MaxThinkTimeMs": 5000,
        "Enabled": true
      }
    ]
  }
}
```

| Campo | Requisito |
| --- | --- |
| `Id` | Identificativo univoco, 1–48 caratteri minuscoli, numeri e trattini. È quello salvato nella partita. |
| `Name` | Nome pubblico, 1–60 caratteri. |
| `Description` | Descrizione pubblica breve, massimo 180 caratteri. |
| `Kind` | `builtin` oppure `http`. |
| `Enabled` | Solo i modelli abilitati sono disponibili per nuove partite. |
| `MaxThinkTimeMs` | Tempo massimo della decisione: da 100 a 30000 millisecondi. Include rete e inferenza per i servizi HTTP. |
| `Endpoint` | Per `http`: URL assoluto HTTP/HTTPS, configurato esclusivamente dal gestore. HTTPS raccomandato fra host diversi. Nessun reindirizzamento automatico. |
| `ApiKeyEnvironmentVariable` | Facoltativo, per `http`: nome della variabile che contiene il token. Il server aggiunge `Authorization: Bearer <token>`. Il token non va scritto in questo file. Se la variabile configurata manca, l'avvio fallisce. |
| `Strategy` | Per `builtin`: `network`. La ricerca non è disponibile come strategia giocabile. |
| `WeightsPath` | Per `builtin`: file dei pesi, relativo alla cartella dell'applicazione, oppure percorso assoluto. |

Esempio di variabile ASP.NET Core: `Arena__Models__1__Endpoint`. In Docker, `127.0.0.1` indica il contenitore del gioco: usare il nome DNS del servizio del modello sulla stessa rete.

## 3. Regole e codifica della posizione

Identificativo regole: **`italian-draughts-8x8`**. Il Bianco comincia. La damiera ha una casella scura in basso a destra, vista dal Bianco. La numerazione è fissa, anche quando il client ruota la tavola:

| Riga | Caselle giocabili, da sinistra a destra |
| --- | --- |
| 8 | 1=A8, 2=C8, 3=E8, 4=G8 |
| 7 | 5=B7, 6=D7, 7=F7, 8=H7 |
| 6 | 9=A6, 10=C6, 11=E6, 12=G6 |
| 5 | 13=B5, 14=D5, 15=F5, 16=H5 |
| 4 | 17=A4, 18=C4, 19=E4, 20=G4 |
| 3 | 21=B3, 22=D3, 23=F3, 24=H3 |
| 2 | 25=A2, 26=C2, 27=E2, 28=G2 |
| 1 | 29=B1, 30=D1, 31=F1, 32=H1 |

`board` contiene esattamente 32 interi. L'elemento `board[0]` corrisponde alla casella 1; `board[31]` alla 32. I valori **non** sono relativi al giocatore:

| Valore | Contenuto |
| --- | --- |
| `0` | Vuoto |
| `1` | Pedina bianca |
| `2` | Dama bianca |
| `-1` | Pedina nera |
| `-2` | Dama nera |

Le pedine muovono e catturano soltanto in avanti e non catturano dame. Le dame hanno movimento corto. Le prese sono obbligatorie: precedenza al maggior numero di vittime, poi al pezzo catturante dama, poi al maggior numero di dame catturate, poi alla sequenza che incontra prima le dame al primo punto di differenza. I pezzi catturati rimangono d'ostacolo fino alla fine della presa. La promozione conclude la mossa.

Il server applica automaticamente la patta alla quarta occorrenza della stessa posizione con lo stesso giocatore al tratto, e dopo 80 semimosse consecutive di sole dame senza prese, con almeno una dama per parte. L'automatismo è un adattamento per il gioco web delle procedure di richiesta/arbitraggio. La partita si conclude anche per abbandono. Regole di riferimento: [FID, regolamento](https://www.fid.it/regolamenti/capo1.htm) e [regolamento tecnico 2008](https://www.fid.it/regolamenti/2008/RegTec_CAPO_I.pdf).

## 4. Richiesta al modello

```http
POST /choose-move HTTP/1.1
Content-Type: application/json
Accept: application/json
Authorization: Bearer <token-se-configurato>
```

Esempio valido di apertura (nessuna mossa precedente):

```json
{
  "protocolVersion": "dama-model/v1",
  "rules": "italian-draughts-8x8",
  "requestId": "e24df8e24fd64a08b845b5412793941d",
  "gameId": "c55c39e9120c4d99b174f4aad0cdf2a5",
  "player": "white",
  "position": {
    "board": [-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,0,0,0,0,0,0,0,0,1,1,1,1,1,1,1,1,1,1,1,1],
    "turn": "white",
    "ply": 0,
    "quietKingPlyCount": 0,
    "repetitionCount": 1
  },
  "legalMoves": [
    {"id":"21-17","from":21,"to":17,"path":[21,17],"captures":[],"promotes":false},
    {"id":"21-18","from":21,"to":18,"path":[21,18],"captures":[],"promotes":false},
    {"id":"22-18","from":22,"to":18,"path":[22,18],"captures":[],"promotes":false},
    {"id":"22-19","from":22,"to":19,"path":[22,19],"captures":[],"promotes":false},
    {"id":"23-19","from":23,"to":19,"path":[23,19],"captures":[],"promotes":false},
    {"id":"23-20","from":23,"to":20,"path":[23,20],"captures":[],"promotes":false},
    {"id":"24-20","from":24,"to":20,"path":[24,20],"captures":[],"promotes":false}
  ],
  "recentMoves": [],
  "thinkTimeMs": 5000
}
```

- `requestId` identifica un tentativo; un nuovo tentativo può avere un altro ID sulla stessa posizione.
- `player` e `position.turn` sono sempre uguali e valgono `white` oppure `black`.
- `ply` è il numero di mosse individuali già completate, non il numero di coppie di mosse.
- `legalMoves` è già filtrato secondo tutte le precedenze italiane. Non è vuoto quando il modello viene chiamato.
- `id` è una stringa opaca da ricopiare. Nell'attuale implementazione è il percorso con caselle 1–32 separate da trattini, anche per le prese. Non ricostruirla da `from` e `to`: due percorsi possono avere gli stessi estremi.
- `path` comprende partenza, ogni atterraggio intermedio e destinazione. `captures` contiene le caselle delle vittime nello stesso ordine.
- `recentMoves` contiene al massimo le ultime 16 mosse come oggetti `{"ply":1,"player":"white","moveId":"21-17"}`. È contesto facoltativo, non una cronologia sufficiente a ricostruire tutta la partita.
- `thinkTimeMs` è il limite per questa richiesta. Restituire la risposta con un margine per il trasporto. Non vengono inviate richieste dopo il termine della partita.

## 5. Risposta del modello

Rispondere `200 OK`, `Content-Type: application/json`, con un oggetto non superiore a 16 KiB:

```json
{
  "protocolVersion": "dama-model/v1",
  "moveId": "22-18",
  "comment": "Sviluppo una pedina verso il centro."
}
```

`protocolVersion` e `moveId` sono obbligatori. `moveId` deve coincidere esattamente con uno degli ID ricevuti. `comment` è facoltativo, massimo 240 caratteri, testo semplice; viene trattato come contenuto non attendibile ed eventualmente mostrato come testo, mai come HTML. Non inviare ragionamenti interni, token o segreti. Gli ulteriori campi vengono ignorati.

Non sono supportati streaming, chiamate di tool, WebSocket o formati specifici di un fornitore. Il servizio deve implementare questo contratto direttamente o attraverso un adattatore, mantenendo la decisione nella rete costruita per l'esperimento.

## 6. Errori, concorrenza e ripetibilità

- Timeout, errore HTTP, JSON invalido, versione errata, risposta troppo grande o mossa illegale: **nessuna mossa viene applicata**. La partita resta nella posizione precedente e mostra un errore recuperabile. Il giocatore può ritentare; non viene sostituito di nascosto il modello con un altro avversario.
- Il server annulla la richiesta al timeout. Il servizio dovrebbe interrompere il lavoro quando il client si disconnette.
- Lo stesso modello può ricevere richieste concorrenti da partite diverse. Non affidarsi a uno stato globale «partita corrente»; ogni richiesta è autosufficiente per scegliere una mossa legale.
- Il server serializza gli aggiornamenti di ogni partita e verifica il numero di semimosse atteso. Richieste duplicate non devono applicare due mosse alla stessa posizione.
- La risposta può essere deterministica o stocastica. Per confronti riproducibili il gestore dovrebbe fissare il seed nel servizio del modello. Il protocollo v1 non impone un seed.
- Nessun dato personale è richiesto: il modello riceve solo identificativi tecnici e stato del gioco.

## 7. Modelli inclusi

**Opale** è una rete ReLU con pesi costruiti da regole esplicite, senza training. Valuta le posizioni risultanti dalle mosse legali e sceglie quella con il punteggio maggiore. La simulazione di una mossa candidata e il filtro di legalità appartengono al motore del gioco; questa modalità non cerca le risposte successive dell'avversario.

Il progetto contiene anche un motore di ricerca per i benchmark indipendenti. Non è un modello selezionabile nel gioco e non interviene nelle decisioni di Opale. La rete non è dichiarata perfetta o di livello agonistico senza risultati.

Il formato del file dei pesi è interno al modello incluso; **non è un requisito per i modelli esterni**, che devono rispettare soltanto il contratto HTTP sopra.

## 8. Verifica di conformità prima dell'uso

1. Registrare il servizio con un ID nuovo e avviare l'applicazione.
2. Inviare l'esempio di apertura: la risposta deve scegliere uno dei sette ID.
3. Provare entrambi i colori: il significato dei segni di `board` resta invariato.
4. Provare una presa multipla: restituire l'ID dell'intero percorso.
5. Provare due richieste concorrenti per partite diverse.
6. Provocare un timeout e una mossa non ammessa: la posizione del gioco deve rimanere invariata.
7. Eseguire una partita modello–modello e una umano–modello fino al risultato, includendo la promozione.

Per cambiare il contratto in modo incompatibile servirà una nuova `protocolVersion`. Le aggiunte di campi facoltativi non richiedono una nuova versione.

## 9. Registrazione delle partite nella classifica

Ogni partita di verifica o benchmark deve comparire nel registro comune. Il protocollo di scelta della mossa resta `dama-model/v1`: è l'organizzatore della partita, non il modello, a registrare partecipanti e risultato.

- Se giochi tramite l'API del server, crea la stanza con `POST /api/games` specificando `source: "test"` oppure `source: "benchmark"`; è equivalente l'header `X-Match-Source`. Il web ordinario usa `web`. Le mosse e il risultato vengono registrati automaticamente dal server.
- Per gli umani usa `whiteNickname` e `blackNickname` alla creazione, oppure `nickname` quando il secondo giocatore chiama `/join`. I nickname identificano le voci della classifica.
- Nei runner C# offline referenzia `Dama.Competition` e usa `CompetitionStore.Start(...)` prima della prima mossa. Al termine chiama `TrackedMatch.Complete(winner, plies, reason)`, con vincitore `1` per il Bianco, `-1` per il Nero e `0` per la patta. Usa `using` per registrare le interruzioni; un limite di mosse o un errore non è una vittoria.
- Condividi con il server il percorso di `CompetitionStore.DefaultDirectory()`, oppure imposta `DAMA_COMPETITION_DIRECTORY` sul medesimo archivio. Non usare registri separati per partite reali di test.
- Le tre tipologie sono derivate dai partecipanti: `human-human`, `ai-ai`, `human-ai`. I filtri per origine e tipologia sono combinabili. Punteggi: 2 per vittoria, 1 per patta, 0 per sconfitta; nessun punto per partite in corso, interrotte o autopartite.

Le fixture artificiali di unit test restano isolate: non sono partite giocate e non devono generare risultati fittizi nella classifica. Vedi anche [AGENTS.md](AGENTS.md).

# Integrazione esterna successiva: Kingsrow

Il 7 novembre 2026 l'utente ha autorizzato separatamente l'integrazione di
Kingsrow Italian e un confronto di 18 partite contro ciascuno dei tre motori
attivi. L'adattatore usa il contratto HTTP di questo documento, ma Kingsrow
è un riferimento esterno con ricerca e conoscenza precalcolata: non è un
concorrente conforme ai vincoli della sfida manuale descritti sotto.
Questa aggiunta non modifica retroattivamente le regole dei tornei precedenti.
Vedere [integrazione](models/Kingsrow/README.md) e
[protocollo e risultati del nuovo girone](arena/kingsrow/2026-11-07/resoconto.md).

