# Kimberlite v1

Kimberlite è una rete neurale ReLU specializzata per la dama italiana, pubblicata come servizio HTTP `dama-model/v1`. I suoi 10.210 parametri sono scritti direttamente e salvati in `Models/kimberlite-v1.json`. Non usa training, fine-tuning, ottimizzazione automatica, ricerca delle continuazioni, servizi AI esterni o tabelle precalcolate.

## Avvio e integrazione

Dipendenza unica: .NET SDK 10.0.100. Il progetto riusa `Dama.Engine` per applicare le mosse già dichiarate legali dal server, non per valutare la posizione.

Dalla radice del repository:

```bash
dotnet run --project src/Dama.Neural/Dama.Neural.csproj -- --urls http://127.0.0.1:9001
```

Avviare poi il gioco in un secondo terminale:

```bash
dotnet run --project src/Dama.Web/Dama.Web.csproj
```

Il gioco contiene già questa voce in `Arena:Models`:

```json
{
  "Id": "kimberlite",
  "Name": "Kimberlite",
  "Description": "Rete neurale ReLU per la dama, con pesi scritti direttamente e senza training.",
  "Kind": "http",
  "Endpoint": "http://127.0.0.1:9001/choose-move",
  "MaxThinkTimeMs": 1500,
  "Enabled": true
}
```

Non servono credenziali. In Docker, sostituire `127.0.0.1` con il nome DNS del servizio, per esempio `http://kimberlite:9001/choose-move`.

## Rete e pesi

L'architettura sparse feed-forward è `128 → 1626 ReLU → 1`. I 128 ingressi codificano quattro piani da 32 caselle: pedine proprie, dame proprie, pedine avversarie e dame avversarie. Per il Nero la tavola viene ruotata, mantenendo invariato il significato dei segni nel protocollo.

Ogni neurone ha bias, connessioni e peso di uscita espliciti. I pesi rappresentano conoscenza scacchistica scritta manualmente:

- materiale: pedina 100, dama 300;
- avanzamento e promozione, centralità, difesa dell'ultima traversa e bordi;
- mobilità e formazioni di supporto;
- motivi ReLU per salti disponibili e pezzi esposti a cattura.

I pesi non derivano da partite, dataset o inizializzazione casuale. `KimberliteWeightBuilder` contiene la loro definizione sorgente; il file JSON è l'artefatto effettivamente caricato dal servizio. Per rigenerarlo identico:

```bash
dotnet run --project experiments/Dama.Neural.Benchmark -- --export src/Dama.Neural/Models/kimberlite-v1.json
```

L'inferenza applica ogni mossa ricevuta in `legalMoves`, valuta la posizione risultante e restituisce l'ID con punteggio maggiore. Le vittorie immediate e le patte già visibili sono casi terminali. Non vengono generate risposte dell'avversario: questa è una rete a una semimossa, non un sistema con ricerca.

La rete è immutabile dopo il caricamento e condivisa in sola lettura; ogni richiesta ha input e scelta locali. Non c'è stato di partita globale.

## Test

```bash
dotnet test WeightSpaceGames.slnx -c Release
```

I test verificano protocollo, apertura, entrambi i colori, prese multiple, concorrenza, JSON non valido, cancellazione, corrispondenza fra file e pesi sorgente, round-trip JSON, simmetria, non linearità e vittoria immediata. I test del gioco coprono anche timeout, mosse illegali e immutabilità della posizione dopo errori HTTP.

## Benchmark riproducibile

```bash
dotnet run -c Release --project experiments/Dama.Neural.Benchmark -- --games 12 --output experiments/kimberlite-benchmark.json
```

Metodo: colori appaiati, aperture casuali di sei semimosse con seed 20261108, limite di 240 semimosse. Avversari misurati separatamente: casuale, materialista, Ambra senza ricerca e Ambra con ricerca a profondità 3. Il report contiene risultati effettivi, parametri e durata.

Il benchmark sintetico non è una stima Elo né prova forza agonistica. La valutazione a una semimossa può non riconoscere tattiche lontane; nessun risultato viene esteso a esseri umani senza misure specifiche.
