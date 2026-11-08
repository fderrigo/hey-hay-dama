# Ametista: sviluppo e conferma

Aggiornamento del 7 novembre 2026, dopo le due conferme e l'esperimento con dati umani.
Nella prima conferma il candidato **search-v14 / manual-v4** ottiene **3 vittorie, 4 patte e
29 sconfitte su 36 partite**. Kingsrow rimane nettamente avanti.

Ogni riga riporta un girone concluso, con replay verificato e partite nel
registro condiviso. V/P/S sono dal punto di vista di Ametista. Lo score
normalizzato è `(2V + P) / (2N)`, coerente con i punti della classifica.

| Girone | Partite | V | P | S | Score | Mosse verificate |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| [v1](development-v1/summary.json) | 4 | 0 | 2 | 2 | 25% | 487 |
| [search-v2](development-search-v2/summary.json) | 8 | 1 | 1 | 6 | 18,75% | 893 |
| [search-v3](development-search-v3/summary.json) | 8 | 0 | 2 | 6 | 12,5% | 1070 |
| [search-v4](development-search-v4/summary.json) | 8 | 1 | 2 | 5 | 25% | 1032 |
| [search-v6](development-search-v6/summary.json) | 8 | 0 | 2 | 6 | 12,5% | 920 |
| [search-v8](development-search-v8/summary.json) | 8 | 0 | 2 | 6 | 12,5% | 1012 |
| [search-v9](development-search-v9/summary.json) | 8 | 0 | 0 | 8 | 0% | 952 |
| [search-v10, manual-v2](development-search-v10/summary.json) | 8 | 0 | 2 | 6 | 12,5% | 1170 |
| [search-v10, manual-v3](development-manual-v3-search-v10/summary.json) | 8 | 1 | 1 | 6 | 18,75% | 973 |

La qualificazione v4/manual-v2 contro v13/manual-v3 è conclusa, con
[scelta e calendario registrati prima delle partite](qualification-selection.json).
Le revisioni v5, v7, v11 e v12 hanno avuto verifiche
statiche, non gironi autonomi conclusi. V7 e v12 non sono state adottate.

## Come leggere questi numeri

Sono prove di sviluppo, origine `test`, sulle stesse quattro aperture a
colori invertiti (solo due nella prima prova). Non sono campioni indipendenti
da sommare per sostenere una maggiore forza. V1 aveva anche condizioni
operative diverse, documentate nel diario. Cambiamenti del carico concorrente
limitano l'interpretazione causale dei confronti a tempo.

Il miglior punteggio osservato fra i gironi da otto è quello di v4; aver
selezionato il massimo fra più tentativi non prova che sia il migliore.
La [qualificazione su aperture nuove](qualification-plan.json) ha dato
search-v4/manual-v2: **0 V, 0 P, 8 S**; search-v13/manual-v3: **1 V, 0 P, 7 S**.
Le 16 partite hanno audit completo e aperture identiche dentro ciascun
confronto; il [resoconto per coppia](qualification-report.json) mostra che
la differenza è concentrata nella quarta apertura. Non basta a dimostrare
superiorità generale. La v13 resta il riferimento di sviluppo successivo.

È concluso il [confronto separato](pressure-comparison-selection.json) fra
manual-v3 e manual-v4 sullo stesso eseguibile search-v14, otto partite per
file, con ordine alternato per apertura. Riutilizza le quattro aperture
iniziali di sviluppo; non è una conferma indipendente.

Manual-v3: **2 V, 0 P, 6 S**; manual-v4: **2 V, 1 P, 5 S**.
Il win rate resta 25% per entrambe; lo score passa dal 25% al 31,25%.
Le 16 partite e 1995 mosse hanno audit completo. Una patta aggiuntiva non
dimostra superiorità generale. Manual-v4 è stata scelta a mano per il
[confronto di ricerca](search-comparison-selection.json): search-v14 contro
search-v16 su otto nuove aperture, 16 partite per versione.

Il confronto è concluso: **v14: 1 V, 2 P, 13 S; v16: 1 V, 0 P, 15 S**.
Entrambe hanno win rate 6,25%; lo score è rispettivamente 12,5% e 6,25%.
La scelta manuale mantiene v14: stesso numero di vittorie e due patte in più.
Entrambe vincono soltanto sull'ultima apertura, con due pedine in più per
il Nero dopo il prefisso casuale. Nessuna superiorità generale dimostrata.

Un timeout HTTP di 35 secondi durante il turno
di Kingsrow ha interrotto la partita `497fffcd785a4ed58ec869bdd3b90c71` al
ply 148, senza assegnare punti. L'[audit originale](search-comparison-v16-seed375105/audit.json)
conserva una sconfitta completata e una partita interrotta. Il
[recupero dichiarato](search-comparison-recovery-selection.json) ripete
entrambi i colori con nuovi ID: 34 avvii previsti per 32 risultati appaiati,
con i due avvii originali riportati separatamente. Nessun dato cancellato;
causa tecnica non identificata. Il [carico condiviso](search-comparison-load-change.json)
è cambiato durante la misura, ma non è stato dimostrato che abbia causato
il timeout. L'ultimo [resoconto del recupero](search-comparison-recovery-report.json)
deve essere letto insieme all'originale interrotto.

Il [candidato congelato](final-freeze.json) è stato verificato anche tramite
il web: [tre tentativi documentati](final-web-integration-summary.json),
due risposte HTTP 502 (timeout documentato nel secondo caso) e una mossa
legale dopo l'aggiunta di 500 ms di tolleranza
di trasporto. Il budget inviato al modello non cambia. Tutti e tre i test
API sono conservati come interrotti senza punti; non sono partite concluse
contro Kingsrow e non entrano nelle percentuali sopra riportate.

Le [18 aperture riservate alla conferma](confirmation-plan.json)
non sono state usate per modificare o scegliere il motore. Il
[piano finale](confirmation-schedule.md) include un controllo Opale a budget
uguale e il congelamento del candidato prima di vedere quei risultati.

Il [controllo finale Opale C manual-v3](confirmation-opale/audit.json) è
concluso: **0 vittorie, 0 patte, 36 sconfitte**, 18 aperture appaiate e
1.944 mosse verificate, senza interruzioni. La conferma Ametista usa le
stesse aperture e il candidato congelato prima del controllo. È conclusa:
**3 vittorie, 4 patte e 29 sconfitte**, 4.205 mosse verificate e nessuna
interruzione. Entrambi gli audit coprono tutte le 18 coppie previste.

Il [confronto finale](final-comparison.json) riporta vittorie **8,33%** e
punteggio normalizzato **13,89%**, contro lo 0% di Opale. È un vantaggio
osservato nel campione. La differenza di punteggio ha intervallo t
approssimato al 95% `[4,14; 23,63]` punti percentuali e limite conservativo
Hoeffding `[-50,13; 77,91]`: il criterio prestabilito, che richiede entrambi
i limiti inferiori positivi, **non dimostra un miglioramento generalizzabile**.
Nessuna superiorità su Kingsrow è dimostrata.

Le [tre vittorie](confirmation-opening-results.json) sono tutte col Bianco:
due aperture avevano 12 pedine bianche contro 11 nere; una aveva 11 contro
11. Parità di materiale non significa equilibrio teorico. Sono partite
da aperture dopo sei semimosse, non dalla posizione iniziale standard.

Il [controllo conclusivo di integrità](final-integrity-audit.json) verifica
26 file, le copie congelate, gli hash degli input del rapporto e tutte le
72 registrazioni finali. I sette file originali di Opale sono immutati.
I tre test API restano interrotti senza vincitore; i picchi di memoria
registrati nelle partite rispettano il limite dichiarato. Il
[riesame del metodo](final-analysis-review.md) distingue questi controlli
dalla certificazione del tempo CPU, che non è disponibile.

Il [controllo di integrità durante la conferma](confirmation-integrity-midrun.json)
conferma la corrispondenza di 26 file agli hash registrati, inclusi i sette
file originali di Opale. Non sostituisce il controllo conclusivo. Il
[riscontro di AGENTE-B sulle risorse](confirmation-colleague-load-update.json)
documenta la sua dichiarazione delle 16:22:24: tabelle ridotte prima delle
16:06 e due processi di ottimizzazione senza affinità fermati verso le 16:22.
Non è una misura indipendente dello stato storico dei processi: il carico
condiviso è cambiato durante la conferma e resta un limite del confronto.

Per ogni girone consultare `manifest.json` per budget, pesi, avversario e
risorse; `games.jsonl` per ogni posizione e mossa; `audit.json` per legalità,
risultato e corrispondenza con il registro. Le interruzioni non sono patte.

## Velocità e forza sono misure diverse

La [rete compilata v11](profile-compiled-comparison.json) conserva mosse,
valori e nodi su 32 fixture a profondità fissa e riduce il tempo CPU del
9–12% nei due ordini misurati. Non è una misura della forza in partita.
L'[anticipo della lettura della cache v12](profile-early-tt-comparison.json)
produce tempi discordanti fra i due ordini; resta archiviato, non adottato.
Le fixture non sono partite e non contribuiscono alla classifica.

Il [changelog](changelog.md) documenta modifiche, motivazioni e idee ricevute
dagli altri agenti. Il racconto editoriale resta nella cartella esterna al
repository; questi dati tecnici appartengono all'esperimento pubblico.

## Tentativo supplementare separato

Dopo la prima conferma, il [piano supplementare](supplemental-plan.json)
introduce una sola ipotesi tattica: evitare riduzioni LMR quando una mossa
tranquilla crea una cattura obbligatoria nel figlio. Il rischio della
posizione precedente non descrive necessariamente il nuovo contatto.
La variante `search-v17-contactguard` conserva gli stessi pesi manual-v4,
è separata in `experiments/Ametista.ContactSearch` e non sostituisce v14.

La [verifica del pacchetto](contact-package-verification.json) passa gli
stessi controlli e osserva 36 protezioni nelle fixture confrontate con
minimax. Il [confronto di sviluppo](contact-comparison-selection.json)
prevede quattro nuove aperture nei due colori per ciascuna versione:
16 partite totali, origine `test`, calendario e codice congelati prima
dell'avvio. La scelta manuale richiede un punteggio strettamente superiore
sul calendario completo. Il [risultato completo](contact-comparison-report.json)
è **v14 1V/0P/7S, v17 1V/1P/6S**, otto partite per versione: rispettivamente
12,5% e 18,75% del punteggio. La [scelta manuale](contact-manual-selection.json)
ammette v17 alla verifica successiva per la patta aggiuntiva, senza inferire
un miglioramento generale da quattro coppie di aperture.

L'attivazione della protezione non prova maggiore forza: può ridurre la
profondità completata. I servizi partono inoltre da stati di riscaldamento
diversi e il carico concorrente cambia durante la misura, come dichiarato
nella discussione. La prima conferma di 36 partite resta integralmente
conservata, indipendentemente dall'esito di questo tentativo.

La [seconda conferma](supplemental-confirmation-selection.json) è conclusa:
36 partite per versione sulle stesse 18 nuove aperture riservate, dopo il
[congelamento](supplemental-confirmation-freeze.json). Il
[protocollo dichiarato](supplemental-confirmation-protocol.md) ha ripartito
le aperture fra i processori logici 0 e 4; entrambe le versioni hanno giocato
ogni singola apertura sullo stesso processore e contro la stessa istanza
Kingsrow, nei due colori, con un solo thread di ricerca per motore.

Il [rapporto supplementare](supplemental-confirmation-report.json) copre tutte
le 72 partite auditate: **v14 1V/7P/28S (score 12,5%)**, **v17 0V/6P/30S
(score 8,33%)**. La differenza appaiata v17 meno v14 è **-4,17 punti
percentuali**, con intervallo t approssimato `[-12,96; +4,63]` e limite
Hoeffding `[-68,19; +59,85]`. Il criterio di miglioramento non è superato e
nessuna versione dimostra superiorità su Kingsrow. V17 non viene promossa;
v14 resta il candidato principale e la prima conferma resta invariata.

Il [controllo conclusivo](supplemental-final-integrity.json) verifica 86 file,
le 72 registrazioni supplementari completate, gli input dei due rapporti e i
sette file originali di Opale. Il picco registrato è 192.933.888 byte, sotto
il limite di 1 GiB. AGENTE-B ha dichiarato una sovrapposizione sul logico 4
fra le 18:07 e le 18:20, circa 2 secondi CPU, documentata in
`supplemental-concurrent-load-disclosure.json`; non se ne deduce un effetto
causale e nessuna partita è stata ripetuta selettivamente.

### Provenienza del runner

Il [pacchetto gestito v6](frozen-runner-v6/README.md) conserva il runner usato, con hash e dipendenze. Il sorgente corrente del runner postdata il binario; non e stato trovato uno snapshot esatto del sorgente v6. La conservazione del binario non dimostra una ricompilazione equivalente. Gli snapshot e gli hash dei motori sono documentati separatamente.

### Sovrapposizione di carico dichiarata nella seconda conferma

AGENTE-B ha segnalato un training erroneamente sul logico4 fra le18:07 e la chiusura riportata18:20, con circa2secondiCPU secondo il suo resoconto. Il [registro della segnalazione](supplemental-concurrent-load-disclosure.json) distingue questi dati dichiarati da misure indipendenti. Il confronto conserva tutte le partite; nessun risultato viene escluso o ripetuto selettivamente. Non e stato isolato un effetto causale sulle prestazioni.

La [descrizione delle durate](supplemental-timing-description.json) raggruppa le partite rispetto alla finestra di carico dichiarata. Le durate sono tempi osservati dal runner, non CPU isolata: non dimostrano assenza di contesa. La sola vittoria v14 della seconda conferma, seme570316 con il Bianco, parte da12pedine contro11 dopo le6semimosse casuali.

### Esperimento successivo con dati umani

Dopo l'autorizzazione esplicita a training e dataset pubblici, un [esperimento separato](human-data/README.md) usa archivi umani FID 2004/2005, senza caricare pesi precedenti nel learner. La previsione degli esiti migliora di poco rispetto alla costante: MSE 0,3315905 contro 0,3332987 in validazione e 0,4390621 contro 0,4421629 sul test temporale 2005. Questo filtro offline non misura da solo la forza di gioco.

La combinazione v18, con manual-v4 invariato più `round(100 * predizione)`, ha completato sei prove esplorative dopo verifiche tecniche e [congelamento](human-exploratory-freeze.json): **1V/1P/4S**, score 25%, 712 semimosse e zero interruzioni. L'[audit](human-exploratory-v18/audit.json) conferma calendario completo, replay legali, tre coppie colore e registrazione condivisa; i 22 file congelati restano invariati. V18 non è promossa e le conferme precedenti restano immutate.

Un'[estensione](human-control-extension.json), decisa dopo le prime due sconfitte v18, ha aggiunto sei controlli v14 sulle stesse aperture e colori. Il controllo è concluso e [auditato](human-control-v14/audit.json): **1V/3P/2S**, score 41,67%, 668 semimosse e zero interruzioni. Il [rapporto appaiato](human-exploratory-paired-report.json) verifica tutti i 12 replay e record condivisi `finished`. La differenza v18 meno v14 è **-16,67 punti percentuali**: il risultato descrittivo favorisce v14 e non mostra un miglioramento del contributo appreso.

Non è una conferma preregistrata: il controllo è stato scelto dopo risultati parziali e segue il candidato nel tempo. Tre aperture non consentono gli intervalli appaiati previsti dal runner; nessuna conclusione causale generale. V18 non è promossa, v14 resta il candidato principale e tutte le conferme precedenti restano immutate.

### Costo dell'inferenza e inventario finale

Il [diagnostico sulle DLL effettive](human-data/inference-cost.json), eseguito dopo le partite, verifica su 1.000 posizioni sintetiche la combinazione numerica e il rischio invariato. Con riscaldamento e sette round a ordine alternato, la mediana passa da 834,534 ns a 1.107,583 ns per valutazione: circa il 33% in più. Non comprende ricerca, cache o HTTP; non è una spiegazione causale delle sconfitte.

L'[inventario finale](final-session-match-inventory.json) contiene 309 identificativi unici: 144 partite `benchmark` concluse, 161 partite `test` concluse e quattro `test` interrotti senza punti. Le 35.353 semimosse registrate includono le aperture casuali e la prova web di una mossa. Sono conteggi di lavoro attraverso più versioni, non il tasso di vittoria di un singolo motore. Replay storici umani e fixture sintetiche sono esclusi dalla classifica.

La [nota di coordinamento](human-control-coordination-resolution.json) conserva l'attribuzione dell'arresto del servizio sperimentale e del controllo a un'altra esecuzione con lo stesso nome operativo. Il tentativo duplicato è stato respinto prima della creazione di partite. I due [calcoli](human-exploratory-paired-report.json) [appaiati](human-comparison-report.json) concordano sui dodici esiti; nessun risultato è stato sovrascritto per scegliere una versione migliore.
