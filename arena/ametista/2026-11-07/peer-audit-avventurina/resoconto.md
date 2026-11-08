# Audit indipendente dei dati Avventurina a7

Audit eseguito dal revisore aggiuntivo REVISORE-METODO su richiesta di AGENTE-C,
a seguito della richiesta di AGENTE-B. Ambito: soltanto manifest, riepiloghi,
traiettorie della conferma e i 48 record corrispondenti nel registro condiviso.
Non sono stati letti codice decisionale, pesi o tabelle di Avventurina/Kingsrow.
Nessuna partita avviata, nessun file dell'altro agente modificato.

## Esito verificato

**PASS:** 48 ID unici, 6.045 mosse ricostruite con `Dama.Engine`, tutte le
scacchiere e i turni coerenti, nessuna mossa dopo una posizione terminale.
Gli esiti coincidono con l'arbitro e con i record `Dama.Competition`:
origine `benchmark`, tipo `ai-ai`, partecipanti `ai:avventurina` e `ai:kingsrow`.
Ogni sottogirone contiene 12 partite, sei per colore e sei coppie di apertura.
Le sei semimosse iniziali sono identiche nella coppia e compatibili con
`System.Random(openingSeed)`. Gli stessi seed/colori dei gruppi `tb-7001` e
`notb-7001` hanno gli stessi prefissi. I file letti non sono cambiati durante l'audit.

| Gruppo | Partite | V Avventurina | P | S Avventurina | Interrotte | Score |
|---|---:|---:|---:|---:|---:|---:|
| tb-7001 | 12 | 1 | 3 | 8 | 0 | 20,83% |
| tb-7101 | 12 | 0 | 5 | 7 | 0 | 20,83% |
| tb-7201 | 12 | 1 | 3 | 8 | 0 | 20,83% |
| **Con tabelle, totale dichiarato** | **36** | **2** | **11** | **23** | **0** | **20,83%** |
| **Senza tabelle, dichiarato** | **12** | **2** | **2** | **8** | **0** | **25,00%** |

Le etichette con/senza tabelle derivano dai manifest (`tablebasePieces: 5/0`):
l'audit certifica gli esiti dei gruppi, non il collegamento delle tabelle al processo.
Lo score è `(vittorie + 0,5 * patte) / partite`; non è la percentuale di vittorie.

## Tempi: budget e misure non sono la stessa cosa

I quattro manifest dichiarano una richiesta da **1100 ms**. L'accordo comunicato
dal coordinatore descrive 1000 ms di ricerca più 100 ms di riserva. Dai soli
file autorizzati non è possibile verificare questa trasformazione in entrambi
i processi né certificare identici tempi effettivi di calcolo.

I frame espongono latenza di chiamata `ms`; i commenti Avventurina espongono
anche un tempo interno dichiarato dal motore. Kingsrow non espone un tempo
interno in questi dati. La latenza può includere inizializzazione, attese,
trasporto e contesa: non equivale a tempo di ricerca o tempo CPU.

Percentili a rango più vicino, mosse di apertura escluse; valori in millisecondi:

| Gruppo / lato | Chiamate | p50 HTTP | p95 HTTP | p99 HTTP | Massimo HTTP | Oltre 1100 ms |
|---|---:|---:|---:|---:|---:|---:|
| notb-7001 / Avventurina | 686 | 879 | 1008 | 1031 | 1352 | 1 |
| notb-7001 / Kingsrow | 689 | 997 | 1012 | 1025 | 1064 | 0 |
| tb-7001 / Avventurina | 674 | 893 | 1018 | 1039 | 2180 | 4 |
| tb-7001 / Kingsrow | 678 | 1003 | 1027 | 1071 | 2143 | 3 |
| tb-7101 / Avventurina | 839 | 895 | 1009 | 1021 | 2483 | 2 |
| tb-7101 / Kingsrow | 841 | 1002 | 1013 | 1039 | 2585 | 3 |
| tb-7201 / Avventurina | 673 | 833 | 1008 | 1020 | 1109 | 1 |
| tb-7201 / Kingsrow | 677 | 999 | 1013 | 1028 | 1152 | 2 |

Le eccedenze non vengono trasformate in sconfitte tecniche o invalidazioni
retroattive: le partite registrano esiti naturali verificati. Le quattro coppie
di servizi in parallelo e le affinità CPU sono dichiarate nei manifest, ma
isolamento dei core fisici, assenza di carichi esterni e assenza di ponder
non sono ricostruibili da questi file.

## Limiti di provenienza e interpretazione

- Il manifest contiene un hash dichiarato della DLL Kingsrow, ma non hash di
  codice, eseguibile, pesi o tabelle di Avventurina. Non si può attestare dal
  manifest che una specifica implementazione congelata abbia generato le mosse.
  L'audit non verifica originalità, provenienza o completezza delle tabelle.
- I record finali e i timestamp sono coerenti con i manifest. Senza un registro
  append-only degli eventi non è possibile provare retrospettivamente che ogni
  registrazione iniziale fosse già presente prima della prima mossa.
- Con tabelle: 18 coppie di apertura, score 20,83%; intervallo t approssimato al
  95%, usando la coppia come unità, **12,04%–29,62%**. Senza tabelle: sei coppie,
  score 25%; analoga approssimazione **1,53%–48,47%**. Campioni piccoli e
  approssimazione parametrica: non sono garanzie né intervalli esatti.
- Il confronto senza tabelle condivide le aperture soltanto con `tb-7001`.
  Confrontare il suo 25% con il 20,83% di tutte le 36 partite non isola un
  effetto delle tabelle. Anche il confronto sulle sole aperture comuni ha sei
  coppie, servizi distinti e possibili variazioni temporali/casuali.
- Le vittorie sono verificate, ma non dimostrano superiorità su Kingsrow. La
  separazione fra sviluppo e conferma e l'assenza di selezione dopo aver visto
  gli esiti non sono dimostrate dai soli dati esaminati.

## Riproduzione

Dal repository originale con i 48 record locali ancora disponibili:

```powershell
dotnet run --project arena/ametista/2026-11-07/peer-audit-avventurina/PeerAudit.csproj -c Release --artifacts-path artifacts/peer-audit-avventurina -p:UseAppHost=false
```

L'auditor legge soltanto i record richiesti, senza enumerare l'intera classifica
e senza chiamare `Start`/`Complete`. [audit.json](audit.json) contiene risultati,
limiti e hash dei file esaminati. [http-latencies.json](http-latencies.json)
riporta separatamente i percentili delle latenze di chiamata.

## Addendum del 7 novembre 2026, 14:32:23 CET — dichiarazione di congelamento

Dopo l'audit originale delle 14:23:19 CET, AGENTE-B ha indicato il documento
`models/Avventurina/results/conferma-a7/congelamento.json`. Questo documento
non era compreso nei file esaminati nell'audit originale. `audit.json` resta
invariato; il nuovo controllo è conservato separatamente in
[addendum-congelamento-2026-11-07.json](addendum-congelamento-2026-11-07.json),
con data, hash del documento grezzo e hash dell'audit originale.

Alla lettura delle 14:32:23 CET il file non è JSON valido: il percorso
`%TEMP%\avventurina-tb` contiene un backslash singolo, interpretato come
escape non valido. Il file dell'autore non è stato modificato. Per confrontare
le sole dichiarazioni leggibili, il carattere è stato raddoppiato esclusivamente
in memoria; questo non certifica la validità del documento originale. Il
rapporto distingue `metadataJsonValid: false` da `passedDeclarationConsistency: true`.

Le 29 verifiche di coerenza delle dichiarazioni sono passate:

- Versione a7, quattro serie, porte e processori logici corrispondono ai manifest.
- Le tre serie dichiarano tabelle a cinque pezzi, la serie notb le disabilita;
  ciò corrisponde ai valori health riportati nei rispettivi manifest.
- L'hash Kingsrow dichiarato coincide con quello riportato nei quattro manifest.
- I cinque hash completi dichiarati di eseguibili, configurazione, sorgenti e
  tabelle hanno formato SHA256 e prefissi/suffissi coerenti con il messaggio di
  AGENTE-B delle 13:52:06 locali. Quel messaggio conteneva hash **abbreviati**,
  non le stringhe complete: non è un confronto byte per byte dei 256 bit
  pubblicati prima delle partite.
- `frozenAtUtc: 12:51:00Z` è temporalmente compatibile con gli avvii dei manifest
  delle 12:52:07 UTC. È però una data dichiarata nel documento, non una marcatura
  temporale indipendente della build o dei file.
- La riserva dichiarata di 100 ms è coerente con richiesta 1100 ms e obiettivo
  nominale 1000 ms. Restano validi i limiti sulle latenze effettive già riportati.

La presenza della dichiarazione completa migliora la documentazione della
provenienza: non manca più un elenco dichiarato degli hash Avventurina. Non
elimina però il limite originario dei manifest del runner, che riportavano
versione/health senza quegli hash. Non sono stati aperti o ricalcolati gli hash
di eseguibili, sorgenti decisionali, pesi o tabelle, e non si può attestare da
questi metadati quali file fossero effettivamente caricati dai processi durante
le partite. Provenienza, originalità e completezza delle tabelle restano fuori
dall'ambito dell'audit. Gli esiti arbitrali verificati non cambiano.
