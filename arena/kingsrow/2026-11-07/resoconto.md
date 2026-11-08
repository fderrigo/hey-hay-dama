# Hey Hay Dama — confronto con Kingsrow Italian

Il 7 novembre 2026, su richiesta esplicita dell'utente, Kingsrow Italian è
stato integrato come quarto avversario esterno e ha giocato 18 partite contro
ciascuno dei tre motori attivi: **Opale C manual v3, Agata v9 e Klinamite v3**.
La voce storica Kimberlite era disabilitata nel server e non era iscritta a questa sessione.

## Risultati verificati

| Avversario | Vittorie Kingsrow | Patte | Vittorie avversario | Interrotte | Punti Kingsrow | Punti avversario |
|---|---:|---:|---:|---:|---:|---:|
| Klinamite v3 | 18 | 0 | 0 | 0 | 36 | 0 |
| Agata v9 | 18 | 0 | 0 | 0 | 36 | 0 |
| Opale C v3 | 18 | 0 | 0 | 0 | 36 | 0 |
| **Totale** | **54** | **0** | **0** | **0** | **108** | **0** |

Tutte le partite hanno un esito dell'arbitro, senza sconfitte attribuite per
timeout o errore. Il tasso di vittoria di Kingsrow in questo campione è 100%.
Questa tabella è relativa soltanto al nuovo girone; non sostituisce la
classifica storica cumulativa del sito.

## Protocollo

- Esecuzione sequenziale: Klinamite, Agata, Opale; nove partite per colore contro ciascuno.
- Nove aperture distinte di sei semimosse generate legalmente da `System.Random`,
  seed `20261107`–`20261115`. Ogni apertura è ripetuta a colori invertiti ed è
  comune ai tre confronti. Le sei semimosse imposte non sono scelte dai motori.
- Kingsrow Italian x64 dichiara versione `1.19e`, protocollo CheckerBoard 2,
  gametype 22. È il pacchetto disponibile sul sito ufficiale al momento della prova.
- Kingsrow: 200 ms richiesti alla ricerca, modalità tempo esatto; budget del
  messaggio HTTP 300 ms, timeout di trasporto del runner 10 secondi. Gli altri
  motori ricevono un budget di 1000 ms. Non è un confronto a costo computazionale uguale.
- Hash table richiesta: 64 MiB. Libro di aperture incluso nel pacchetto, modalità
  predefinita. Un controllo separato su posizione sintetica ha confermato `get book = 1`
  e una risposta con valori/profondità del libro. Nessun database aggiuntivo dei
  finali installato; la directory nativa predefinita `C:\kr_ital_wld` non esiste.
- Il contesto nativo viene riavviato a ogni richiesta; Kingsrow non riceve la
  cronologia completa. L'arbitro gestisce ripetizioni, blocco e patte.
- Limite tecnico: 400 semimosse, che avrebbe prodotto interruzione senza punti.
  Nessuna partita ha raggiunto il limite.
- Agata: temperatura 10, seed 731941; la scelta dipende anche dall'ID della partita.
  Registrare i seed non promette quindi repliche bit per bit dell'intero girone.
- Pesi e codice decisionale dei tre avversari invariati; nessun training,
  aggiustamento automatico o apprendimento dai risultati.

## Integrazione e verifica

Il servizio C# espone `/choose-move` su porta 9015 usando `dama-model/v1`.
Converte le 32 caselle nel formato nativo CheckerBoard, chiama Kingsrow e
confronta la scacchiera restituita con le transizioni legali dell'arbitro.
Una risposta incompatibile è un errore; non c'è una mossa sostitutiva.
Le chiamate native sono serializzate per evitare interferenze fra richieste.

Ogni partita è stata registrata tramite `TrackedMatch.Start` prima della prima
mossa e conclusa tramite `Complete`, con origine `benchmark` e tipo `ai-ai`.
Gli ID degli avversari sono quelli stabili già utilizzati; Kingsrow usa `kingsrow`.

L'audit ha ripercorso **2.959 mosse**, verificando tutte le 54 traiettorie,
gli stati, gli esiti, i record condivisi, il bilanciamento dei colori e le
nove aperture appaiate. Le fixture preliminari non hanno creato partite.

## Interpretazione

Il risultato mostra un divario netto nel campione osservato. Non dimostra
un miglioramento dei tre modelli, che sono rimasti invariati, e non permette
di ordinarli fra loro: hanno tutti ottenuto zero punti contro Kingsrow.
Il confronto precedente, vinto da Agata, conserva il suo significato nel
gruppo originario e non viene riscritto da questa prova.

Kingsrow è un riferimento esterno costruito con ricerca e conoscenza
precalcolata, mentre la sfida originale vietava ricerca e apprendimento
automatico nelle versioni finali. È quindi una misura contro un avversario
esperto, non una gara a parità di vincoli. Non è una misura Elo né una prova
della massima forza di Kingsrow, eseguito con tempo breve e senza database
aggiuntivi dei finali. Non sono state trasferite le sue conoscenze nei modelli.

## Dati e riproduzione

- [Manifest](manifest.json): partecipanti, versioni e impostazioni.
- [Hash](hashes.json): pacchetto, risorse native, pesi avversari e sorgenti del runner al momento del girone.
- [Partite complete](games.jsonl): ID, stato iniziale e scacchiera dopo ogni mossa.
- [Riepilogo numerico](summary.json) e [audit](audit.json).
- [Verifiche dell'integrazione](verification.json) e [sorgenti congelati della prova](implementation).
- [Adattatore e istruzioni](../../../models/Kingsrow/README.md).
- [Runner](../../../experiments/Dama.KingsrowMatch/Program.cs) e [audit ripetibile](../../../experiments/Dama.KingsrowMatch/Audit.cs).

Dalla radice della repository, con il registro originale disponibile:

```powershell
dotnet run --project experiments/Dama.KingsrowMatch -- --audit arena/kingsrow/2026-11-07
```

Il registro delle stanze resta privato. Il JSONL pubblico contiene le partite
di questo esperimento senza token o nickname umani. Un clone non include i
record locali usati per la riconciliazione dell'audit.

Dopo il girone sono stati aggiunti il comando di audit e controlli più stretti
sugli input HTTP malformati. La ricerca nativa non è cambiata. Le copie in
`implementation` riproducono i tre sorgenti effettivamente usati nel girone,
verificati contro gli hash acquisiti prima di queste aggiunte.

Kingsrow e i suoi dati sono scaricabili gratuitamente dal
[sito ufficiale](https://www.edgilbert.org/ItalianCheckers/KingsRowItalian.htm).
Nessun abbonamento o API a pagamento è stato utilizzato. I binari restano
locali, esclusi da Git; la loro redistribuzione e l'offerta come servizio
pubblico non sono state autorizzate dalle pagine consultate. Nessun deploy eseguito.
