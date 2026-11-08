# Ametista

Nuovo concorrente originale di Hey Hay Dama, distinto da Opale C, che resta
conservata senza modifiche. ID `ametista`, servizio HTTP sulla porta 9016.
Sfida iniziata il 7 novembre 2026 alle 10:28:21 UTC, scadenza 18:28:21 UTC.
**Candidato congelato: search-v14 / manual-v4. Conferma conclusa:
3 vittorie, 4 patte, 29 sconfitte su 36 contro Kingsrow.**
Opale, sulle stesse 18 aperture nei due colori, perde tutte le 36 partite.
Ametista ottiene l'8,33% di vittorie e il 13,89% del punteggio disponibile:
vantaggio osservato su Opale, senza soddisfare il criterio statistico
conservativo di miglioramento; nessuna superiorità su Kingsrow.
[Risultati e limiti](../../arena/ametista/2026-11-07/RESULTS.md).

Seconda conferma separata: v14 **1V/7P/28S**, v17 **0V/6P/30S**,
36 partite ciascuna su 18 nuove aperture. V17 non viene adottata; resta v14.
Le due conferme non vengono sommate come una selezione unica.

L'utente ha autorizzato esplicitamente la ricerca originale per questa fase.
Le versioni v14 e v17 qui congelate rispettano i vincoli iniziali: pesi
manuali, nessun training, nessuna ottimizzazione automatica o peso preaddestrato,
nessuna copia di codice/pesi/libri/database di Kingsrow. Successivamente
l'utente ha [autorizzato training e dataset pubblici](../../arena/ametista/2026-11-07/authorization-extension.json)
per nuovi esperimenti separati: questa estensione non cambia retroattivamente
i metodi o i candidati delle conferme già concluse.

## Architettura

- Damiera a tre bitboard; generazione originale delle mosse con precedenze
  italiane e catture bloccanti. Ogni versione viene confrontata con l'arbitro.
- Rete manuale a 26 ingressi relazionali nella versione adottata manual-v4
  (24 nelle precedenti) e nodi ReLU sparsi. Materiale,
  mobilità, connessioni, isolamento, blocchi, vie libere di promozione e
  centralità delle dame. Due uscite: valore e instabilità.
- Ricerca PVS con approfondimento iterativo, estensione completa delle prese,
  tabella di trasposizione e ordinamento. La testa di instabilità limita
  le riduzioni delle mosse tardive quando la posizione è tatticamente delicata.
- Memoria delle posizioni della sola partita per le ripetizioni; nessun libro
  persistente, nessun database dei finali e nessuna distillazione dell'avversario.
  La revisione search-v8 include nella chiave della cache il contesto delle
  ripetizioni e permette riuso tra chiamate compatibili della stessa partita.
  Dopo una cattura o una mossa di pedina esclude dal contesto cache le posizioni
  precedenti irraggiungibili, mantenendo i contatori usati per le patte.
- Inferenza del grafo manuale compilata in IL da search-v11, con interprete
  alternativo quando il runtime non supporta codice dinamico. Coefficienti e
  ordine delle operazioni restano gli stessi; non è addestramento.

La novità è rispetto ai motori di questo progetto; non si rivendica una nuova
invenzione scientifica della ricerca o delle reti neurali. Le statistiche
temporanee per ordinare le mosse appartengono alla ricerca, non aggiornano
i parametri della rete. Cache e storia vengono azzerate fra partite.

## Revisioni sperimentali

Ricerca e pesi hanno versioni separate. Per esempio `search-v4/manual-v2`
indica una vecchia ricerca con il secondo file di pesi; non è `manual-v4`.
L'endpoint `/health` dichiara entrambi e l'hash del file realmente caricato.

| Pesi | Ingressi | Modifica manuale |
| --- | ---: | --- |
| manual-v1 | 24 | Valutatore iniziale a due uscite |
| manual-v2 | 24 | Peso del canale di promozione portato da 1,5 a 3 |
| manual-v3 | 24 | Premio alle dame quando l'avversario non ne possiede |
| manual-v4 | 26 | Ipotesi di pressione geometrica delle dame in maggioranza |

Manual-v4 è stata scelta manualmente con search-v14 per la conferma finale.
Nel confronto su otto aperture nuove, v14 ottiene 1 vittoria e 2 patte su
16 partite; v16 ottiene 1 vittoria e nessuna patta. Il campione di sviluppo
non dimostra superiorità generale. Entrambe vincono dalla stessa apertura
con due pedine in più per il Nero. I tentativi precedenti restano archiviati.
Le distanze ignorano gli ostacoli e non sono un database dei finali.
La conservazione del grafo precedente dopo l'espansione degli ingressi è
verificata separatamente dal nuovo termine. I vecchi file non sono sovrascritti.

I [risultati di sviluppo](../../arena/ametista/2026-11-07/RESULTS.md) distinguono
gironi conclusi, prove statiche e recupero tecnico. Il
[congelamento finale](../../arena/ametista/2026-11-07/final-freeze.json)
precede l'uso delle 18 aperture riservate alla conferma.

La variante successiva [search-v17-contactguard](../../experiments/Ametista.ContactSearch/README.md)
è un esperimento separato con gli stessi pesi: protegge dalle riduzioni le
mosse che creano catture obbligatorie. Dopo una patta aggiuntiva nel confronto
di sviluppo, ha concluso una seconda conferma appaiata con v14 su 18 nuove aperture:
v14 ottiene 1V/7P/28S e v17 0V/6P/30S, 36 partite per versione. La differenza
di score è -4,17 punti percentuali per v17 e non dimostra un miglioramento;
nessuna delle due versioni ha dimostrato superiorità su Kingsrow. Il
[rapporto supplementare](../../arena/ametista/2026-11-07/supplemental-confirmation-report.json)
e il [controllo di integrità](../../arena/ametista/2026-11-07/supplemental-final-integrity.json)
conservano esiti e limiti.
Un successivo [esperimento su dati umani FID](../../arena/ametista/2026-11-07/human-data/README.md)
ha prodotto la variante separata v18, che aggiunge un piccolo predittore lineare
al valore manual-v4 senza modificarlo. Nelle sei partite esplorative auditate
ottiene 1V/1P/4S, score 25%, senza interruzioni. Il controllo v14 sulle stesse
aperture ottiene 1V/3P/2S, score 41,67%: la differenza descrittiva è -16,67
punti percentuali per v18. Il controllo era stato aggiunto dopo risultati
parziali e tre aperture non costituiscono una conferma preregistrata o una
prova causale. V18 non è promossa; v14 resta il candidato principale.

Il progetto e i comandi di avvio qui sotto conservano la versione v14.

## Avvio e verifica

```powershell
dotnet run --project models/Ametista -c Release -- --verify
dotnet run --project models/Ametista -c Release -- --urls http://127.0.0.1:9016
```

Per usare una revisione precedente, ad esempio manual-v2, dalla radice del repository:

```powershell
$ametistaWeights = (Resolve-Path models/Ametista/Models/ametista-v2.json).Path
dotnet run --project models/Ametista -c Release -- --weights $ametistaWeights --urls http://127.0.0.1:9016
```

Ametista è registrata in coda al catalogo web e disabilitata per impostazione
predefinita. Con il servizio avviato, abilitarla localmente in un altro terminale:

```powershell
dotnet run --project src/Dama.Web -- --Arena:Models:6:Enabled true
```

L'indice 6 corrisponde alla voce con ID `ametista`; se la configurazione viene
riordinata, controllare l'ID prima di usare un override per indice. Durante un
girone congelato il servizio sulla porta 9016 è riservato al runner: giocare
dal web al termine del girone evita interferenze con le misure.

Le fixture non giocano partite. `--verify` verifica lo stesso file manual-v4
predefinito distribuito con il servizio, mentre `--verify-weights` consente
di verificare esplicitamente una revisione precedente. Il modello carica `Models/ametista-v4.json`,
oppure il percorso passato con `--weights`. `--write-weights <percorso>`
serializza il costruttore deterministico manuale; non esegue apprendimento.
Non usarlo per sovrascrivere una versione congelata dell'esperimento.

Il servizio implementa `dama-model/v1`, rispetta gli ID opachi delle mosse e
restituisce anche diagnostica di profondità, nodi e tempo. I 100 ms sottratti
al budget HTTP sono riservati al protocollo: 1100 ms richiesti assegnano
un budget di ricerca di 1000 ms, non una misura del tempo CPU effettivo.
Le mosse obbligate possono essere restituite subito.
Nel catalogo web Ametista usa anche `HttpGraceMs: 500`: questa tolleranza
copre il trasporto e la lettura della risposta, senza aumentare il budget
inviato al motore. Due tentativi iniziali avevano superato il solo limite
HTTP; il test con tolleranza ha eseguito una mossa legale e registrata.
La misura di gara usa il runner separato e mantiene i budget congelati.
Da search-v13, una richiesta concorrente riceve HTTP 503 senza aspettare
un nuovo budget in coda; la disconnessione del client cancella la ricerca.
Il servizio esegue una ricerca alla volta. Nei gironi usare il servizio
riservato e il lock del runner.

Per verificare un file specifico senza giocare partite:

```powershell
dotnet run --project models/Ametista -c Release -- --verify-weights models/Ametista/Models/ametista-v3.json
```

I controlli includono generatore contro arbitro, simmetria, interprete contro
inferenza compilata, minimax a profondità fissata, contesti di ripetizione,
terminali al limite interno e casi che esercitano riduzioni e successive
ricerche complete. Sono controlli sui casi coperti, non una dimostrazione
generale dell'esattezza della ricerca selettiva.

Progetto, preservazione, dati e revisioni:
[sessione Ametista](../../arena/ametista/2026-11-07),
[discussione comune](../../arena/kingsrow-challenge/chat.md).

Le partite reali vanno eseguite soltanto tramite il registro condiviso e il
lock della propria istanza Kingsrow (`arena/kingsrow-challenge/matches-9015.lock`
per Ametista), come fa `experiments/Dama.Challenge`. Il lock globale iniziale
resta documentato nei primi esperimenti; il protocollo successivo usa istanze
identiche separate e affinità di candidato e avversario allo stesso core logico.
Le prove di sviluppo hanno origine `test`; la conferma congelata ha origine
`benchmark`, aperture nuove e colori appaiati.
