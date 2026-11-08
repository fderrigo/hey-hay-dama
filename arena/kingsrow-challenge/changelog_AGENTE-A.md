# Changelog di AGENTE-A — Diaspro

## 2026-11-07 11:36 locali

- Conservati Klinamite v3 e Kingsrow; hash iniziali acquisiti. Nessun file decisionale di quei motori modificato.
- Concordanza sul canale comune: scadenza 18:28:21 UTC, confronto principale a 1000 ms effettivi, un thread, 1 GiB, nessun ponder, aperture appaiate e conferma congelata.
- Creata Diaspro v1 come progetto isolato: 192 ingressi relazionali di casa e 17 caratteristiche globali, due propagazioni sul grafo, coefficienti manuali e memoria episodica per partita.
- Registrato ID `diaspro`, endpoint previsto 9016. Nessuna partita avviata.
- Prima compilazione del motore superata senza avvisi. Il primo tentativo ha rilevato un avviso nullable trasformato in errore; corretto prima della build riuscita.

## 2026-11-07 12:03 locali

- v1 diagnosticata su 18 partite: 0V/0P/18S, 709 mosse, audit completo. Il difetto tecnico era la cancellazione delle feature relative al tratto tramite antisimetrizzazione.
- v2: simmetria corretta con media dei canali colore-scambiati; avanzamento calcolato dal colore del pezzo. Nessun peso regolato automaticamente.
- v2 sullo stesso seme di sviluppo 31001: 0V/0P/18S, 969 mosse, audit completo; il difetto di presa immediata scende da 101/296 a 21/426 decisioni.

## 2026-11-07 12:12 locali

- Diagnostica offline estesa al saldo presa/ripresa con promozioni. Su v2: 19 scambi perdenti evitabili su 426 decisioni.
- Preparata v3 con feature geometriche locali di esposizione senza ripresa e continuazione di cattura; nessuna espansione dell'albero nel motore.

## 2026-11-07 12:20 locali

- v3: coefficienti manuali 36 per esposizione senza ripresa e 56 per continuazione di cattura; 15 test superati. Checkpoint SHA256 8d633451cdcf224ba717652ca49a11bce85419018bb427f2774ae2a804250c3c.
- Memoria episodica limitata a 64 partite; affinità dei due motori al processore logico 2. Fixture HTTP v3 valide, nessuna partita di fixture registrata.
- Avviato girone di sviluppo v3 sul seme 31001. Ripristinata la posizione di Kingsrow nel selettore su indicazione di AGENTE-C; Diaspro resta in coda.

## 2026-11-07 12:27 locali

- v3 audit: 0V/0P/18S, 985 mosse, 27 rischi e 25 scambi evitabili su 434 decisioni. Picchi: Diaspro 71,31 MiB, Kingsrow 176,46 MiB.
- v4: correzione dimensionale manuale, deterrente pedina indifesa 127,36 > 112 e dama 382,08 > 336. Nessuna nuova feature e nessun ciclo esiti-pesi.
- Checkpoint v4 SHA256 9d4738f6d7a731c4d7556facaa7ab7a26f0cfb56091a7223880000a44407e3b6; 16 test e fixture HTTP superati. Girone 31001 avviato.
- Audit esteso a hash del checkpoint, dispersione per coppia e intervallo Wilson per non riportare certezza nulla con score zero.

## 2026-11-07 12:32 locali

- v4 audit: 0V/0P/18S, 1003 mosse; 25 rischi e 18 scambi evitabili su 443 decisioni. Il difetto di scambio cala rispetto a v3, ma non produce punti.
- Decisione motivata dai replay: superato il vincolo autoimposto senza albero. v5 manterrà i coefficienti v4 e aggiungerà ricerca originale, senza trasferire codice o risposte da altri motori.

## 2026-11-07 12:35 locali

- v5: nuovo `Search.cs`, negamax alpha-beta con approfondimento iterativo, deadline interna, fallback all'ultima profondità completa, estensione massima quattro prese e patte per ripetizione sul percorso.
- Pesi e feature restano quelli manuali v4; cambia soltanto il nome checkpoint a diaspro-v5. SHA256 66dc1ffa12894965059c8b1bdec04e67b9290ac02b773c21faf830a572b19f00.
- 19 test e fixture HTTP superati. Aggiornate descrizioni che ancora dichiaravano assenza di ricerca. Avviate quattro partite di sviluppo prima di un girone esteso.

## 2026-11-07 12:46 locali

- v5: quattro partite auditate, 0V/0P/4S, 256 mosse. Profilo iniziale: profondità 4, 3.008 nodi/s.
- v6: eliminata doppia generazione delle mosse in Evaluate; mobilità sicura calcolata da quattro relazioni locali; unite le tre scansioni di esposizione. Snapshot di 200 posizioni byte-identico alla v5.
- Profilo dopo ottimizzazione: profondità 5. Una tabella di trasposizione con chiavi stringa è stata implementata e scartata perché riduceva il profilo a 3.392 nodi; nessuna partita giocata con quella variante.
- v6 checkpoint SHA256 2bd72e49e723f42bd2392c79510d2355ca8c642b7901e5187867cb269e6c0168; 19 test e fixture HTTP superati. Telemetria di profondità/nodi/tempo attiva.

## 2026-11-07 12:56 locali

- v6: quattro partite Release sul seme 31001, 0V/0P/4S e 246 mosse. Audit completo: replay, registri, aperture appaiate, colori e hash corretti; intervallo Wilson dello score [0;0,4899].
- v7 su consiglio di AGENTE-C: la ricerca copia i conteggi reali di PositionMemory e include repetitionCount. Corretto anche l'ingresso nella posizione figlia, non in quella genitrice, prima della valutazione arbitrale.
- Aggiunti minimax esaurente senza tagli a profondità 4 su tre posizioni e fixture di arrivo alla quarta ripetizione. Il primo test della ripetizione ha evidenziato la penalità di radice dopo la patta; riferimento corretto, non soglia forzata.
- 21 test superati in Release. Checkpoint v7 SHA256 116ca60b1c40d7c9a3571a930cd4569a9d04e9266f1203c8e21554b9f7cdd626; coefficienti numerici invariati dalla v4. Servizio 9017 aggiornato, PID 18796 con affinità 4.

## 2026-11-07 13:03 locali

- v7 audit: 0V/0P/4S, 322 mosse, nessuna interruzione e nessun punto. La correzione delle ripetizioni è necessaria ma non migliora da sola il risultato.
- v8: le foglie riusano le mosse legali già generate dal negamax; estensioni di cattura fino a 24 invece di quattro, limite non raggiungibile prima dell'esaurimento del materiale. Nessun coefficiente cambiato.
- 21 test Release superati; snapshot statico di 200 posizioni byte-identico a v6. Profilo iniziale v7/v8: stessa mossa e profondità 6; v8 completa più nodi nello stesso secondo, misura non isolata da altro carico dichiarato sul canale.
- Checkpoint v8 SHA256 d5b90183e4f9015227aaa022dbb0503cff64fd60d632487d43e8016fadf41d35. Servizio 9017 PID 23220, affinità 4; fixture HTTP e quattro partite di sviluppo sul seme 31001.

## 2026-11-07 13:16 locali

- v8 audit: 0V/0P/4S, 334 mosse. La maggiore durata non è trattata come miglioramento di forza.
- v9: history heuristic sui soli tagli quieti e punteggi di radice dell'iterazione precedente. 21 test e audit del girone: 0V/0P/4S, 280 mosse. Profili rumorosi, nessuno speedup rivendicato.
- Diagnosi della sconfitta v9 `42d10e3a`: a ply 70 entrambe le dame candidate vengono catturate; minimax a profondità 8 conferma che la posizione era già perduta. Non è stato corretto un errore inesistente sull'ultima mossa.
- Variante con cache statica per posizione e contatore di mosse quiete: valori minimax invariati, prestazioni non stabilmente migliori; non promossa da sola.
- v10: cache statica locale alla ricerca e continuazione migliore usata solo per ordinare i nodi fra iterazioni, mai per riusare valori in un altro contesto di ripetizioni. 21 test superati.
- Checkpoint v10 SHA256 b3ce85a231d690b06f129e56213089250af0e93737fec077acfb199bade1fe53. Servizio PID 20780, affinità 4; fixture HTTP valide e quattro partite di sviluppo avviate.

## 2026-11-07 13:23 locali

- v10 audit: 0V/0P/4S, 336 mosse. Ordinamento e cache non hanno cambiato lo score.
- v11: corretta la prospettiva del grafo. Poiché i canali usano pezzo*tratto, la rotazione con segno e tratto invertiti non scambia propri e avversari; eliminata la mappa incrociata e la seconda propagazione ridondante.
- 21 test superati, inclusa la rotazione su 250 posizioni. Profilo iniziale: v10 due volte profondità 6; v11 profondità 6 e poi 7, con mossa diversa. Indicazione preliminare, non prova di forza.
- Checkpoint v11 SHA256 13cecdd1224277ba4dadf243c6d12cb1b30b4756ca58c50d42204c13c288308a. Servizio PID 38592, affinità 4, fixture HTTP valide; quattro partite sul seme 31001 in corso.

## 2026-11-07 13:35 locali

- v11 audit: 0V/0P/4S, 208 mosse e crolli materiali precoci. La correzione geometrica non viene promossa; v12 ripristina la valutazione v10.
- Nuovo generatore FastRules: 1.000 posizioni casuali con tutte le transizioni e 40 percorsi deterministici identici all'arbitro. Integrato nel motore; nessun vantaggio stabile nel solo profilo iniziale.
- v12: principal variation search con finestra nulla e nuova ricerca completa al fallimento; 20 posizioni casuali ramificate concordano con minimax, oltre alle tre fixture precedenti. 24 test totali.
- Riferimenti al checkpoint unificati tramite ManualWeights.Version; aggiunto riferimento del runner al progetto dopo che la pubblicazione aveva rilevato la dipendenza mancante.
- v12: 0V/1P/3S, 336 mosse, audit completo. Prima patta a ply 121; score 0,125 con intervallo Wilson [0,0132;0,6042]. Checkpoint SHA256 d219a7783393847790a8fd2335535e0858603d4d9fe6789b1d708db3ec764845.
- Motore congelato per 18 partite di sviluppo sul seme 31001. Runner esteso con hash dei sorgenti decisionali e del runner stesso; nessuna modifica al motore durante il girone.


## 2026-11-07 13:51 locali

- Girone esteso v12 completato e auditato: 0V/0P/18S, 1.431 mosse, nove aperture appaiate e nessuna interruzione. Score 0; intervallo Wilson [0;0,1758845506].
- Replay, esiti, registri, colori e checkpoint verificati. Questo manifest precede l'estensione del runner, quindi `sourcesVerified:false`; i sorgenti sono stati controllati separatamente e quello della conferma conterrà i loro hash.
- La patta del test iniziale non è replicata. v12 resta il candidato congelato per completezza tecnica e per l'unica patta osservata, non per una presunta superiorità. Prossima conferma: 54 partite `benchmark`, seme nuovo 9202601.

- Conferma avviata alle 12:52:25 UTC: 54 partite `benchmark`, seed 9202601, 27 coppie a colori invertiti. Diaspro v12 PID 29332, affinità 4; health e nove fixture HTTP valide prima dell'avvio. Nessuna compilazione durante il girone.
- Il manifest della conferma include gli hash dei sette sorgenti del motore e dei due sorgenti del runner. Vincoli dichiarati: 1000 ms effettivi, un thread, 1 GiB, nessun ponder e nessun database finale.
- Controllo risorse durante la prima partita: Diaspro 29332 e Kingsrow operativo 48660; picchi iniziali 106,11 e 106,93 MiB. Kingsrow aveva ereditato affinità 4095: impostata manualmente a 4 sulla sola copia 9026 usata da Diaspro. Motore e pesi invariati; la condizione iniziale viene annotata, non nascosta.


## 2026-11-07 14:54 locali — conferma conclusa

- Conferma v12: 0V/1P/53S su 54 partite, 4.799 mosse, 27 aperture appaiate e nessuna interruzione. Patta alla partita 43, ply 193, motivo `forty-moves`.
- Score 0,0092592593; Wilson 95% [0,0009673995;0,0827370624]; margine per coppie [0;0,0274074074]. Nessuna superiorità: Kingsrow vince il confronto in modo netto.
- Audit completo con `sourcesVerified:true`, checkpoint e sorgenti invariati. Picchi finali: Diaspro 117,59 MiB, Kingsrow 106,93 MiB; affinità finale 4 per entrambi.
- 24 test Diaspro Release superati. Build completa `WeightSpaceGames.slnx` in Release: 0 errori e 0 avvisi.
- Totale dei 14 gironi auditati, versioni miste: 176 partite, 0V/2P/174S, 12.214 mosse. Non è una misura di una singola versione.
- Racconto tecnico finale scritto e verificato in `C:/Users/filip/source/repos/hey-hay-dama-editoriale/2026-11-07/DIASPRO_RACCONTO_COMPLETO.md`; Klinamite v3 intatta.

## Riepilogo in cinque righe

Diaspro è un motore relazionale nuovo, con 133 coefficienti manuali, memoria episodica e ricerca PVS; Klinamite è preservata.
Dodici versioni hanno corretto prospettiva, geometria, ripetizioni, generazione e ricerca senza training né trasferimenti da Kingsrow.
La conferma congelata ha prodotto una patta e 53 sconfitte: non ho battuto Kingsrow e non rivendico miglioramenti non dimostrati.
Replay, registri, aperture, checkpoint e sorgenti della conferma sono auditati; test e build completi passano.
Il resoconto editoriale include risultati negativi, risorse, non determinismo e limiti delle prove, senza selezionare soltanto gli esiti favorevoli.


## 2026-11-07 15:33 locali — ripresa senza limite di tempo

- Ripresa v12 senza cancellare la conferma negativa. Diagnosi: ricerca a profondità 5-7, chiavi stringa, nessuna TT e grafo costoso.
- v13 introduce bitboard a 64 bit, hash di posizione e multinsieme storico, TT con limiti esatti/superiori/inferiori e normalizzazione dei terminali. Seguono gli invarianti di contesto suggeriti da AGENTE-C; non sono stati copiati codice o valori.
- Corretto prima delle partite il filtro direzionale delle prese: il salto ha ampiezza due e va controllato il segno, non l'ampiezza uno. Il generatore è confrontato con l'arbitro su 1.000 posizioni, tutte le transizioni e 40 percorsi.
- La valutazione SIMD usa due blocchi Vector128 e conserva la valutazione scalare pubblica come riferimento. Un primo prodotto vettoriale errato è stato scartato prima delle partite; la versione corretta coincide con lo scalare nei 500 casi casuali e nei 28 test Release.
- Ritirata una compilazione sparsa del grafo senza vantaggio stabile. Profilo valido iniziale, due esecuzioni: v12 30.592/28.288 nodi a profondità 7; v13 76.928/80.192 nodi a profondità 8. Non è una prova di forza.
- Checkpoint separato `diaspro-v13.json`, SHA256 `df334cff0657c495d217eacb355ce0404e5ee6e955a13730ea9cf2fda3db4321`; coefficienti numerici invariati. Integrazione nel servizio con health `bitboard-pvs-tt-simd`, hash dei nuovi sorgenti nel runner.


## 2026-11-07 15:42 locali — esito v13 e diagnosi finale

- Girone `v13-prova4`: 0V/0P/4S, 298 mosse. Audit completo: replay e registro validi, aperture/colori appaiati, checkpoint e sorgenti verificati. Nessun punto, quindi nessun miglioramento agonistico rivendicato.
- 29 test Release: aggiunti confronti a profondità 4 con storie distinte e contatori 78/79/80/81. La TT mantiene gli stessi valori della ricerca v12 in questi casi finiti.
- Nel primo replay Diaspro arriva a due dame e una pedina contro una dama e due pedine, poi perde. È un caso concreto di conversione, non la prova che ogni posizione con quel materiale sia vinta.
- Prossima v14: sotto sei pezzi, valutazione statica leggera specifica e ricerca più profonda, attivata esplicitamente dal servizio. I test v13 mantengono il percorso equivalente precedente; nessun risultato aggiorna automaticamente coefficienti.


## 2026-11-07 15:45 locali — v14 finali

- Aggiunta `Endgame.cs`: sotto sei pezzi usa materiale 112/336, avanzamento 5, corsa di promozione 8, centralità 4, avvicinamento delle dame 2 e mobilità quieta 3. Coefficienti scelti manualmente; nessun addestramento o regressione sugli esiti.
- Il percorso v13 resta disponibile e testato con `useEndgameEvaluation:false`; il servizio v14 attiva il nuovo percorso. Il grafo generale resta invariato sopra sei pezzi.
- 32 test Release passano: simmetria colore, blocco immediato, progresso e materiale nel finale oltre alle verifiche precedenti. Checkpoint `diaspro-v14.json` SHA256 `038e93e63047563ddcd484c5c44279a854574dc09221b45c3e19ad092d4458f1`.
- Fixture HTTP valide, Diaspro v14 su 9017 con affinità 4; avvio quattro partite `test` sul seme 31001 per confronto diagnostico con v13, non conferma.


## 2026-11-07 15:51 locali — esito v14 e nuovo apprendimento

- `v14-prova4`: 0V/0P/4S, 350 mosse; audit completo e sorgenti verificati. Il percorso finale non migliora lo score e non viene promosso.
- Nuova fase consentita dalle regole iniziali e dalla ripresa senza limiti: TD(0) sul valore degli stati, usando esclusivamente autopartite Diaspro dalla posizione iniziale. Nessuna supervisione da mosse, valori o pesi di Kingsrow.
- Verrà aggiornata soltanto la testa lineare a 19 coefficienti; grafo, architettura e ricerca restano distinti dagli altri motori. Ogni autopartita sarà registrata e ogni transizione salvata prima di creare un checkpoint v15 separato.


## 2026-11-07 16:08 locali — v15 appresa ma respinta dai test

- `v15-td-fumo2`: 2 autopartite, 225 mosse e 213 aggiornamenti; replay, registro ed esiti verificati.
- `v15-td-100`: 100 autopartite, 11.903 mosse e 11.303 aggiornamenti; stesso audit completo superato. Seme 15101, budget 50 ms, logico 2. Il candidato conserva SHA256 `ae801698f20a10d57055b460f9a62ebcf84a2ea036a64c2a3fb5d77ebf1d42f6`.
- Integrato senza sovrascrivere v13/v14 come `diaspro-v15-td.json`. Il percorso del servizio è tornato alla valutazione v13 usata durante l'apprendimento, senza valutatore finale v14.
- Esito negativo: 31/32 test. `Policy_avoids_an_immediate_man_loss` fallisce: posizione esposta 158,92865 contro sicura 105,84134. Il coefficiente di presa immediata è sceso da 0,92 a 0,67079735; non si modifica il test e non si gioca contro Kingsrow con questo candidato.
- v16 mantiene congelati i coefficienti tattici 9,10,11,12,17,18 di v13 e apprende soltanto gli altri 13. È una salvaguardia decisa dal test interno, non ottimizzazione su Kingsrow. Fumo `v16-td-fumo`: 2 partite, 197 mosse, 185 aggiornamenti, audit valido.
- Avviato `v16-td-safe-100`, seme 16111, 100 autopartite registrate a 50 ms sul logico 2, priorità BelowNormal. Ogni risultato sarà auditato prima dell'integrazione.
- Correzione tecnica durante il run: il PID Bash non corrispondeva al processo Windows e la prima impostazione di affinità è fallita. Individuato per riga di comando il solo learner v16 (PID 3856) e verificati affinità 4 e priorità BelowNormal; nessuna interruzione o duplicazione.
- Audit esteso per rileggere ogni aggiornamento e verificare che i sei coefficienti tattici congelati siano esattamente quelli del checkpoint iniziale v13. Il fumo v16 supera anche questo controllo.
- Build completa della soluzione in Release completata senza errori o avvisi; questo non sostituisce il test fallito del candidato v15.


## 2026-11-07 16:16 locali — v16 integrata e avvio sviluppo

- `v16-td-safe-100`: 100 autopartite, 10.875 mosse e 10.275 aggiornamenti. Audit: registro, replay, esiti e congelamento dei sei coefficienti tattici verificati su tutti gli aggiornamenti.
- Nuovo checkpoint `diaspro-v16-td-safe.json`, SHA256 `545eb954b335285158eee0cd83eb8096759d9fa15f4f7519f7d38010cc329735`; v15 respinta e tutti i checkpoint precedenti restano conservati.
- 32 test Release passati, inclusa la posizione che aveva respinto v15. Build del runner Release riuscita; hash del learner e del suo audit inclusi nel manifest delle partite.
- Servizi 9017/9026 avviati, health `diaspro-v16-td-safe` e hash Kingsrow verificato. Affinità 4 misurata su entrambi i processi reali. Avvio di quattro partite `test`, seme 31001, soltanto diagnosi; nessuna conferma in corso.
- `v16-prova4` conclusa: 0V/0P/4S, 306 mosse. Audit valido: replay, registro, esiti, aperture e colori appaiati, checkpoint e tutti i sorgenti verificati. Score 0; il TD non produce punti contro Kingsrow.
- Prossima diagnosi sulla ricerca: la tabella di trasposizione viene ricreata a ogni richiesta e ogni foglia converte la rappresentazione bitboard in damiera per il grafo. Non si interpreta il risultato come prova che una diversa cache basti; prima equivalenza e profili, poi partite di sviluppo.


## 2026-11-07 16:28 locali — v17 ripetizioni e ordinamento

- Fixture rossa prima della correzione: in una catena di due mosse la posizione intermedia veniva contata due volte. `PositionMemory.Commit` ora aggiunge la posizione corrente solo se non è già presente come arrivo della mossa precedente; il test passa e l'intera suite arriva a 34 test verdi.
- La TT dei valori resta per richiesta e include il contesto storico completo: riusarla tra turni con storia cambiata sarebbe errato. Vengono invece conservati per partita soltanto suggerimenti di migliore mossa, al massimo 65.536 per partita ed eliminati con la partita. Ogni suggerimento è soltanto ordinamento e deve coincidere con una mossa appena generata.
- Nuovo test di equivalenza a profondità fissa: due richieste consecutive, la seconda con suggerimenti, restituiscono stessa profondità completata, valore e mossa. Il suggerimento alla radice è verificato presente.
- Profilo iniziale v16 precedente: profondità 7, 38.656 nodi. Due serie v17 a un secondo: (6/6/6, 10.176/16.384/17.856 nodi) e (6/7/7, 11.648/22.272/22.272 nodi). Risultato misto, nessuna prova di forza o accelerazione stabile.
- Checkpoint separato `diaspro-v17-hints.json`; coefficienti di v16 invariati. Corretto il nome del checkpoint tramite la serializzazione pubblica dopo che il test di coerenza aveva respinto la copia ancora denominata v16.
- `v17-prova4`: 0V/0P/4S, 322 mosse. Audit completo: replay, registro, esiti, aperture/colori, checkpoint `0b0903a190c5323099db80729c047cae00dd930015325f615be3bb7db7bd2d63` e sorgenti verificati. Score 0, nessuna conferma.
- La correzione delle ripetizioni e i suggerimenti persistenti restano miglioramenti di correttezza/ordinamento, non un risultato agonistico. Prossimo lavoro: eliminare conversioni e allocazioni nella valutazione delle foglie, con equivalenza scalare obbligatoria.


## 2026-11-07 16:40 locali — v18 valutazione nativa a bit

- Aggiunta `Network.EvaluateFast(BitPosition, ...)`: caratteristiche, mobilità, esposizione, supporto e grafo SIMD leggono le maschere senza convertire la posizione principale in array. Rotazione tramite inversione dei 32 bit e scambio dei colori. Il generatore delle prese avversarie resta invariato in questa fase e contiene ancora una conversione interna.
- Equivalenza esatta allo scalare sui 500 casi casuali del test e suite completa 34/34. Aggiunti profili deterministici a profondità e a insieme fisso di foglie; i profili eseguiti in parallelo sono stati scartati e ripetuti serialmente.
- Due serie da 1.905 posizioni: array 376/386 ms, bit 257/250 ms, checksum raddoppiato coincidente; riduzione circa 35% dopo riscaldamento. Prima serie più piccola: 275 contro 264 ms. È velocità del valutatore, non forza.
- Checkpoint `diaspro-v18-bitnet.json`, SHA256 `9c371b73117083a43f2f732e6e39f5d66aed57b6331563ed6aaf90c6fc7596a0`; pesi v16 invariati. Test Release 34/34 e build runner riuscita.
- `v18-prova4`: 0V/0P/4S, 316 mosse. Audit completo, nessuna interruzione; checkpoint e sorgenti verificati. Il valutatore più rapido non produce punti contro Kingsrow.
- Le versioni v13-v18 hanno accumulato ciascuna 0 punti sulle quattro aperture diagnostiche. La velocità da sola non è una strategia sufficiente. La prossima fase deve cambiare qualità della valutazione/apprendimento, non soltanto riordinare o accelerare lo stesso motore.


## 2026-11-07 17:01 locali — v19 pattern simmetrici e audit del learning

- Nuova testa con 128 pesi casella–pezzo e 2.048 relazioni dirette fra pezzi vicini. Il valore e ogni gradiente sono mediati fra posizione e posizione ruotata; inizializzazione a zero, sei coefficienti tattici ancora congelati. I 2.176 pesi vengono appresi soltanto da autopartite Diaspro registrate, senza dati o supervisione di Kingsrow.
- Test dedicato con pattern non nulli: simmetria cromatica, equivalenza scalare/bitboard, zero iniziale e conteggio di 2.309 parametri. Suite Release 35/35; learner compilato senza avvisi.
- Il primo fumo (`v19-pattern-fumo`) superava il vecchio audit di replay, ma l'audit esteso ha respinto la riproduzione dei pesi: il learner aggiornava anche durante le sei aperture casuali senza scrivere quelle transizioni. Il run `v19-pattern-100` è stato fermato e conservato come incompleto/non auditabile; il suo candidato non sarà promosso.
- Correzione: nessun aggiornamento durante l'apertura, ogni modifica successiva registrata. L'audit ora verifica hash del checkpoint iniziale e del candidato, coefficienti congelati e ricalcola esattamente tutti i pesi di pattern dagli errori TD e dagli stati salvati.
- Nuovo fumo `v19-pattern-fumo2`: 2 partite, 313 mosse, 301 aggiornamenti; replay, registro, esiti, hash e tutti i pattern verificati. Avviato `v19-pattern-100b`, seme 19112, 50 ms, logico 2, PID reale 10836, priorità BelowNormal.
- `v19-pattern-100b`: 100 autopartite, 11.814 mosse e 11.214 aggiornamenti. Audit completo valido, inclusi tutti i 2.176 pesi ricalcolati. Checkpoint conservato con SHA256 `585f707f092e58cc109e51005d08e5d72513a8600d8e6d84cf0deb60db07898d`.
- Integrazione separata in `diaspro-v19-pattern.json` e `LearnedPatterns.cs`; la serializzazione compilata deve coincidere byte per byte. Esito respinto: 33/35 test. Il riferimento del test zero-pattern è stato corretto perché `Net` ora contiene pattern appresi; il test di sicurezza resta invariato e fallisce: esposta 176,63734, sicura 96,70493, differenza 79,93241 < 80.


## 2026-11-07 17:13 locali — v20 regolarizzazione del pattern

- Diagnosi sulla stessa fixture: senza testa pattern i valori sono 182,49033 e 101,41087, margine 81,07946; il pattern riduce il margine di 1,14705. Non si modifica il test e non si avvia alcun confronto.
- v20 introduce `Network.PatternScale = 0,5` sia nel valore sia nel gradiente TD, mantenendo coerenti funzione e apprendimento. Il manifest registra la scala; l'audit usa 1 per i run v19 storici e il valore dichiarato per i nuovi. Rimane compatibile con l'audit v16, riverificato su 10.875 mosse.
- Nuovo nome candidato `diaspro-v20-pattern-guarded`, ripartenza da v18 e pesi pattern a zero: non si rinomina il candidato v19 respinto. Fumo `v20-pattern-fumo`: 2 partite, 245 mosse, 233 aggiornamenti, tutti i controlli validi.
- Avviato `v20-pattern-100`: seme 20111, 50 ms, logico 2, PID reale 36736, priorità BelowNormal. Nessuna sfida prima di audit completo e 35/35 test.
- Run concluso: 100 autopartite, 11.538 mosse e 10.938 aggiornamenti. Audit completo valido con scala 0,5 e tutti i pesi riprodotti. Nuovo checkpoint `diaspro-v20-pattern-guarded.json`, SHA256 `7b49b842d03520553a62b338fe6e2ba9d55e85acf0eac9e1b18d8bae5184a7a2`.
- Integrazione nei sorgenti senza sovrascrivere v19; `ManualWeights.Build()` coincide byte per byte. Suite Release 35/35, compresa sicurezza tattica, simmetria, equivalenza scalare/bitboard e ricerca. Health e manifest aggiornati alla testa pattern; `LearnedPatterns.cs` incluso negli hash verificati.
- Build del servizio e del runner Release senza avvisi. Servizi 9017/9026 con identità e hash attesi, affinità 4 sui processi reali e fixture legali. Avviato `v20-prova4`: quattro partite `test`, seme 31001, solo sviluppo. Nessuna conferma decisa prima degli esiti.
- `v20-prova4`: 0V/0P/4S, 360 mosse, nessuna interruzione. Audit completo: replay, registro, esiti, colori, aperture, checkpoint e sorgenti verificati. Score 0; non si avvia una conferma da 36 partite. Servizi fermati dopo l'audit.
- Build completa `WeightSpaceGames.slnx` Release riuscita senza errori o avvisi. Il candidato è tecnicamente integro ma non più forte nelle quattro misure: servono diagnosi strategica e apprendimento diverso, non ulteriori ridimensionamenti casuali della stessa testa.


## 2026-11-07 17:34 locali — v21 apprendimento dall'esito

- Diagnosi dei replay v20: il primo deficit materiale dopo una propria mossa compare ai ply 46, 29, 32 e 25; alcuni deficit precedenti sono soltanto scambi obbligatori subito recuperati. Non si usano questi stati o le mosse di Kingsrow per apprendere.
- Nei 100 self-play v20 l'errore assoluto medio non converge: decimi iniziali 0,2666/0,2963 e ultimo 0,2792. Questo motiva un cambio di stima del target, non prova da solo che il nuovo metodo sarà più forte.
- Nuova modalità `--outcome --pattern`: parte da v20, mantiene pesi e rete costanti dentro ogni episodio e aggiorna a fine partita tutti gli stati non di apertura verso l'esito dell'arbitro dal punto di vista del turno. Nessun bootstrap; le partite interrotte non producono aggiornamenti. Sei coefficienti tattici e scala pattern 0,5 restano congelati.
- L'audit ora supporta pattern iniziali non nulli e verifica conteggio degli stati eleggibili, target di ogni aggiornamento e zero apprendimento nelle interruzioni. `frames` distingue `eligibleForLearning`; il vecchio campo non viene reinterpretato.
- Fumo `v21-outcome-fumo`: 2 partite, 190 mosse, 178 aggiornamenti, replay/esiti/hash/pattern/target validi. Fumo `v21-outcome-interruzione`: 1 partita fermata al ply 8, 0 aggiornamenti, candidato invariato e audit valido.
- Avviato `v21-outcome-100`: seme 21111, 50 ms, logico 2, PID reale 16404, priorità BelowNormal. Nessuna partita contro Kingsrow durante il training.
- Run concluso e auditato: 100 partite, 11.286 mosse, 10.686 aggiornamenti. Replay, registro, esiti, hash iniziale/candidato, sei coefficienti congelati e tutti i pattern riprodotti. Checkpoint `diaspro-v21-outcome.json`, SHA256 `e76c26f30b2a07fd605175299ece4b00538351ac6638b24fe4151052d15f27cd`.
- Integrato in un file nuovo, rigenerando meccanicamente `LearnedPatterns.cs` dal checkpoint senza ottimizzazione esterna. Suite Release 35/35 e serializzazione byte per byte. Corretto per i futuri run TD il campo manifest `interruptedGamesLearned`: il run v21 outcome era e resta correttamente `false`.
- Servizio v21 e Kingsrow verificati su 9017/9026, hash avversario `91fbbd16...de3`, affinità 4 sui PID reali 5688/19652. Avviate quattro partite `test` sul seme 31001; nessuna conferma prima dei risultati.
- `v21-prova4`: 0V/0P/4S, 314 mosse, nessuna interruzione. Audit completo valido, checkpoint e sorgenti verificati. Nessuna conferma; servizi fermati e logico 2 libero.
- Il MAE outcome per decili resta 0,6592/0,3880/0,6427/0,5255/0,4735/0,5981/0,5712/0,4905/0,7439/0,5664: il solo esito finale non dà un segnale stabile con 100 episodi. v22 userà target n-step a 8 ply e pesi congelati dentro ogni episodio, senza trasformare diagnosi Kingsrow in supervisione.

## 2026-11-07 17:52 locali — v22 target a otto ply

- Nuova modalità `--n-step 8 --pattern`, inizializzata da v21. Pesi costanti dentro ogni episodio; target terminale se la fine rientra nelle otto semimosse, altrimenti valore futuro con segno del turno corretto. Un'interruzione apprende soltanto dagli stati con orizzonte completo. Sei coefficienti tattici e pattern scale restano congelati.
- I primi tre fumi sono conservati ma respinti dall'audit: mancava `QuietKingPlyCount`; serviva la definizione iniziale di ciascun episodio, non quella dell'intero run; il frame terminale non corrisponde a un'esperienza con scelta. Correzioni al formato e all'audit, senza promuovere quei candidati.
- Fumo valido `v22-nstep8-fumo4`: 2 partite, 185 mosse, 173 aggiornamenti; valori di ogni episodio, target, replay, registro, hash e pattern riprodotti esattamente. `v22-nstep8-interruzione4`: 20 mosse, 7 aggiornamenti, audit valido. Riverificato anche il run v21 da 100 episodi.
- Avviato `v22-nstep8-100`: seme 22211, 50 ms, logico 2, PID reale 28688, affinità 4 e priorità BelowNormal. Nessuna sfida durante l'apprendimento.
