# Hey Hay Dama

Dama italiana giocabile via web: umano–modello, due umani sullo stesso dispositivo o tramite invito, e modello–modello con spettatori. Server **ASP.NET Core 10**, client **JavaScript vanilla, HTML e CSS**, senza framework frontend o dipendenze npm. Anche il modello incluso è C# e non richiede GPU, Python o servizi esterni.

Il repository contiene il gioco e la documentazione dell'esperimento fra motori indipendenti: sorgenti, pesi, discussioni, protocolli, risultati e audit. Il nome storico del repository e della soluzione è `weight-space-games` / `WeightSpaceGames.slnx`.

## Esplorare il progetto

- [Guida all'esperimento e alle fonti](docs/ESPERIMENTO.md): cronologia, discussioni originali, dati e limiti dei risultati.
- [Protocollo dei modelli](specifiche_modello.md) e [istruzioni iniziali](prompt_modello.md).
- Motori: [Opale C](models/Opale.C/README.md), [Agata](models/Agata/README.md), [Kimberlite](src/Dama.Neural/README.md) e [Klinamite](models/Klinamite).
- [Contribuire e verificare](CONTRIBUTING.md).
- [Kingsrow Italian](models/Kingsrow/README.md): avversario esperto esterno gratuito per Windows; [girone di 54 partite e audit](arena/kingsrow/2026-11-07/resoconto.md).
- [Ametista](models/Ametista/README.md): nuovo motore a pesi manuali e ricerca originale, autorizzata nella sfida successiva contro Kingsrow; Opale C resta preservato. [Sviluppo, conferme e limiti](arena/ametista/2026-11-07/RESULTS.md).
- [Diaspro](models/Diaspro/README.md): evoluzione distinta da Klinamite; [prove e checkpoint delle revisioni successive](arena/kingsrow-challenge/diaspro/).
- [Avventurina](models/Avventurina/README.md): ricerca e tabelle dei finali originali; [diario delle misure](models/Avventurina/DIARIO.md).

Le bozze redazionali per l'articolo sono conservate fuori dal repository. Discussioni e risultati tecnici sono invece parte del materiale pubblico. I dati personali delle stanze, le credenziali e gli output di compilazione restano esclusi.

## Avvio

Prerequisito: .NET SDK 10. Dal percorso della repository:

```sh
dotnet run --project src/Dama.Web
```

Aprire **http://localhost:5317**. Le risorse grafiche sono locali e disegnate in CSS/SVG; non vengono caricati font, script o immagini da CDN.

Per rendere il server raggiungibile sulla rete locale:

```sh
dotnet run --project src/Dama.Web --no-launch-profile --urls http://0.0.0.0:5317
```

Aprire dal secondo dispositivo `http://<indirizzo-LAN-del-server>:5317`, consentendo la porta nel firewall se necessario. I link di invito usano l'indirizzo con cui si apre il tavolo: un link `localhost` funziona solo sullo stesso computer.

## Giocare

- **Contro un modello:** scegliere l'avversario abilitato e il proprio colore. Il Bianco inizia. È disponibile l'annullamento fino all'ultima decisione umana.
- **Con un amico:** scegliere lo stesso dispositivo oppure una stanza online. Il creatore occupa un posto; il primo ospite che preme «Prendi posto» occupa l'altro. Gli altri visitatori sono spettatori.
- **Tra modelli:** scegliere i due avversari. Il browser del creatore avanza automaticamente i turni e può mettere la sfida in pausa. Gli spettatori seguono gli aggiornamenti. Se il creatore chiude la pagina, la partita resta salvata e riprende quando ritorna e preme «Riprendi».
- Fare clic/toccare una pedina, poi la destinazione evidenziata. Nelle prese multiple scegliere un atterraggio alla volta. Le frecce muovono il focus sulla damiera; Invio/Spazio selezionano e Esc cancella la selezione.
- «Invita / condividi» copia il link pubblico della stanza. Quel link non contiene la chiave privata del posto. Questo browser conserva la chiave in localStorage; cancellare i dati del browser comporta la perdita del posto, pur lasciando la partita visibile.

Sono implementate le specificità italiane: pedine solo in avanti, pedina incapace di catturare una dama, dame a movimento corto, presa obbligatoria, precedenze complete, ostacolo dei pezzi catturati fino alla fine della sequenza, promozione a fine turno. Si applicano automaticamente la quarta ripetizione e il conteggio delle 40 mosse di sole dame per parte senza prese, con almeno una dama per parte. L'automatismo sostituisce la richiesta all'arbitro delle regole da torneo. Riferimenti: [FID](https://www.fid.it/regolamenti/capo1.htm), [regolamento tecnico 2008](https://www.fid.it/regolamenti/2008/RegTec_CAPO_I.pdf).

## Classifica

Il pulsante **Classifica** apre il registro condiviso. La vista generale include tutte le partite; le viste **Umano vs umano**, **AI vs AI** e **Umano vs AI** si combinano con il filtro di origine **Web**, **Test** o **Benchmark**.

Ogni partita viene conteggiata dalla creazione. I punti si assegnano alla conclusione: **2 vittoria, 1 patta, 0 sconfitta**. L'ordine è per punti, poi vittorie e nome. Partite in corso, interrotte e autopartite sono registrate senza assegnare punti. Il registro mostra le ultime 50 partite dei filtri selezionati, mentre i totali e la classifica includono tutte le partite. L'annullamento aggiorna la stessa registrazione e rimuove l'eventuale risultato precedente.

Ogni umano sceglie un nickname, anche quando prende posto tramite invito. Usa sempre lo stesso nome: spazi ripetuti e maiuscole vengono normalizzati. I due umani al medesimo tavolo devono avere nomi diversi. I nickname non sono account autenticati; due persone che usano lo stesso nome condividono la stessa voce.

Il registro persistente è `src/Dama.Web/App_Data/competition`. Il server e tutti i runner inclusi usano la libreria `Dama.Competition`, con un file atomico per partita e un blocco condiviso fra processi. Il server ricostruisce gli eventuali aggiornamenti mancanti dai salvataggi delle stanze dopo un riavvio. I dati in `App_Data` sono esclusi dalla compilazione e dalla pubblicazione.

Per test o benchmark esterni usare `/api/games` con il campo `source` o l'header `X-Match-Source`. Nei runner offline usare `CompetitionStore.Start(...)` e `TrackedMatch.Complete(...)`, come descritto in [AGENTS.md](AGENTS.md). Tutti i processi devono condividere la stessa directory; `DAMA_COMPETITION_DIRECTORY` permette di impostarla fuori dalla repo. Il server supporta anche `Arena:CompetitionDirectory`. I client non possono inviare punteggi alla classifica attraverso un'API pubblica.

L'API di lettura è `GET /api/leaderboard?source=all&type=all`. `source` ammette `all`, `web`, `test`, `benchmark`; `type` ammette `all`, `human-human`, `ai-ai`, `human-ai`. L'origine è una dichiarazione del chiamante, non un sistema di rilevamento automatico degli agenti.

## Collegare modelli

**Leggere [specifiche_modello.md](specifiche_modello.md)**: contiene il contratto HTTP completo `dama-model/v1`, la numerazione, esempi di richiesta/risposta e la configurazione. Il [prompt neutro](prompt_modello.md) può essere fornito ad altre AI insieme alle specifiche.

Il gestore modifica `Arena:Models` in [appsettings.json](src/Dama.Web/appsettings.json) e riavvia il server. Il giocatore può scegliere soltanto gli ID abilitati; non può aggiungere URL o credenziali. Per i modelli esterni sono ammessi servizi HTTP implementati in qualsiasi linguaggio. L'applicazione invia posizione e mosse legali, il servizio restituisce un `moveId`.

Le credenziali dei modelli si leggono da variabili d'ambiente e non raggiungono il browser. Timeout, risposte errate e mosse illegali lasciano la posizione invariata e offrono un tentativo esplicito; non viene usato un modello sostitutivo.

## Il modello incluso

**Opale v1** è una rete feed-forward `128 → 1080 ReLU → 1`, con **6.412 parametri memorizzati in formato sparso**, costruiti direttamente dal codice in [WeightBuilder](src/Dama.Engine/NeuralModel.cs). Nessun training, fine-tuning o ottimizzatore.

L'input codifica 32 caselle su quattro piani di occupazione, normalizzati rispetto al giocatore. I neuroni rilevano materiale, avanzamento, centro, protezioni, mobilità e configurazioni di presa. I rilevatori di congiunzioni usano soglie ReLU; il file dei pesi è [opale-v1.json](src/Dama.Web/Models/opale-v1.json).

- **Opale** valuta le posizioni dopo ciascuna mossa legale e sceglie il punteggio massimo, con controllo delle condizioni terminali. Non cerca le risposte dell'avversario. La generazione delle mosse e la simulazione della mossa candidata rimangono nel motore delle regole.
- Il motore di ricerca in `SearchModel.cs` è riservato ai **benchmark**: non è registrabile come strategia interna nel gioco e non interviene nelle mosse di Opale. Usa negamax, alpha-beta, approfondimento iterativo ed estensione delle prese. Il taglio dei cicli nella ricerca è un'euristica; la regola reale di patta resta la quarta ripetizione.

Rigenerare esattamente i pesi:

```sh
dotnet run --project experiments/Dama.Arena -- --export src/Dama.Web/Models/opale-v1.json
```

## Verifica

```sh
dotnet build WeightSpaceGames.slnx -c Release --artifacts-path artifacts/tests -p:UseAppHost=false
dotnet test tests/Dama.Tests -c Release --artifacts-path artifacts/tests -p:UseAppHost=false --filter "FullyQualifiedName~RulesTests|FullyQualifiedName~ModelTests"
node --check src/Dama.Web/wwwroot/app.js
```

La verifica mirata della classifica è `dotnet test tests/Dama.Tests --filter "FullyQualifiedName~CompetitionTests|FullyQualifiedName~CompetitionApiTests"`. Con il server avviato, `python tests/browser_leaderboard.py --url http://localhost:5317` verifica l'interfaccia giocando una sola partita fra due umani simulati, marcata `test` e conservata nella classifica. Queste verifiche con partite sono distinte dai controlli delle sole regole indicati sopra.

I test verificano regole italiane, priorità lessicografica delle vittime, promozioni, patte, invarianti delle partite, rete, protocollo HTTP, errori dei modelli, autorizzazione dei posti, concorrenza e persistenza.

Per verificare soltanto il gioco fra persone, con il server avviato e Playwright/Chrome disponibili:

```sh
python tests/browser_humans.py --url http://localhost:5317
```

La verifica copre stesso dispositivo (desktop e mobile), identità e colori, ripristino dopo ricaricamento, nuova partita locale, invito e spettatori. Controlla anche l'accesso agli inviti quando l'elenco dei modelli non è disponibile. Crea otto partite fra umani con origine `test`, le conclude tramite l'API e conserva i record nella classifica; non avvia incontri fra modelli.

Per la prova completa dell'interfaccia, con il server già in esecuzione e Google Chrome installato:

```sh
python -m pip install -r tests/requirements.txt
python tests/browser_smoke.py --url http://localhost:5317
```

Il test usa finestre headless e contesti browser distinti per simulare creatore, secondo giocatore e spettatore. Verifica anche touch a 390px, rotazione, annullamento, finestre di dialogo e sfide tra modelli. Screenshot e report sono salvati in `artifacts/`, esclusa da Git.

## Pubblicazione

```sh
dotnet publish src/Dama.Web -c Release -o artifacts/publish
```

Oppure, con Docker:

```sh
docker compose up --build -d
```

Aprire `http://localhost:5317`. Il contenitore usa un utente non root, ascolta sulla porta interna 8080 e mantiene le partite nel volume `dama-games`. Per esporlo su Internet collocarlo su un host Docker/.NET con dominio e HTTPS sul reverse proxy. Il progetto non dipende da Sites o Cloudflare Workers, che non eseguono questo server ASP.NET Core.

Per una configurazione specifica dell'host, montare un `appsettings.Production.json` in `/app/appsettings.Production.json:ro` e passare le variabili d'ambiente necessarie. I modelli HTTP su un altro contenitore vanno indicati tramite il nome DNS del servizio, non `localhost`.

### Dati e limiti operativi

Le stanze sono salvate come JSON in `src/Dama.Web/App_Data/games` durante lo sviluppo, o `/app/App_Data/games` nel contenitore. Il percorso è configurabile con `Arena:DataDirectory`. I file contengono hash delle chiavi di accesso e non sono serviti dal web server. Gli aggiornamenti di una stanza sono serializzati e i salvataggi sostituiscono atomicamente il file.

L'archiviazione attuale è per **una singola istanza server**, adatta a questo progetto; non eseguire più istanze sullo stesso archivio. Non ci sono account, recupero delle chiavi, matchmaking pubblico o tornei pianificati. Gli incontri tra modelli sono organizzabili dalla schermata di gioco e i confronti riproducibili dal programma `Dama.Arena`.

## Struttura

```text
src/Dama.Engine/          regole, rete ReLU, costruttore dei pesi, ricerca
src/Dama.Competition/     registro comune e classifica
src/Dama.Web/             API ASP.NET Core, stanze, adattatore HTTP, configurazione
src/Dama.Web/Models/      artefatto dei pesi
src/Dama.Web/wwwroot/     interfaccia HTML/CSS/JavaScript vanilla
tests/Dama.Tests/        test delle regole, del modello e dell'API
tests/browser_smoke.py   verifica dell'interfaccia nel browser
experiments/Dama.Arena/  esportazione pesi e benchmark
specifiche_modello.md    contratto per collegare nuovi modelli
prompt_modello.md        prompt neutro per implementazioni indipendenti
```
