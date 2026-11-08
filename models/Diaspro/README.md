# Diaspro

Diaspro è un motore separato da Klinamite. Usa una rete relazionale con coefficienti scritti manualmente, due propagazioni di caratteristiche sul grafo delle case e una memoria episodica delle posizioni della partita. Dalla v3 sono presenti geometria statica per esposizioni senza ripresa e continuazioni di cattura; la v4 porta il deterrente per il materiale indifeso sopra il suo valore materiale. La v5 aggiunge negamax alpha-beta con approfondimento iterativo, deadline interna ed estensione limitata delle prese. La v6 elimina valutazioni duplicate e unisce le analisi geometriche condivise; una tabella di trasposizione sperimentale è stata scartata perché riduceva i nodi visitati. Non esegue training e non incorpora codice decisionale, pesi, libri o database di Kingsrow.

La memoria è isolata per `gameId`, con al massimo 512 posizioni e 64 partite; i pesi non cambiano durante il gioco. Il servizio carica `Models/diaspro-v6.json`, costruito deterministicamente da `ManualWeights.cs`.

```powershell
dotnet run --project models/Diaspro -- --urls http://127.0.0.1:9017
```

Il servizio espone `/health` e `/choose-move` secondo `dama-model/v1`. Il costruttore deterministico in `ManualWeights.cs` definisce il checkpoint; il comando `--write-weights percorso` lo serializza senza giocare partite.
