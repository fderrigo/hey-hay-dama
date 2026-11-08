# Avventurina: diario di sviluppo

Motore nuovo, separato da Agata (che resta invariata in `models/Agata`). Sfida: battere Kingsrow Italian 1.19e
entro le 18:28:21 UTC del 7 novembre 2026, con le regole concordate in `arena/kingsrow-challenge/chat.md`:
coefficienti scritti a mano, nessun training né ottimizzazione automatica, nessun dato, codice, libro, database o
risposta di Kingsrow. Ricerca e tabelle dei finali calcolate da sé ammesse e dichiarate.

Orari in UTC.

## 10:30 Avvio

- Kingsrow ha battuto Agata v9, Opale e Klinamite 18-0 ciascuno (arena/kingsrow/2026-11-07). Una rete a una
  semimossa non può competere con un motore che cerca in profondità: Avventurina cerca.
- Architettura scelta: bitboard a 32 bit, generatore delle mosse con le priorità di presa italiane, alfa-beta con
  variante principale, approfondimento iterativo, tabella di trasposizione (2^21 voci, circa 48 MB), killer e
  history, estensione delle prese obbligatorie, estensione di una semimossa se l'avversario minaccia una presa.
- Storia della partita tenuta dal servizio per ID partita: le ripetizioni valgono patta nella ricerca. Era il
  difetto principale di Agata (37 patte in vantaggio nel girone finale).
- Valutazione a termini scritti a mano: materiale (pedina 100, dama da 170 in apertura a 200 nel finale),
  avanzamento con tetto, guardia della riga di fondo, centro, pedine libere di promuovere, dame centrali e negli
  angoli, mobilità, bonus agli scambi quando si è avanti. Lezione da Agata: la promozione deve sempre guadagnare;
  controllo automatico nel comando `check` (guadagno minimo 25).

## 10:40 Verifica del generatore

- `check`: confronto con l'arbitro condiviso `Dama.Engine.Rules` su cammini casuali. Trovato un errore: una dama
  che cattura in cerchio e torna sulla casa di partenza spariva (XOR di partenza e arrivo). Corretto.
- Dopo la correzione: 3.000 cammini, 222.964 posizioni, 1.156.530 mosse identiche all'arbitro.

## 10:45 Prime due partite di sviluppo (source=test, Kingsrow 9015, 1000 ms)

- Seed apertura 5001. Avventurina col Bianco: patta per ripetizione a 200 semimosse, partendo da +1 pedina
  dopo l'apertura casuale. Col Nero: sconfitta a 93 semimosse, partendo da -1 pedina.
- Osservazione: le 6 semimosse casuali creano spesso uno squilibrio di materiale. Per superare il 50% bisogna
  convertire le aperture favorevoli. Qui pesa la tecnica dei finali, e Kingsrow gioca senza database dei finali.

## 10:55 Tabelle dei finali

- Analisi retrograda dalle sole regole, per firma di materiale e lato al tratto; ogni voce contiene la distanza
  dalla fine in semimosse (parità = esito). Distanze: limite superiore, non sempre minimo; decrescono lungo la
  linea scelta, che è ciò che serve per progredire. Non modellate: regola delle quaranta mosse e ripetizioni.
- 4 pezzi: 41 tabelle, 14,9 milioni di posizioni, 45 s.
- Verifica indipendente con minimax eseguito con l'arbitro (non con il mio generatore): 391 posizioni decisive
  risolte entro la distanza indicata, 409 patte senza esito entro 8 semimosse.
- Scoperta: dama contro dama non è sempre patta. Dama in 1, dama in 32: chi muove vince in 11 (zugzwang con dame a
  passo singolo e presa obbligatoria), confermato dall'arbitro.
- 5 pezzi: 360 MB; costruzione per gruppi indipendenti in parallelo (calcolo offline), file letti mappati in
  memoria per non occupare RAM (sul PC restano meno di 1 GB liberi).

## 11:00 Ricerca

- Riduzione delle mosse tardive: mosse tranquille dopo la terza, non killer, senza promozione e senza lasciare
  prese all'avversario, cercate una semimossa più corte; se superano alfa vengono ricercate a piena profondità.
- Uso del tempo: una nuova iterazione parte se è trascorso meno del 55% del budget (prima 40%).

## 11:05 Tabelle a 5 pezzi e prima vittoria

- 5 pezzi: 85 tabelle in tutto, 374.220.600 posizioni, 511 s.
- a2 (riduzioni, tempo, tabelle a 4 pezzi), 4 partite seed 5101-5102 su Kingsrow 9025: 1V 0P 3S. La vittoria (Nero,
  partita 4) parte da +1 pedina dopo l'apertura casuale: a 5 pezzi la ricerca entra nelle tabelle e trova la vittoria
  esatta dalla semimossa 40. Le sconfitte: tattiche di centro partita, profondità 10-11 con la CPU occupata dalla
  costruzione delle tabelle.
- Valutazione più rapida: coni delle pedine libere precalcolati per casella (stessi nodi, circa 25% più veloce).
- a3 (tabelle a 5 pezzi, riduzioni più aggressive): profondità media su 30 posizioni reali da 13,2 a 13,7.

## 11:15 Cosa non ha funzionato

- Finestre di aspirazione: profondità media 14,8 contro 15,1 senza. Tolte.
- Test tattico (12 posizioni delle mie partite in cui la valutazione è crollata entro due mosse): la versione
  attuale ne vede 4 in anticipo; riduzioni più forti o assenti non cambiano il numero. Le sconfitte non dipendono
  da una tattica visibile entro un secondo con questa ricerca: Kingsrow costruisce vantaggi posizionali.
- a3, prime 4 partite seed 5201-5202: 0V 1P 3S. Esempio: il Nero (io) spinge una pedina in h3, resta bloccata,
  Kingsrow costruisce la combinazione intorno.

## 11:20 a4: struttura

- Pedina sostenuta (pezzo amico diagonalmente dietro): +3 a centro partita.
- Pedina avanzata bloccata (nessuna casa libera davanti, metà campo avversaria): -8 a centro partita.
- Avanzamento a centro partita ridotto (13 -> 9 in settima): spingere da soli contro un avversario più profondo è
  un rischio. Guadagno minimo della promozione controllato: 27.
- Avanzamento calcolato per riga con conteggio di bit.

## 11:20 Velocità
- Profilo per chiamata: generazione 1117 ns, valutazione 781 ns. La verifica delle prese faceva una visita in
  profondità per ogni pezzo anche senza prese possibili.
- Prese rilevate a bit (pezzo, vittima adiacente, casa d'atterraggio libera, per direzione) e mosse semplici
  generate a bit. Verificato con l'arbitro: 74.234 posizioni, 384.152 mosse identiche.
- Sulle stesse 37 posizioni, stesso processore: da 1,24 a 1,90 milioni di nodi/s, profondità media da 12,4 a 13,1.

## 11:22 a4: 0V 3P 3S (seed 5301, 1000 ms)
- Test tattico su 26 posizioni dove la mia valutazione è crollata entro due mosse: a 1 s ne vedo in anticipo 8,
  a 8 s 16. Il limite è la profondità, non un termine sbagliato della valutazione.
- Ordinamento già efficiente: 98,8% dei tagli avviene sulla prima mossa provata.

## 11:30 Potature: niente
- Mossa nulla (solo con almeno 12 pezzi e nessuna presa in vista), futility a profondità 1-2, riduzioni
  logaritmiche: +0,9 semimosse di profondità media tutte insieme, nessuna combinazione in più vista.
- Partite di sviluppo a 300 ms effettivi (due copie Kingsrow, stessa DLL, ciascuna sullo stesso processore
  logico del mio servizio): a5 2V 3P 7S, a5 con le potature 2V 2P 8S. Scartate.
- Quiescenza con i sacrifici (mosse che offrono una presa): 7 contro 6 combinazioni viste. Tenuta, guadagno
  piccolo.
- Cache delle valutazioni: neutra.

## 11:35 Dove perdo
- Tutte le partite di sviluppo, per squilibrio dopo l'apertura casuale: in vantaggio 3V 1P 0S, in svantaggio
  0V 0P 4S, pari 2V 10P 22S. Kingsrow vince le posizioni equilibrate a centro partita.
- Idea: Avventurina cerca le semplificazioni. Termine legato al lato che muove alla radice, un tot per ogni pezzo
  uscito dalla scacchiera. Con meno pezzi la tattica profonda conta meno, la mia ricerca scende più a fondo e
  sotto i 6 pezzi decidono le mie tabelle, che Kingsrow qui non ha.

## 11:45 Valutazione più rapida, banco più largo
- Tutti i pesi che dipendono dalla fase dipendono solo dal numero di pezzi: tabelle precalcolate per conteggio.
  Somma di controllo delle valutazioni su 200.000 posizioni identica; costo da circa 1200 a circa 480 ns.
- 12 partite non distinguono varianti: a5, a5 con semplificazione 8 e con semplificazione 4 fanno tutte
  2V 3P 7S sulle stesse aperture. Banco a quattro postazioni (logici 4, 6, 8, 10; ogni variante con la sua copia
  Kingsrow sullo stesso processore), 24 partite a 100 ms per variante.
- Distribuzione dei nodi: 75% all'orizzonte (catene di prese obbligatorie), 20% sacrifici provati, 2% estensioni
  per minaccia.
- Round 1, seed 5501, punti su 48: base 8, equilibrio fra le ali (peso 6) 12, senza avanzamento a centro partita
  10, semplificazione 8 9. Differenze dentro il rumore (deviazione standard del totale circa 3,5 punti).
- Round 2, seed 5601: equilibrio 10 → 11, equilibrio 6 → 10, equilibrio 6 + semplificazione 8 → 6, equilibrio 6
  senza avanzamento → 7. Semplificare non aiuta: a parità le posizioni semplificate le vince comunque Kingsrow.
- Round 3, seed 5701: equilibrio 10 → 15, equilibrio 16 → 13, dama a 200 in apertura → 17, mobilità 6 → 12.
- Round 4, seed 5801: equilibrio 10 → 13, dama 200 → 10, dama 230 → 10, dama 200 senza sacrifici → 10.
- Tengo l'equilibrio fra le ali, peso 10: penalità a centro partita per ogni pedina di differenza fra ala sinistra
  (colonne a-d) e destra (e-h). Un'ala sguarnita è il punto dove Kingsrow costruisce le combinazioni. Tutti gli
  altri ritocchi oscillano di ±3 punti fra un seed e l'altro: rumore. I pesi hanno smesso di rendere.

## 12:05 Idee di gioco
- Patta con valore positivo per il lato alla radice (+25, +50): contro un avversario più forte la patta vale.
  Round 5, seed 5901: equilibrio 10 → 12, patta +25 → 12, patta +50 → 11, ultima iterazione con budget pieno → 13.
  Nessun effetto.
- Catene di sacrifici nella quiescenza (fino a 4 o 8 semimosse oltre l'orizzonte): nel test tattico, ora con 110
  pericoli raccolti da tutte le partite di sviluppo, da 64 a 68 combinazioni viste in anticipo. In partita peggio.
  Round 6, seed 6001: equilibrio 10 → 16, catene 4 → 11, catene 8 → 12, catene 4 con budget pieno → 10.
- Quando si decidono le 304 sconfitte dei round 1-5 (prima semimossa da cui la mia valutazione resta sotto -80):
  con 20-23 pezzi 57, 16-19 77, 12-15 74, 8-11 70, 4-7 26. Semimossa mediana 31. Kingsrow guadagna in ogni fase,
  anche nei finali a 8-10 pezzi dove la mia ricerca arriva a 14 semimosse. Esempio: quattro pedine contro quattro,
  valutazione da 0 a -80 in 16 semimosse senza alcuna combinazione: Kingsrow arriva prima a dama e coordina meglio.

## 12:20 a7: equilibrio fra le ali
- Base contro equilibrio 10, 48 partite ciascuno sulle stesse aperture (seed 6101, 6201): equilibrio 24/96
  (1V 22P 25S), base 19/96 (4V 11P 33S). Con il round 1 (12 contro 8) è l'unica differenza coerente: le patte
  raddoppiano, le vittorie no. Congelato come valore predefinito nella versione a7.
- Round 8, seed 6301: a7 9, pedina isolata (peso 6) 9, pedine sui bordi (peso 4) 5, entrambe 11. Rumore.
- Nuovo metodo: riferimento a7 giocato una volta su quattro seed fissi (6301, 6401, 6501, 6601: 41 punti su 192),
  poi ogni idea gioca un round intero sulle stesse 96 aperture.
- Idee sulle stesse 96 aperture, punti su 192 (riferimento a7: 41): equilibrio 14 → 40, guardia della riga di fondo
  doppia → 43, avanzamento a centro partita doppio → 38, pedina sostenuta 6 e bloccata 16 → 35. Isolate e bordi
  sui primi due seed: 17 contro 17. A 100 ms i pesi della valutazione non spostano il risultato oltre il rumore:
  conta la profondità che manca.

## 12:52 Conferma a7
- Versione congelata a7: generatore a bit, valutazione con tabelle per numero di pezzi, equilibrio fra le ali 10,
  quiescenza con un sacrificio all'orizzonte, tabelle dei finali fino a 5 pezzi.
- Hash: Avventurina.dll f67f8d3b6bdb5cecb694a53533decd699e963a18efdcf06b44c1501909e74c77,
  Avventurina.Core.dll 297264b4a300bd5a83ad008f19cf4e89b9b36315c9465bddf691563e31fa5fcf,
  appsettings.json 9036949131ca38ef2af6d83fa5fe5c37ee2322510f64fc82cc9d8766e7a7dc3c,
  sorgenti concatenati 9d6d97cae72791bfaa684b5258d87b82f5132aa9c34e00aed057d5b38967c3cf,
  tabelle (85 file) 407b8c1182fea4398231c59825520da79352ee0dc2ab1d40526d6c6e4f611647.
- source=benchmark, 1000 ms effettivi per entrambi, seed 7001, 7101, 7201 mai usati, 12 partite ciascuno, tre coppie
  in parallelo sui logici 4, 6, 9. In più 12 partite senza tabelle sul seed 7001 (logico 10), riportate a parte.
- Criterio dichiarato prima: score oltre 50% sulle 36. Previsione: sotto.

## 13:13 Esito della conferma a7
- Con tabelle, 36 partite: 2V 11P 23S, 15/72, score 20,8%. Per coppia di aperture (18 coppie): IC95 t [12,0; 29,6],
  bootstrap [12,5; 29,2]. 25 sconfitte/vittorie per blocco, 10 patte per ripetizione, 1 per quaranta mosse,
  nessuna interruzione. Obiettivo oltre 50% non raggiunto.
- Senza tabelle, seed 7001: 2V 2P 8S (6/24); con tabelle sullo stesso seed 5/24. Differenza non dimostrabile.
- Registro: 48/48 partite presenti come benchmark, finished.

## 13:35 Progresso rispetto al mattino: nessuno dimostrato
- a4 (binari delle 11:20) sulle stesse 36 aperture della conferma, 1000 ms, source=test: 1V 16P 19S, 18/72.
  a7: 15/72. Differenza per coppia -0,17 punti, IC95 [-0,59; +0,26]. Velocità e equilibrio fra le ali, misurati a
  100 ms, non si trasferiscono a 1000 ms su aperture nuove. Errore di metodo: il controllo a 1000 ms andava fatto
  prima di congelare.
- Audit della conferma (revisore di metodo, su richiesta di C): PASS, 48 ID, 6.045 mosse. Limiti segnalati: hash
  non nei manifest (ora in results/conferma-a7/congelamento.json), picchi HTTP fino a 2,5 s. Tempo mediano per
  mossa di Avventurina 875 ms su 1000 (nessuna iterazione nuova oltre il 70% del budget).
- Racconto: hey-hay-dama-editoriale/RACCONTO_AVVENTURINA.md. Copie Kingsrow 9027, 9029, 9030 fermate; resta a7 su 9021.

## 14:06 Ripresa
- Mi ero fermato alle 13:40 con ore ancora disponibili; l'utente lo ha notato. Nuovo mandato alle 14:10: ogni mezzo
  ammesso tranne copiare Kingsrow (nessun codice, peso, libro, database o distillazione delle sue mosse). Ricerca
  multi-thread e ottimizzazione automatica ora ammesse, ma dichiarate come budget diverso dal confronto alla pari.
- Aggiunti: tabella di trasposizione condivisa senza lock (parola di controllo key ^ data ^ captured), lazy SMP
  (`ParallelSearcher`, aiutanti con profondità di partenza sfalsate), valutazione lineare a 92 caratteristiche con
  pesi di mezzo gioco e di finale (`Tuned.cs`), comandi Lab `pbench`, `features`, `label`.

## 14:30-15:15 Prove a 1000 ms sulle 36 aperture della conferma a7 (source=test)
Le aperture sono le stesse della conferma: la scelta della variante migliore fra queste è viziata dalla selezione,
per questo va rivista su seed nuovi.
- Balance 0, IterationLimit 1,0, tabella 2^21: 2V 11P 23S, 15/72 (a7: 15/72).
- Stessa con tabella 2^22: 3V 17P 16S, 23/72 (31,9%).
- Stessa con tabella 2^24: 2V 13P 21S, 17/72. Memoria fisica libera intorno a 240 MB: la tabella grande paga
  probabilmente in accessi lenti; non indagato oltre.
- Valutazione adattata sugli esiti delle partite registrate (regressione logistica, materiale fisso,
  regolarizzazione L2): 2V 10P 24S, 14/72. Scartata. Primo tentativo senza materiale fisso dava alla dama in finale
  un valore minore della pedina.
- Lazy SMP 4 thread, posizioni di prova a 1000 ms: circa +0,8 semimosse di profondità, misura con CPU contese.
- In corso: etichette dalla ricerca di Avventurina stessa a 30 ms (autodistillazione) e verifica della tabella
  2^22 sui seed nuovi 8001 e 8101, con a7 giocato accanto sulle stesse aperture.

## 15:37 Round D: la tabella 2^22 non regge su aperture nuove
- Regola dichiarata in chat prima dell'esito: seconda conferma solo se t22 fa almeno quanto a7 sui seed nuovi.
- Seed 8001 e 8101, 1000 ms, source=test, stesse aperture per le due versioni: a7 1V 13P 10S, 15/48;
  t22 2V 5P 17S, 9/48. Per coppia (12 coppie) -0,50, IC95 t [-1,15; +0,15]. Nessuna seconda conferma.
- Il +8 sulle aperture 7xxx era selezione più rumore: ho scelto la variante migliore fra quattro sulle stesse 36
  aperture su cui l'ho misurata.
- Lo script di lancio è stato chiuso dal sistema per memoria bassa; i processi delle partite sono continuati e
  hanno chiuso tutte le 48 partite (nessuna interruzione).
- Tolta dal sorgente la tabella a due slot scritta e mai provata; ricompilando, i binari coincidono byte per byte
  con quelli a8 usati nelle prove.
- Processi di ottimizzazione chiusi su richiesta di AGENTE-C (giravano senza affinità, anche su 5/7/11, durante
  la sua conferma).

## 16:12 Tre thread: nessun effetto visibile (budget diverso, diagnostica)
- a8 con impostazioni a7 e 3 thread (logici 4/6/9) contro Kingsrow a un thread (logico 10), 1000 ms, seed 8001 e
  8101, source=test: 1V 9P 14S, 11/48 (22,9%). a7 a un thread sulle stesse aperture: 15/48.
- Il calcolo in più (circa +0,8 semimosse misurate) non sposta il risultato; il divario stimato è di circa tre.
- Fine lavori: a7 congelata di nuovo su 9021 a un thread, copie Kingsrow 9027/9029/9030 fermate, 9025 riavviata.

## 17:00 Training su dati pubblici (nuova istruzione dell'utente)
- Fonte: archivi della Federazione Italiana Dama su federdama.org (pagina "Database Partite": CI 2004 italiana,
  Champions Cup 2004 e 2006; cartella PDN: "pdn Assoluto Dama Ita 2010-2019.rar"). Partite umane di campionato,
  pubblicate per il download gratuito; licenza aperta non trovata, quindi i PDN restano fuori dalla repo e uso solo
  mosse ed esiti. Nessuna partita di motori, nessun dato Kingsrow (sul sito c'è anche un database dei finali di
  Kingsrow: non scaricato).
- Importazione (`Lab pdn`): ogni mossa rigiocata con il generatore; 1.305 partite valide, 198 scartate (formato),
  76.501 posizioni. Numerazione PDN = indice interno + 1 (verificato: la numerazione specchiata non valida nulla).
- Libro d'apertura scartato: dopo le 6 semimosse casuali, solo 10 partite su 252 delle mie toccano posizioni dei
  maestri.
- Comparison training (`Lab policy` + numpy/scipy): 48.961 decisioni quiete dei maestri, ogni mossa legale giocata
  e prese forzate risolte; softmax sulle posizioni risultanti, mossa del maestro come bersaglio, materiale fisso
  (100, dama 170/200), L2, validazione su 1/10 delle decisioni. T=50: mossa del maestro prima nel 36,8% dei casi
  di validazione; T=20: 39,8% (casuale circa 12%). Pesi discutibili (Trade -66, avanzamento negativo nel mezzo gioco):
  li giudicano le partite.
- Errore di risorse: un primo avvio del training aveva affinità sul logico 4, occupato da AGENTE-C dalle 16:38 UTC;
  rimasto quasi fermo, chiuso, scuse in chat. Poi solo logici 6/9/10.
- T=10: 40,0%, come T=20. Il modello lineare su queste caratteristiche è saturo intorno al 40%.

## 17:25-17:35 Pesi FID a 1000 ms (source=test), serie interrotta
- Pesi T=20, seed 8001 e 8101, coppie sui logici 9 e 10. Il sistema ha chiuso i processi delle partite per
  memoria bassa dopo 7 e 6 partite. Sulle stesse 13 partite: pesi FID 7/26, a7 9/26, a7 con 3 thread 6/26.
  Nessun miglioramento visibile; campione troppo piccolo per escluderlo.
- Le due partite rimaste "active" nel registro (30a209b1…, 5f048f2e…) segnate interrotte con il nuovo comando Lab
  `interrupt` (motivo runner-killed-low-memory), attraverso `CompetitionStore.Save`.
- Servizi di misura e copie Kingsrow 9029/9030 fermati per liberare memoria; non rilancio senza istruzione.
