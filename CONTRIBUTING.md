# Contribuire a Hey Hay Dama

Servono .NET SDK 10 e, per verificare JavaScript, Node.js. I percorsi dei progetti restano quelli storici della soluzione `WeightSpaceGames.slnx`.

## Verifica senza incontri fra modelli

```sh
dotnet build WeightSpaceGames.slnx -c Release --artifacts-path artifacts/public-check -p:UseAppHost=false
dotnet test tests/Dama.Tests -c Release --artifacts-path artifacts/public-check -p:UseAppHost=false --filter "FullyQualifiedName~RulesTests|FullyQualifiedName~ModelTests"
node --check src/Dama.Web/wwwroot/app.js
node --check src/Dama.Web/wwwroot/leaderboard.js
```

I test API e browser possono creare partite reali: leggere [AGENTS.md](AGENTS.md) prima di eseguirli. Ogni partita deve essere registrata alla creazione e alla conclusione. Non avviare confronti fra modelli durante una normale verifica di documentazione o interfaccia.

## Organizzazione dei materiali

- `src/`, `models/`, `tests/`, `experiments/`: implementazioni, pesi, verifiche e runner.
- `arena/`: discussioni originali, piani, protocolli, manifest e prove delle sessioni. Conservare le chat in append, senza riscrivere messaggi storici.
- `models/*/results/`: risultati sperimentali pubblici; non confonderli con i dati delle stanze web.
- `docs/`: guide alle fonti e copie documentate dei riepiloghi delle prime prove.
- `artifacts/`, `bin/`, `obj/`, `App_Data/`: file locali esclusi da Git. Non aggiungere stanze, nickname o chiavi di accesso a un commit.
- Materiale per la redazione dell'articolo: fuori dal repository; `editoriale/` è ignorata come protezione aggiuntiva.

Prima di un commit controllare `git status --short` e il diff. Non usare `git add -f` sui dati locali. Non aggiornare pesi o risultati storici per rendere coerente una narrazione: documentare le correzioni separatamente.

Il repository non contiene ancora una licenza di distribuzione scelta dal titolare. Rendere il codice visibile non equivale a concedere una licenza open source; la scelta non viene effettuata automaticamente durante la preparazione alla pubblicazione.
