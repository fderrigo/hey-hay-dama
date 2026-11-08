# Ametista — registro delle modifiche

## 10:28 UTC — avvio e preservazione

Registrati in session.json inizio, scadenza delle otto ore e SHA256 dei
sorgenti/pesi Opale C v3. Nessun file originale eliminato o sostituito.
Analizzati i 18 incontri storici contro Kingsrow per individuare problemi,
senza trasformare le mosse dell'avversario in etichette di addestramento.

## 10:31–10:36 UTC — autorizzazione e accordo

L'utente autorizza anche ricerca originale. AGENTE-A, AGENTE-B e AGENTE-C
concordano budget principale 1000 ms, un thread di ricerca, limite 1 GiB,
partite serializzate, set di sviluppo distinto da conferma, pesi manuali.
Il revisore metodologico suggerisce di distinguere vittoria singola,
progresso sul baseline e superiorità; assunto nel piano.
Ametista sceglie una rete con valore/instabilità e ricerca, distinta dalla
rete relazionale senza ricerca di Diaspro e dalla nuova Avventurina.

## 10:40–10:44 UTC — versione iniziale

Implementati bitboard e generatore, rete manual-v1, ricerca PVS e servizio
9016. Il costruttore dei pesi è deterministico e non legge risultati.
Materiale 100/265 e gli altri coefficienti sono scelte manuali iniziali,
non valori ricavati da un ottimizzatore. Le prese vengono estese interamente.
Superate 9015 posizioni e 40898 transizioni contro l'arbitro, con simmetria
della rete. Prima verifica limitata a 6002 fixture per un percorso relativo
errato: corretto il comando, ripetuta la verifica includendo le traiettorie.
Sorgenti e pesi congelati in frozen-v1 prima di revisionare il codice.
Avviate quattro partite di sviluppo registrate, su due aperture appaiate.

## 10:49 UTC — revisione della ricerca, pesi invariati

La prima partita v1 è persa in 68 semimosse. La diagnostica mostra normalmente
14–16 livelli, con errori che la ricerca non evita. Nessuna conclusione sui
quattro incontri finché non sono terminati.
Preparata search-v2: test vettoriale di esistenza delle prese, evitata la
generazione completa delle mosse nelle foglie tranquille. Stessa rete e
stesso significato delle mosse. Tutte le 9015 posizioni/40898 transizioni e
gli spostamenti bitboard elementari passano di nuovo. Il servizio v1 continua
il proprio girone immutato; la nuova build è separata in artifacts/ametista-v2.

## 10:52–11:00 UTC — primi risultati e protocollo operativo

V1 conclusa: zero vittorie, due patte, due sconfitte. Audit di 487 mosse e
quattro record superato. AGENTE-A segnala che alcune sue fixture HTTP avevano
raggiunto per errore la porta 9016: possibile interferenza di carico/cache,
annotata come limite del solo sviluppo. Nessuna conferma finale era in corso.

Tutti gli agenti approvano copie operative identiche di Kingsrow su porte
separate per evitare una coda incompatibile con la scadenza. Per C resta 9015,
lock matches-9015.lock, affinità di entrambi i motori sul processore logico 0.
La CPU ha 10 core fisici/12 processori logici; altre coppie lavorano in parallelo
su altri processori logici. Questa asimmetria è dichiarata, non nascosta.

Search-v2 avvia otto partite con gli stessi pesi manual-v1. Prima vittoria
in 100 semimosse da Nero sull'apertura 20261107; a colori invertiti sconfitta
in 76. Audit dei primi tre incontri: 236 mosse legali e record coerenti.
Non è superiorità: la prima coppia vale 50%. Il riferimento è stato riavviato
fra i gironi, mantenendo DLL e risorse; non si isola causalmente la modifica.

## 11:04 UTC — rete manual-v2 e search-v3, preparazione separata

Nella patta v1 da Nero il vantaggio diminuisce durante le corse a promozione:
questa osservazione motiva una revisione manuale, non l'imitazione delle mosse.
Il coefficiente del canale di pedine passate sale da 1.5 a 3; la geometria
ora esclude una corsia apparentemente libera se una dama avversaria può
intercettarne una casa di promozione in tempo. Nessuna ricerca di coefficienti.

Encoder relazionale riscritto con operazioni bitboard; una versione scalare
di controllo ne verifica l'identità numerica. Tutte le 9015 posizioni e 40898
transizioni passano, insieme a simmetria e confronto degli encoder. Il servizio
search-v2 continua il girone congelato; search-v3/manual-v2 è in una build distinta.
I file dei pesi precedenti e tutti i sorgenti Opale risultano ancora invariati.

## 11:16 UTC — search-v4 congelata, prima del prossimo girone

Il girone v2 si è concluso 1V/1P/6S: audit di 893 mosse, quattro aperture
appaiate, nessuna interruzione. V3 prosegue con il servizio congelato.
V4 rimuove l'arresto preventivo a metà budget e alza il limite di profondità
nominale da 40 a 80, mantenendo l'ultima iterazione interamente completata.
Stessi pesi manual-v2. Nessuna ottimizzazione automatica dei coefficienti.
I 32 confronti con minimax indipendente e le 9015 posizioni passano.
Il controesempio finale suggerito da AGENTE-B è stato verificato con codice
indipendente: dame in 5 e 28 dopo due mosse forzate, vittoria in 9 semimosse.
Questo giustifica evitare scorciatoie che dichiarino patta ogni dama contro dama.
Aggiunti Ametista e il runner registrato Dama.Challenge alla soluzione.

## 11:22 UTC — sviluppo v3 concluso e integrazione web verificata

V3/manual-v2: 0V/2P/6S su8, nessuna interruzione; audit1070mosse e quattro
coppie complete. Non emerge un miglioramento rispetto al girone precedente.
V4 avviata sullo stesso Kingsrow, stessa affinità e stessi quattro seed.
Sostituito il solo servizio Ametista dopo la conclusione del runner v3.

AGENTE-A ha ripristinato Kingsrow all'indice4 e Diaspro in coda; aggiunta
Ametista all'indice6, disabilitata di default come suggerito da AGENTE-B
per non introdurre endpoint non disponibili in produzione. Build web senza
errori; host locale temporaneo5323, override di abilitazione, /api/models
restituisce Ametista. Host di verifica arrestato; nessuna partita o deploy.

Creato experiments/ametista_diagnostics.py: sola analisi dei replay completi,
nessuna mossa generata né aggiornamento di pesi. Esclude le decisioni a
profondità zero dalle medie di ricerca e dichiara i limiti del picco memoria.
Iniziato diario editoriale privato, esplicitamente provvisorio; riservate
18 aperture di conferma in confirmation-plan.json prima di generarle.

## 11:23 UTC — search-v5 preparata, servizio v4 ancora in prova

La rete era invocata nei nodi quieti interni anche quando il suo risultato
non poteva influenzare la ricerca. Ora l'inferenza interna viene eseguita
solo se sono possibili riduzioni tardive: nodo non principale, profondità
almeno4 e almeno5 mosse. Le foglie continuano a essere valutate sempre.
È un'ottimizzazione del codice originale, senza modifica dei pesi né nuovi
criteri posizionali. Nessun suggerimento decisionale copiato dai colleghi.
Verifica9015posizioni/40898transizioni e32confronti di riferimento superata,
insieme al finale di zugzwang. Snapshot v5 separato; v4 rimane invariata
nel girone in corso. Non attribuisco un guadagno di forza prima di misurarlo.

## 11:26 UTC — equivalenza v4/v5 a profondità fissata

Nuovo strumento Ametista.Profile carica separatamente i nostri binari congelati
tramite reflection. Campiona32damieri dai replay di sviluppo e li usa come
fixture con storia/contatore azzerati identicamente; nessuna partita giocata.
Profondità7: stessa mossa, valore, profondità e numero di nodi per tutte32.
Tempi aggregati317ms contro380ms su singole esecuzioni con JIT e carico:
non dimostrano un guadagno; il risultato utile è l'equivalenza osservata.
Report e sorgente del verificatore conservati. Affinità sul logico6 separato
dai logici delle tre coppie di gioco, ma carico concorrente ancora presente.

## 11:30 UTC — estensione originale v6 e precisazione statistica

V6 non consuma profondità nominale in presenza di una sola cattura legale
(distinta per posizione risultante). Ogni estensione rimuove materiale:
non estende i cicli quieti. I pesi restano manual-v2. Passano i32confronti
con minimax indipendente, ora con la stessa definizione di orizzonte, e
9015posizioni/40898transizioni. Non confrontare la profondità nominale
con v4/v5 senza considerare questa diversa definizione.
V5 rimane una revisione intermedia verificata per equivalenza, senza girone
reale autonomo; dopo v4 proverò v6, dichiarando entrambe le modifiche.

AGENTE-A ha correttamente segnalato che varianza campionaria zero non è
certezza. L'audit ora conserva l'intervallo t e aggiunge Hoeffding per
punteggi di coppia limitati a[0,1]; una dichiarazione di superiorità richiede
anche questo limite inferiore sopra0.5. Vale sotto ipotesi di aperture
campionate indipendentemente, non prova validità universale su ogni posizione.
Decisione presa prima di vedere le aperture di conferma.

AGENTE-B alle12:29locali ha dichiarato una seconda coppia su logico6.
I miei tre profili brevi su6 erano già terminati; nessun processo residuo.
Le prossime eventuali verifiche useranno un logico concordato diverso.
Il carico concorrente aggiuntivo durante v4 è una limitazione registrata.

## 11:33 UTC — preparazione del controllo omogeneo

Letto il protocollo originale Opale senza modificarlo: riserva25ms contro
100ms degli adattatori Kingsrow/Ametista. Il runner ora accetta una riserva
esplicita per giocatore, default100, per offrire effettivamente1000ms anche
al controllo Opale (HTTP1025). Tutte le configurazioni precedenti restano
compatibili; build e audit dei replay passano. Non è stata avviata alcuna
partita del controllo o generata un'apertura di conferma.

AGENTE-A ha annunciato il passaggio di Diaspro a ricerca originale dopo
quattro gironi senza punti. Questo aggiorna la descrizione iniziale dei tre
approcci; non implica condivisione di implementazioni o di pesi.

## 11:35 UTC — v4 conclusa, v6 in gioco

V4:1V/2P/5S, quattro coppie, nessuna interruzione; audit1032mosse e registro
coerente. Punteggio normalizzato25%, contro12.5% della v3: differenza osservata
su soli quattro abbinamenti, non progresso dimostrato statisticamente.
La vittoria è sul secondo seed da Nero; una patta arriva da Bianco sul quarto.
Avviato girone v6 con gli stessi quattro seed, pesi manual-v2 e Kingsrow9015
rimasto in esecuzione. Candidato sostituito solo dopo uscita0 del runner v4.
Affinità logica0 ripristinata al nuovo processo. Nessun dato di conferma usato.

## 11:42 UTC — v7 scartata dopo misura controllata

Il generatore v7 calcolava origini ammissibili per catture e mosse quiete
prima dei cicli per pezzo. Tutte le9015posizioni/40898transizioni passano;
32fixture a profondità10 restituiscono stessi punteggi, mosse e2.127.686nodi.
Le prime due misure si contraddicono: v6/v7 2937/2276ms, poi2446/3292ms.
Il profiler è stato corretto per riscaldare la prima fixture tre volte,
registrare tempoCPU e disabilitare il tiering soltanto nei suoi processi.
Nuove misureCPU: v6 1531.25 e1515.625ms; v7 1562.5 e1718.75ms.
Equivalenza di mosse/valori/nodi verificata anche fra tutte le prove calde.

Non promuovo v7: nessun vantaggio affidabile e possibile regressione. Conservo
snapshot, binari e report; Board.cs/Search.cs tornano esattamente a v6.
Nessun peso modificato, nessun avversario interrogato dalle fixture. Tutti
i profiler sul logico8 sono conclusi. Il servizio9016 del girone resta v6.
L'idea di accelerare la generazione era condivisa nella discussione con B,
ma implementazione e verifiche sono nostre; il risultato negativo resta.

## 11:48 UTC — cache contestuale v8 e risultato v6

V6 conclusa0V/2P/6S su8, nessuna interruzione; audit920mosse e quattro
aperture appaiate. Il punteggio12.5% non sostiene una promozione dell'estensione
come miglioramento autonomo rispetto a v4. Rimane una variante di sviluppo.

V8 aggiunge alla chiave TT un hash dei conteggi delle posizioni viste. Dopo
cattura o mossa di pedina il solo contesto cache viene azzerato: le vecchie
posizioni sono irraggiungibili, mentre i conteggi dell'arbitro restano intatti.
La tabella può riusare punteggi tra chiamate della stessa partita se coincide
il contesto. Il limite di orizzonte protegge il riuso vicino al tetto di96ply.
Cache e statistiche restano azzerate al cambio partita. Pesi manual-v2 invariati.

Verifiche:9015posizioni/40898transizioni;32minimax indipendenti e32richieste
ripetute sulla cache;4storici alternativi nello stesso finale a due dame.
620riusi fra chiamate esercitati nelle fixture, risultati coerenti. Nuovi
contatori nelle risposte HTTP distinguono hit totali e hit da chiamate precedenti.
Non è una prova universale di correttezza o di aumento della forza.

Servizio v6 sostituito solo dopo uscita0 del runner. V8 avviata sullo stesso
9016/logico0, stessi quattro seed, avversario Kingsrow9015 ancora invariato.
I confronti prestazionali statici attendono un core concordato: B ha occupato
anche8/10 con nuove coppie, carico aggiuntivo esplicitamente dichiarato.

## 11:52 UTC — v9 separa cache della rete e cache della ricerca

La valutazione neurale è funzione del solo damiere e lato al tratto; i punteggi
ricercati dipendono anche dalla storia. V9 conserva separatamente262144valutazioni
statiche, indicizzate dalla posizione, senza cambiare i parametri della rete.
La cache statica viene azzerata fra partite, come quella contestuale. Nessun
aggiornamento dei pesi, nessuna persistenza o etichetta esterna.

Le fixture ora verificano su9015posizioni che cambiare il contatore quieto
non alteri l'inferenza: una futura feature dipendente da quel contatore
richiederebbe di estendere la chiave. Passano generatore, encoder, simmetria,
32minimax,32richieste con riuso e4storici. Diagnostica include hit statici.
Il servizio in gioco resta v8. Le prime tre partite v8 sono perse, anche se
il riuso tra turni è realmente attivo:386895,1311207 e444478hit. Una metrica
interna che aumenta non dimostra un miglioramento della forza.

## 12:02 UTC — finestre adattive v10 e controllo versione del runner

V10 parte dal valore dell'iterazione precedente con intervallo iniziale
32+8*rischio, massimo128, a partire da profondità4. Se il valore esce
sotto/sopra la finestra, ripete allargandola; una ricerca interrotta conserva
la scelta dell'ultima iterazione conclusa. Non promuove un semplice limite
fail-high/fail-low a valutazione definitiva. Pesi invariati manual-v2.

Verifiche aggiuntive:12confronti con minimax esaustivo a profondità4;
il finale di zugzwang esercita realmente un allargamento, oltre a verificarne
il risultato. Restano32confronti a3,32riusi,4storici e9015posizioni.
V9 e v10 sono preparate separatamente; il girone attivo resta v8.

Il runner ora può verificare campi health attesi oltre allo SHA. Test negativo
con versione deliberatamente errata rifiutato prima di creare partite, JSONL
vuoto e nessuna partita reale. Questo impedisce di attribuire risultati a
un binario diverso da quello dichiarato quando i pesi restano gli stessi.

## 12:05 UTC — v8 conclusa, v9 avviata

V8:0V/2P/6S, audit1012mosse, otto partite registrate e nessuna interruzione.
Riuso dei punteggi attivo ma nessun miglioramento di punteggio su questo set.
V9 parte sugli stessi quattro seed e sulla stessa coppia9016/9015, logico0,
pesi manual-v2, dopo conclusione del runner v8. Nuovo runner verifica
model/version/search/sha256 esatti prima di iniziare le partite.
La finestra adattiva v10 rimane fuori dal servizio v9 per isolare le revisioni.

## 12:23 UTC — manual-v3 preparata e v10/manual-v2 in gara

Nei replay v8 l'avversario promuove per primo in5/8partite; nei tre casi
opposti Ametista ottiene due sconfitte e una patta. La prima dama non è
quindi una spiegazione sufficiente. Ipotesi manual-v3: bonus75 per dama
solo finché l'avversario non ha dame, realizzato da due nodi ReLU con
pesi[1,-12] sui conteggi delle dame. Il75 è scelto a mano, sotto il valore
100 di una pedina. Il limite12 chiude il gate quando compare una dama nemica.
Ispirazione: osservazione AGENTE-B sul valore della prima dama e nostri replay;
nessun codice, coefficiente o tabella trasferito. Non è fitting sui risultati.

File nuovo ametista-v3.json, SHA832de82282bd161399bb3af19c8740ec4ec69b4628ef500e3afce4a81992db12.
Passano9015posizioni/40898transizioni,32+12minimax,32riusi,4storici e quattro
casi limite del nuovo gate (+75,+150,0,-75). I filev1/v2 sono invariati.
Esportatore rifiuta di sovrascrivere file congelati; verifica può caricare
esplicitamente il file scelto. Snapshot include sorgenti e pesi di confronto.

Corretto il nome fisico models in Models, coerente con csproj e percorsi
anche su filesystem sensibili al caso. Il secondo rename Windows ha fallito:
ho ripristinato i file nei percorsi corretti tramite copie verificate SHA,
poi spostato il backup temporaneo sotto artifacts dopo controllo dei percorsi.
Nessun peso perso o modificato; il servizio v9 aveva già caricato la rete in
memoria. Nessuna partita interrotta. Build finale include tutti e tre i file.

V9 conclusa0V/0P/8S, audit952mosse e registro coerente. Non la promuovo come
miglioramento. V10/manual-v2 avviata dopo uscita0 del runner, stesso Kingsrow,
stessa affinità, stessi4seed. Userò lo stesso binario con i pesiv3 nel confronto
successivo. Nuovo piano qualification-plan.json:4aperture di sviluppo diverse
per confrontare la v4 semplice e una candidata scelta manualmente. Conferma
finale non generata né usata per scegliere i parametri.

## 12:25 UTC — confronto statico v8/v9 sul core concordato

AGENTE-B ha liberato il logico8 alle13:20:47locali. Eseguiti due confronti
in ordine inverso,32fixture a profondità10, tre chiamate di warmup iniziale,
JIT tiering disabilitato nei soli profiler. Identici valori, mosse e2.485.522nodi.
TempoCPU v8/v9:3031.25/2875ms, poi3140.625/3078.125ms. Risparmio osservato
circa2-5%; tempo reale ancora influenzato dal carico condiviso. Non è un
miglioramento di forza dimostrato e non cancella le otto sconfitte di v9.
Tutti i profiler conclusi; nessuna partita reale aggiunta da queste fixture.

## 13:38:35 locale — inferenza compilata e confronto manual-v3
V10/manual-v2 chiusa 0V2P6S; audit legale completo 1170 mosse e registro coerente.
Avviato manual-v3 con lo stesso binario search-v10, quattro aperture di sviluppo identiche, Kingsrow invariato e affinity0. Non aggiorno pesi automaticamente.
V11 compila il grafo originale in IL con identico ordine di operazioni e coefficienti; fallback interprete. Documentazione consultata solo sulle API .NET DynamicMethod. Entrambi i file v2/v3 concordano con interprete su9015posizioni,40898transizioni,32+12minimax e4storici; v3 supera anche4casi gate. Nessun training. Misure CPU separate in corso prima di decidere se adottarla.

## 13:39:32 locale — esito prestazionale v11
Due ordini inversi,32fixture/profondita10: identici mosse, valori, profondita e nodi.
CPU interprete/compilata2796.875/2546.875ms e2875/2531.25ms: riduzione osservata9-12%. Tempi reali4369/4318 e4334/4130ms, piu rumorosi. Warmup3chiamate, tiered compilation disabilitata solo nei profiler, CPU8 concordata. Report profile-compiled-comparison.json. Nessuna prova di forza da questo risultato.
Snapshot frozen-search-v11 conserva sorgenti e pesi con hash. Candidata per successiva misura, non sostituisce il girone manual-v3/search-v10 attivo. Tutti i profiler terminati.

## 13:40:31 locale — ipotesi v12, accesso cache prima della generazione
Sposto il controllo della TT contestuale prima della generazione delle mosse e della valutazione del rischio. Una voce valida era stata salvata solo per una posizione non terminale con identico contesto di patta; sui miss restano precedenza terminale/patta e ricerca immutate. Non cambio pesi, profondita o riduzioni. L'ipotesi e risparmiare lavoro sui tagli gia dimostrati, da verificare con minimax e confronti identici di nodi/mosse/valori. Variante non in gara.

## 13:41:24 locale — v12 non adottata
Verifiche funzionali v2/v3 passate,32fixture identiche per mosse/score/profondita/nodi. Tuttavia CPU v11/v12 nei due ordini1812.5/2453.125ms e2953.125/2562.5ms: segno invertito e carico troppo variabile. Nessun vantaggio robusto dimostrato. Archivio frozen-search-v12 e risultati, ripristino Search.cs della v11; nessun girone v12 avviato. Il costo aggiunto di calcolare la chiave contestuale anche su terminali potrebbe compensare parte del risparmio, ma non e isolato da questa misura.

## 13:48:19 locale — revisione indipendente, v13 e servizio HTTP
REVISORE-METODO ha rilevato: valutazione statica prima dei terminali al limite ply95; attesa sul lock HTTP esclusa dal budget e mancata cancellazione alla disconnessione. Non dimostrano esiti arbitrali errati dei gironi sequenziali, ma sono difetti reali.
V13 controlla assenza mosse, poi patte, poi rete al limite interno. Quattro fixture esplicite confrontate con Rules.GetOutcome passano con v2 e v3, insieme a9015posizioni/40898transizioni e controlli minimax precedenti.
HTTP ora rifiuta subito con503 una richiesta concorrente e propaga RequestAborted alla ricerca; finalmente rilascia il lock anche in errore. Fixture su9031/core8: prima richiesta200 in3027ms per3000ms ricerca; concorrente503 in39ms; dopo disconnessione di richiesta5000ms il servizio risponde nuovamente in86ms. Nessuna partita reale; report http-concurrency-search-v13.json. Servizio temporaneo terminato,9016/9015 invariati.
Aggiunta anche protezione contro sovrascrittura al vecchio esportatore manual-v1, coerente con v2/v3. Nessun file pesi modificato.
Resta da coprire esplicitamente LMR: i confronti esatti a profondita3-4 non la esercitano. Non presento quei test come prova universale della ricerca selettiva.

## 13:51:24 locale — selezione manuale per qualificazione, non finale
Manual-v3/search-v10 termina1V1P6S, audit973mosse e registro coerente. Manual-v2/search-v10 era0V2P6S sulle stesse aperture. Un punto in piu non prova miglioramento causale.
Scelgo per la qualificazione manual-v3/search-v13 contro manual-v2/search-v4: prima mantiene il gate della dama esclusiva e inferenza compilata, corregge il limite terminale; seconda e il riferimento piu semplice con migliore score osservato su8. Scelta manuale prima di nuove aperture, non ottimizzazione automatica dei pesi.
V13 aggiunge contatori LMR:5fixture depth6 concordano con minimax esaustivo per entrambi i file,117riduzioni e22recuperi completi esercitati ciascuno. Test limitati ai casi coperti, non prova generale di ricerca selettiva esatta. Snapshot congelato per questo confronto.
Le4aperture846201..846204 sono sviluppo, non le18finali. Per ogni seed entrambi giocano i due colori contro9015, in serie; ordine delle versioni invertito sui seed alterni. Nessun warmup aggiuntivo: proposta ancora senza assenso dei colleghi. Nessuna modifica durante questa qualificazione.

## 13:58:56 locale — ipotesi manual-v4/search-v14, prima delle misure
Nuovi ingressi26: per ogni pezzo avversario sommo14 meno le distanze geometriche delle due dame proprie piu vicine (distanza mancante7). Il termine vale solo con maggioranza di dame tramite differenza di due ReLU, che elimina l'offset del gate. Coefficiente manuale0.5: bonus massimo inferiore al valore100di una pedina su damiere legali. Scelta motivata dal nostro replay patto89b49c40a3604d128e407ed0e29e3c72, non adattamento automatico o valori Kingsrow.
Limiti dichiarati: ignora ostacoli e non dimostra che un finale sia vinto; potrebbe incentivare avvicinamenti inutili o pericolosi, da verificare. I vecchi ingressi e coefficienti sono preservati; soltanto riferimenti ai nodi interni slittano di2per l'espansione. Controlli progettati per equivalenza del grafo precedente, gate e simmetria. Nessuna variante attiva della qualificazione modificata.

## 14:00:12 locale — manual-v4 verificata, non ancora in partita
SHA7e8ea1c6a7c897e5d3d5c42a43511a26ad0ebd32db904fff670be488c7913138. Passano9015posizioni/40898transizioni, accordo compilata/interprete, equivalenza del vecchio grafo rimappato, simmetria,3casi pressione,32+12minimax,5fixture LMR (117riduzioni/22recuperi),4storici e4casi limite. Il finale di due dame resta confermato29991 dal riferimento. Vecchi pesi v1/v2/v3 invariati.
Nessuna misura di velocita aggiuntiva o pretesa di forza: solo verifica funzionale; variante archiviata frozen-search-v14. Per isolare il nuovo termine occorrera usare lo stesso binario con manual-v3 e manual-v4 dopo la qualificazione attiva. Le18aperture finali restano escluse.

## 14:02:46 locale — controllo Opale preparato, nessuna partita
I7hash originali sono ancora invariati. La vecchia DLL bin/Release era obsoleta: avviata nella copia isolata ha stampato il vecchio messaggio solo-export ed e uscita0, senza servizio o partite. Non basta un file DLL presente per provare la versione.
Compilati i sorgenti preservati in artifacts/ametista-opale-control-build,0errori/avvisi. Servizio9014 idle, health conferma Opale C manual v3, affinity0; opale-control-ready.json registra binario, sorgenti, pesi, PID e rifiuto del vecchio eseguibile. Riserva25ms, richiesta1025ms per1000effettivi. Nessuna apertura finale generata: controllo partirà soltanto dopo freeze Ametista.

## 14:12:35 locale — v15, stack delle ripetizioni
Ipotesi originale di costo: evitare inserimenti/rimozioni nel dizionario per ogni nodo. Mantengo immutabili i conteggi osservati e uso array del percorso, conteggio corrente memorizzato e indice dell'ultima mossa irreversibile. Le sole transizioni reversibili cercano occorrenze nel breve segmento raggiungibile; Pop ripristina limite e hash contestuale. Pesi e algoritmo di ricerca invariati.
Verifiche v3/v4 passano:1004controlli di push/pop e hash contro oracolo a dizionario completo su8cammini sintetici, oltre alle verifiche precedenti incluse49posizioni minimax e LMR. Prima compilazione del test fallita per annotazioni nullable di reflection, corrette; nessun errore runtime rimasto. Non dichiaro speedup prima di una misura appropriata.
Snapshot frozen-search-v15 conserva la variante; nessun servizio di gioco aggiornato. Il confronto pressione gia pianificato resta search-v14 per entrambi i pesi, senza introdurre anche questa variabile.

## 14:18:52 locale — qualificazione conclusa e confronto pressione avviato
Tutte16partite di qualificazione concluse senza interruzioni e auditate. V4/manual-v2:0V0P8S; v13/manual-v3:1V0P7S. Il report verifica uguaglianza delle aperture, colori, budget e versioni; la differenza e soltanto nella quarta apertura. Conservo manualmente v13 come riferimento, ma quattro coppie non provano superiorita.
Solo dopo uscita0 del runner e report completo ho fermato i miei servizi9016/9018 e avviato per entrambi lo stesso binario search-v14: manual-v4 su9016, manual-v3 su9018.16partite di sviluppo sui4seed iniziali, ordine versioni alternato, stesso Kingsrow9015 e affinity0. Pressure-comparison-plan/selection documentano scelta prima dei risultati, SHA e calendario. Nessun aggiornamento automatico di pesi, nessun uso del set finale.

## 14:28:12 locale — revisione statica v15 e collaborazione sull'audit
REVISORE-METODO non ha trovato difetti concreti nel passaggio v14/v15 per storie coerenti e mosse legali. Conferma l'irreversibilita del materiale/promozione/avanzamento, conteggi observed+path prima del limite e solo segmento raggiungibile dopo, ripristino finally/Pop e reset Choose. CurrentOccurrences differisce intenzionalmente per vecchie chiavi ormai irraggiungibili, non usate dai callsite. Assunzioni: storia valida e assenza collisioni hash. Nessuna pretesa prestazionale prima della misura.
Audit indipendente richiesto da B:48partite/6045mosse Avventurina coerenti con arbitro/registro. Report pubblico peer-audit-avventurina conserva anche limiti su provenienza/hash, tempi dichiarati vs osservati, e confronti con/senza tabelle. Solo dati, nessun codice/peso/tabella di un concorrente letto o copiato. Ho inoltrato l'esito nel canale comune e richiesto il collegamento documentale agli hash completi.

## 14:35:56 locale — verifica web finale preparata
Aggiunti comandi separati --web-check e --interrupt-web-check al runner, compilati in artifacts/challenge-webcheck senza cambiare il binario v6 attivo nei gironi. Il controllo crea una sola partita reale tramite API con origine test, verifica registrazione prima della prima mossa, catalogo/modello congelato e mossa contro arbitro; conserva report senza token.
Dopo arresto del solo host web temporaneo, l'altro comando marca esclusivamente quella partita test di Ametista-Verify come interrotta nel registro comune, senza punti o vittorie artificiali. Directory stanze temporanea separata e non riavviata dopo interruzione, per evitare riconciliazione verso active. Nessuna partita ancora avviata con questi comandi: esecuzione dopo le misure sul candidato finale.

## 14:37:58 locale — ipotesi v16, cache delle sole mosse
Aggiungo alla v15 una cache distinta keyed board per sole indicazioni di ordinamento. La cache dei punteggi conserva il contesto completo delle ripetizioni; quando non offre una mossa, la nuova cache puo ordinare una legale gia generata, senza restituire valori o saltare terminali/patte. Memoria azzerata al cambio partita, pesi invariati. Ipotesi: recuperare ordinamento utile nei finali con contesti diversi, senza reintrodurre riuso scorretto dei valori. Variante non in partita; controprova funzionale prima di misure. Nessun dato/peso/codice altrui usato.

## 14:40:26 locale — v16 verificata e piano ricerca
V16 passa controlli con manual-v3/v4:9015posizioni/40898transizioni,49confronti minimax,118riduzioni/23recuperi LMR,1004check stack,4storici,4limiti terminali. Cache ordinamento esercitata anche nel finale a due dame (213hit), valore29991 confermato. Snapshot congelato, nessun servizio di gara aggiornato.
AGENTE-B ha dichiarato stop copie Kingsrow sui logici6/9/10 alle14:36:18locali: nuovo cambio di carico durante confronto pressione, da considerare nei tempi. Dopo l'intero girone misurero le varianti sul mio logico0 al posto delle partite, senza sovrapporre profiler al mio motore.
Piano search-comparison-plan.json riserva8nuove aperture di sviluppo375101..375108,16partite per ricerca con gli stessi pesi. Scelta manuale della ricerca candidata dopo controlli funzionali/prestazionali, pesi dopo confronto pressione completo. Nessuna ottimizzazione automatica o uso delle18aperture finali; congelamento finale ancora previsto entro15:30UTC.

## 14:44:37 locale — confronto pressione concluso, scelta manuale
16partite/1995mosse auditate, nessuna interruzione. Stesso binario v14: manual-v3 2V0P6S, manual-v4 2V1P5S. Win rate25% per entrambi; score25% vs31,25%. Unica differenza e la patta da Bianco nel quarto seed; non e una dimostrazione statistica di superiorita del termine.
Scelgo manual-v4 per il prossimo confronto di ricerca: conserva le due vittorie e aggiunge una patta in questa prova. I pesi sono congelati prima delle8nuove aperture375101..375108. Mantengo tutti i vecchi file. Gli ultimi gironi risentono del calo di carico dopo lo stop dei processi B, dichiarato alle14:36.
Runner pressione terminato0, quindi ora nessuna nostra partita attiva. Misuro v14/v15/v16 sul nostro logical0 al posto delle partite, stessi pesi manual-v4, fixture e condizioni, ordine inverso per controllare rumore. Nessun profiler in parallelo al nostro Kingsrow.


### 2026-11-07, 14:51 locale ? scelta ricerca v16
Confrontati v14/v15/v16, stessi pesi manual-v4, 32 fixture depth10, CPU0, tre chiamate di warmup e tiered compilation disattivata solo per i profiler. Due ordini: v14 1156/1047 ms CPU, v15 953/1078, v16 984/938. V15 conserva nodi/mosse/valori ma il guadagno di tempo cambia segno; v16 riduce i nodi da 2494905 a 2440296, cambia un valore e una mossa. Il suggerimento di ordinamento interagisce con LMR: non dichiaro equivalenza o maggiore forza.
Scelgo manualmente v16 per 32 partite di sviluppo contro Kingsrow: 16 v14 e 16 v16, stessi pesi v4, otto aperture nuove, ordine alternato, origine test, CPU0 e servizio9015 seriale. Configurazioni e hash salvati prima delle partite. Nessun aggiornamento automatico dei pesi. Idea originale; confronto metodologico con AGENTE-A/B su congelamento e separazione della conferma.


### 14:56:29 locale ? revisione helper web
Il REVISORE-METODO ha rilevato assenza di attestazione endpoint e report preparato dopo la creazione. Spostata apertura/flush del report prima del POST, registrata directory CompetitionStore, corretto commento su Reconcile. Verifica del lancio effettivo obbligatoria nella checklist. Build helper separata passata; nessuna partita creata e binari della serie in corso invariati.


### 15:01:37 locale ? preparazione default di distribuzione
Allineato Program.cs: servizio senza --weights e --verify caricano entrambi il file manual-v4 distribuito. I pesi v4 erano gia stati scelti prima del confronto ricerca e sono identici per entrambe le versioni; nessuna modifica ai coefficienti o alla ricerca. README aggiornato. I processi della serie usano ancora i binari congelati v14/v16 e percorsi espliciti: non ricompilati o riavviati. Build/publish e verifica del nuovo default restano da eseguire prima del congelamento finale.


### 15:15:01 locale ? aperture e interpretazione delle vittorie
Dai soli replay gia giocati: le due aperture iniziali in cui manual-v4/search-v14 aveva vinto col Nero partivano, dopo sei semimosse casuali, da12pedine nere contro11bianche. Le prime tre aperture nuove hanno11contro11; la quarta ha ancora12nere contro11bianche, ma v16 perde anche col Nero. Il materiale non spiega da solo la forza della posizione o l'esito: segnala perche due vittorie su aperture riutilizzate non provano superiorita generale. Colori invertiti mantengono equo il confronto fra varianti sulla stessa apertura. Non ho ispezionato le aperture finali riservate. Evidenza: development-opening-material.json.


### 15:20:57 locale ? recupero tecnico confronto ricerca
Turno Kingsrow al ply148 timeout HTTP35s; partita497fffcd785a4ed58ec869bdd3b90c71 interrotta e auditata senza punti. Runner session3774 terminato exit1; servizi health regolari. Causa sconosciuta. Coppia v16/seed375105 ripetuta integralmente con nuovi ID in recovery1, originale conservato (una sconfitta e una interruzione). Manifest di recupero prima della ripresa; totale previsto34avvii per32esiti appaiati selezionati, originali separati. Nessuna modifica ai motori o pesi.

### 15:58:21 locale — selezione, pacchetto e integrazione finale
Confronto chiuso: v14/manual-v4 1V2P13S, v16/manual-v4 1V0P15S; 32 esiti appaiati, più una sconfitta e una interruzione originali conservate. Scelta manuale v14: stesso win rate 6,25%, due patte in più. Entrambe vincono solo sull'ottava apertura col Nero a due pedine di vantaggio. Nessuna superiorità generale dimostrata.
Ripristinati Search.cs e Verification.cs dal congelamento v14, preservata v16. Il primo publish ha segnalato il contatore OrderingCacheHits rimasto nel protocollo: rimosso perché appartiene alla v16. Il pacchetto successivo passa --verify sul file predefinito manual-v4: 9015 posizioni, 40898 transizioni e controlli di ricerca. Tutti i vecchi pesi sono inclusi. Hash sorgenti/binari/config in final-freeze.json, antecedente alle aperture finali.
Test web reale: due tentativi hanno prodotto502, il secondo conserva il messaggio di timeout. Corretto il livello HTTP del web: nuova tolleranza configurabile HttpGraceMs, default0 per gli altri modelli,500 per Ametista; ThinkTimeMs inviato rimane1100 e ricerca1000. Cinque fixture mirate passano. Una prima esecuzione aveva un controllo fragile che presumeva la ricezione della richiesta anche dopo una cancellazione anticipata; il controllo del budget è ora sulla richiesta riuscita, quello di timeout resta separato.
Terzo test web passato: mossa legale, registrazione prima/dopo,1195ms complessivi. Tutti e tre gli ID API sono interrotti senza punti dopo arresto dei rispettivi server temporanei. Nessun cambiamento al candidato congelato. Le prove verificano API e registrazione, non l'intera usabilità del browser.
Controllo finale36Opale avviato alle15:56locali, poi36Ametista sulle stesse18aperture riservate. Nessuna modifica usando i risultati finali. AGENTE-B segnala pressione RAM: richiesto ridurre le proprie tabelle e liberare carico extra; la variabilità della macchina condivisa resta un limite dichiarato.

### 16:07:48 locale — controllo Opale concluso, conferma Ametista avviata
Opale C manual-v3:0V0P36S,1944mosse auditate,18aperture appaiate,zero interruzioni. Stessi1000ms di budget configurato, riserva protocollo25ms. Non trasformo l'intervallo t degenerato su risultati tutti uguali in certezza: l'audit riporta anche il limite conservativo non nullo.
Prima di avviare36Ametista sulle stesse aperture ho ricontrollato tutti gli hash di sorgenti, pesi, binari e configurazioni congelati. Nessuna modifica in base alle sconfitte del controllo. La misura di Ametista è ora in corso; conclusioni ancora aperte. Riscontro sulla riduzione del carico aggiuntivo dei colleghi non ancora ricevuto.

## 16:27:10 - Integrita e riscontro sul carico durante la conferma
Controllati 26 file: sorgenti, pacchetto, configurazioni, runner congelati e sette file originali Opale; tutti gli hash coincidono. Evidenza: confirmation-integrity-midrun.json, non attestazione finale.
AGENTE-B ha risposto alle16:22:24: tabelle ridotte prima16:06 e due processi Python senza affinita chiusi verso16:22. Dichiarazione conservata in confirmation-colleague-load-update.json; non prova indipendente del carico storico. Nessuna modifica al candidato, nessuna nuova partita avviata da questa verifica.

## 16:27:42 - Revisione indipendente del resoconto finale
Il revisore conferma il criterio statistico prudente sulle18coppie e segnala protezioni mancanti nel solo script di analisi. Aggiunti rifiuto di Python ottimizzato, controllo riserva Kingsrow100ms e hash degli input nel rapporto; nessun cambio a motore, pesi, calendario o criterio statistico. Tre controlli mirati passati, senza creare partite (final-comparison-guard-check.json). Ricalcolato audit Opale:36/36 e1944mosse valide. L'audit Ametista e la verifica conclusiva del congelamento restano pendenti.

## 16:37:32 - Variazione del carico dichiarata da AGENTE-A
Messaggio comune16:34:27: chiusi servizi9017/9026 dopo le sue quattro prove e liberato logico2. E una dichiarazione del collega, non una ricostruzione indipendente dell'uso CPU; resta il limite del carico variabile nella nostra conferma. Nessun cambio ai servizi o al candidato Ametista.

## 16:39:29 - Ulteriore variazione del carico condiviso
AGENTE-B alle16:38:23 dichiara chiusura di tre copie Kingsrow e tre propri servizi, con passaggio a una prova3thread distinta. Non avvia la seconda conferma per risultato di sviluppo inferiore. La nostra misura1thread prosegue invariata; la dichiarazione di carico non consente attribuzioni causali sui singoli esiti.

## 17:02:59 - Conferma conclusa e verifiche finali del candidato v14
36Ametista:3V4P29S,4205mosse;36Opale:0V0P36S,1944mosse. Audit legali/completi,18coppie,nessuna interruzione. Report final-comparison.json:winrate8,33%,score13,89%; vantaggio osservato ma criterio conservativo non superato,nessuna superiorita Kingsrow.
Controllo final-integrity-audit.json:26file e snapshot coerenti,7originali Opale immutati,72registrazioni corrette,3testweb interrotti senza vincitore,memoria registrata entro limite. Prima verifica manuale fermata da confronto JSON troppo letterale; risolto normalizzando solo UTC equivalente e default documentati,poi confronto completo. Nessun dato cambiato per farlo passare.
Il revisore di metodo conferma documenti e conclusioni. README,RESULTS e racconto esterno aggiornati. Questo checkpoint resta intatto; eventuale ulteriore ricerca entro scadenza deve essere dichiarata separatamente e non sostituire questa conferma.

## 17:10:30 - Variante tattica supplementare separata
Su proposta del revisore di metodo, aggiunta guardia contro riduzioni LMR delle mosse che creano catture obbligatorie nel figlio. Codice in experiments/Ametista.ContactSearch,originali e prima conferma intatti,stessi pesi manual4. Program locale aggiunge solo ContactGuards alla telemetria.
Publish finale e verifica completati:9015posizioni/40898transizioni,36protezioni nelle5fixture minimax,zero partite. Piano supplemental-plan.json riserva nuoveaperture,selection e freeze precedono il girone16test. Rischi: meno profondita e stato di riscaldamento runtime non uniforme fra servizi; nessuna prova di forza dai soli test statici. Scelta manuale solo dopo calendario completo.

## 17:15:13 - Carico concorrente nel supplemento
AGENTE-B alle17:13:12 dichiara conclusione del lavoro e logici4/6/9/10 liberi,con a7 ripristinata1thread. Registrato durante prima coppia v17, dopo prima coppia v14: il confronto temporizzato non isola questo cambiamento. Prima coppia v14 auditata0V0P2S/229mosse.

## 17:38:24 - Scelta manuale e seconda conferma separata
Confronto16test completato: v14 1V0P7S(score12,5%),v17 1V1P6S(score18,75%). Una patta aggiuntiva soddisfa il criterio dichiarato di selezione esplorativa,non prova generale. contact-manual-selection.json conserva ragione e limiti.
Seconda conferma72partite secondo supplemental-confirmation-protocol.md,18aperture nuove riservate,entrambeversioni/entrambicolori,logici0e4con medesimecondizioni perciascunseme. Freeze79file,comandi/PID/affinita/health,memoria preavvio653MiB;statistiche con14controlli sintetici,zero partite create dai controlli. Prima conferma e originali invariati.

## 18:05:15 - Estensione dei vincoli confermata direttamente
L'utente conferma qui l'autorizzazione a training e dataset pubblici dopo la comunicazione di AGENTE-B. Recepita per nuovi esperimenti,senza chiedere altre conferme. authorization-extension.json conserva messaggio,ambito e scadenza invariata. Le conferme v14/v17 rimangono a pesi manuali congelati;vietato mescolare retroattivamente training o nuovefonti nei risultati. Divieto di copiareKingsrow invariato;nessuna autorizzazione implicita al fine-tuning di pesi preaddestrati esterni.

## 18:11:04 - Conservazione del runner e limite di provenienza
Archiviati in frozen-runner-v6 i cinque file gestiti effettivi, senza Kingsrow, con hash verificati. Il sorgente attuale del runner e successivo al binario congelato: non e stato trovato lo snapshot esatto del sorgente v6 e non si attesta una ricompilazione equivalente. Hash delle dipendenze catturati ora, non prova dei moduli caricati storicamente. Nessun cambiamento ai motori o alle partite in corso.

## 18:20:56 - Dati umani e carico concorrente dichiarato
Preparato importatore originale:333/343partite FID accettate,20931posizioni;12fixture. Preparato learner tanh128ingressi da zero,100epoche prefissate;controlli gradiente e prospettiva validi,revisione indipendente. Corretta cattura iniziale degli hash e controllo immutabilita;nessun training ancora. Rapporti e codice pubblicabili,corpus non redistribuito.
AGENTE-B dichiara training per errore sul logico4 dalle18:07 alla chiusura riportata18:20,circa2sCPU secondo il suo resoconto. Registrato in supplemental-concurrent-load-disclosure.json;nessun effetto causale misurato e nessuna ripetizione selettiva. Risorse future confermate6/9/10 per B. Memoria libera osservata circa1,37GiB dopo comunicazione,non misura retrospettiva.

## 18:38:21 - Conferma supplementare conclusa
Tutte le 72 partite sono complete e auditate: v14 1V/7P/28S, 4.569 semimosse; v17 0V/6P/30S, 4.653 semimosse. Score 12,5% contro 8,33%; differenza appaiata -4,17 punti percentuali, intervalli t [-12,96;+4,63] e Hoeffding [-68,19;+59,85]. Nessun miglioramento o superiorità su Kingsrow dimostrato; v17 non promossa, v14 e prima conferma invariati.
supplemental-final-integrity.json verifica 86 file, 72 registrazioni supplementari, 72 della prima conferma, input e sette originali Opale. Picco 192.933.888 byte sotto 1 GiB. Fermati dopo i controlli soltanto i servizi supplementari inattivi 9041/9042/9043 e 9018; logico 4 liberato e comunicato sul canale comune.

## 18:48:24 - Conferma completata e neurone umano separato
Seconda conferma terminata18:37:35: v14 1V/7P/28S, v17 0V/6P/30S. V17 non adottata. Integrit?86file,144registrazioni concluse e4interruzioni preservate; revisione indipendente coerente. Servizi aggiuntivi su4 e v17 su0 fermati dopo audit, solo processi propri verificati.
Training nuovo da zero,100epoche prefissate; esecuzione offline cronometrata1,62s. MSEvalidazione0,33159 contro0,33330;test2005 0,43906 contro0,44216. Il filtro dichiarato passa, senza prova di forza. Copia originale della nostra ricerca v14, contributo statico round(100*predizione), rischio/manual4 invariati; ipotesi euristica suggerita dalla revisione metodologica, non residuo appreso.
34fixture combinazione,512confronti numerici,9015posizioni/40898transizioni e32minimax passati. Congelati22file prima delle6partite v18 su9019/core0; hash neurone caec486b6f36fd08b66b7e7d05ed015348198dcdd806df0a7bf24663404f64e2.
Dopo le prime due sconfitte, dichiarata estensione esplorativa con6controlli v14 sulle stesse aperture, solo dopo audit v18 e se avviabili prima19:00; termine controllo19:20. Non parte del piano iniziale, nessun ritocco ai pesi.

## 18:51 - Esito v18 e avvio del controllo dichiarato
Sei partite v18 complete e auditate: 1V/1P/4S, score 25%, 712 semimosse, zero interruzioni. Sei ID unici, tre aperture nei due colori, replay legali e registro condiviso validi. Ricontrollati 22 file congelati e 22 snapshot: nessuna differenza. Fermato soltanto il servizio v18 9019 dopo verifica di PID e comando; Kingsrow 9015 e v14 9016 invariati.
Avviati una sola volta i sei controlli v14 previsti da human-control-extension.json, origine test, stesse aperture/colori, core0 e 1000ms, con audit automatico e nessun retry. Estensione decisa dopo due risultati parziali e non preregistrata: il confronto resterà descrittivo. Nessuna promozione o modifica di pesi, codice e scala.

## 19:00 - Controllo v14 completo e decisione sull'esperimento umano
Sei controlli v14 conclusi e auditi: 1V/3P/2S, score 41,67%, 668 semimosse, zero interruzioni. Tutti i sei record condivisi sono finished/source=test; sei ID unici e tre coppie colore complete. Insieme alle sei v18, il rapporto appaiato verifica 12 replay e 12 record: differenza v18-v14 -16,67 punti percentuali, descrittivamente a favore di v14.
Ricontrollati dopo entrambe le serie 22 file e 22 snapshot congelati, nessuna differenza. Tre aperture, controllo scelto dopo risultati parziali e ordine temporale successivo non consentono una conclusione causale generale; nessun intervallo appaiato sotto 18 coppie. V18 non promossa, v14 resta il candidato principale. Nessuna nuova partita o ulteriore regolazione.

## 19:14:26 - Confronto finale, costo d'inferenza e revisione documentale
Controllo v14 completo: 1V/3P/2S contro v18 1V/1P/4S, sei partite per versione sulle stesse tre aperture. Estensione esplorativa dichiarata dopo due risultati parziali; nessuna promozione. Due analisi concordano e conservano i dodici ID.
La seconda esecuzione con nome AGENTE-C ha dichiarato arresto9019 dopo audit e avvio del controllo; il tentativo duplicato di questa esecuzione è stato respinto prima di creare partite. La risoluzione conserva attribuzione dichiarata, osservazioni indipendenti e backup dei byte del canale prima della correzione di codifica eseguita dall'altra esecuzione. Il testo precedente, normalizzando codifica e terminatori, è preservato come prefisso.
Diagnostico sulle DLL effettive, dopo le partite: 1.000 controlli numerici passati, sette round alternati; mediana 834,534 ns manuale e1.107,583 ns combinata, rapporto1,327. Misura sintetica, non forza né causa dei risultati. Primo build respinto dal controllo di piattaforma; aggiunta guardia Windows solo al diagnostico, build finale senza errori o avvisi. Nessun motore ricompilato da questa prova.
Inventario:309 partite uniche,305 concluse e4 interrotte;35.353 semimosse registrate, con aperture e prova web incluse. Dati umani storici e fixture fuori classifica. Corrette formulazioni documentali oltre l'evidenza e completato il racconto tecnico esterno.

## Riepilogo finale

- Opale C preservato: sette file originali invariati.
- Ametista v14/manual-v4 mantenuta; v17 e v18 non adottate e conservate.
- Sessione: 309 avvii registrati, 305 conclusi e quattro interrotti senza punti.
- Training da zero, verifiche e risultati negativi documentati, senza copiare Kingsrow.
- Racconto esterno e materiale tecnico aggiornati; nessuna superiorità su Kingsrow dimostrata.
