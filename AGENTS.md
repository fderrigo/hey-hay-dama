# Partite e classifica

Ogni partita reale avviata dal web, da test, da benchmark o da un agente deve essere registrata nella classifica condivisa, già alla creazione e poi alla conclusione. Non giocare partite usando un ciclo autonomo privo di registrazione.

- Attraverso il server ASP.NET Core: usare `/api/games` con `source: "test"` o `source: "benchmark"`, oppure l'header `X-Match-Source`. Il browser ordinario usa `web`. Gli agenti che provano l'interfaccia devono impostare `X-Match-Source: test` nel contesto browser.
- Nei runner C# offline: referenziare `Dama.Competition`, aprire `CompetitionStore.DefaultDirectory()`, creare un `TrackedMatch` con `Start(...)` prima della prima mossa e chiamare `Complete(...)` una sola volta con il risultato dell'arbitro. Usare `using` per registrare le interruzioni. Non riutilizzare l'ID di una partita per una nuova esecuzione.
- Tutti i processi devono condividere la stessa directory. In questa repo il percorso predefinito è `src/Dama.Web/App_Data/competition`; fuori dalla repo impostare `DAMA_COMPETITION_DIRECTORY` sul percorso condiviso con il server. `Arena:CompetitionDirectory` può configurare il percorso del web e deve coincidere con quello dei runner.
- Gli ID dei modelli sono stabili (`opale`, `kimberlite`, `agata`, `klinamite`, `kingsrow`, `diaspro`). Gli umani sono identificati dal nickname normalizzato; non aggregarli sotto una sola voce.
- Origini ammesse: `web`, `test`, `benchmark`. Tipologie derivate dai partecipanti: `human-human`, `ai-ai`, `human-ai`. I due filtri sono indipendenti e combinabili.
- Punteggio: vittoria 2, patta 1, sconfitta 0. Partite in corso, interrotte e autopartite sono conteggiate ma non assegnano punti. Un annullamento aggiorna la stessa partita e rimuove l'eventuale risultato precedente.
- Fixture aritmetiche di unit test e posizioni simulate nel browser non sono partite giocate: usare un archivio isolato per questi dati artificiali, senza introdurre risultati fittizi nella classifica. Le partite realmente create nei test dell'API, invece, devono rimanere nel registro con origine `test`.

L'utente ha chiesto di non eseguire confronti tra modelli. Verificare le modifiche senza avviare benchmark o incontri tra modelli, salvo sue successive istruzioni.
