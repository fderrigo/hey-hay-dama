# Changelog AGENTE-A — Klinamite

## [18:39:58] Inizio miglioramento
Discussione chiusa al minuto 10; nessuna modifica a codice o pesi durante il confronto.

## [18:41:30] Rete multi-livello e prospettiva
Creato servizio isolato `models/Klinamite` con 160 input e grafo feed-forward ReLU. La scelta usa `-Evaluate(next, -turn)`, come consigliato da AGENTE-B, con pesi tattici realmente asimmetrici. Materiale manuale 100/300 e posizione simmetrica.

## [18:42:20] Prese, OR e massimo
Aggiunti rilevatori pedina/dama con atterraggio libero; pedina contro dama esclusa. OR saturato per vittima su indicazione di AGENTE-C. Massimo fra esposizioni alternative e condizioni esplicite per doppie con vittime dello stesso tipo. Pesi manuali: attesa 35/70, tratto 90/170. Rilevatore di assenza di mosse legali, senza ricerca.

## [18:42:57] Fixture
Aggiunti test per rotazione/colori, pedina-dama, doppio attaccante, vittime alternative, doppia reale e scelta senza pedina esposta. Le fixture geometriche derivano dalla discussione con AGENTE-B e AGENTE-C; nessuna partita o ottimizzazione automatica usata.

## [18:45:14] Correzione da fixture e prima verifica
Le fixture hanno rilevato la direzione sbagliata dell'attaccante nella vista ruotata e una casella di test non valida. Corretta la geometria, non i pesi. Rigenerato il checkpoint deterministico: 9 test Klinamite superati. Nessuna partita di confronto avviata.

## [18:53:44] Integrazione e protocollo
Klinamite v2 registrato come servizio HTTP proprio su porta 9003, senza cambiare il motore condiviso. Build soluzione senza avvisi; richieste reali valide per Bianco, Nero e presa multipla; JSON invalido rifiutato con 400. Nessun incontro tra modelli avviato.

## [18:53:44] Massimo unificato e doppie miste
Su suggerimento di AGENTE-C, il massimo ora opera direttamente sui valori di tutte le prese alternative, anche fra pedine e dame. Aggiunte doppie di dama con vittime miste; pesi base invariati. Fixture di AGENTE-B sulla casa di origine vuotata e promozione superate; 12 test Klinamite verdi.

## [18:54:43] Riepilogo finale
Klinamite v2 è una rete propria multi-livello con 160 input e pesi scritti direttamente, senza training.
Tratto asimmetrico, OR per vittima, massimo fra alternative e doppie anche miste correggono le falle discusse.
Dodici fixture Klinamite passano; protocollo verificato per entrambi i colori, prese multiple, promozione ed errori JSON.
Build della soluzione riuscita; suite condivisa non verde per errori API 500 e lock del registro, quindi non dichiaro quei test superati.
Nessun confronto tra modelli eseguito: nessun nuovo dato di win rate; servizio aggiornato attivo su porta 9003.

## [19:17:24] Completamento autorizzato dopo spegnimento
Ripresa autorizzata dall'utente, mantenendo divieto di training, ottimizzazione automatica, ricerca e confronti fra modelli.
Aggiunti soltanto rilevatori manuali per triple di pedina e di dama, comprese vittime miste; valori per vittima invariati.
Tredici test Klinamite superati e build completa della soluzione senza avvisi o errori.
Protocollo reale verificato per triple semplici e miste con risposta 26-19-12-3; servizio finale attivo su porta 9003.
Nessun incontro fra modelli eseguito e nessun dato di win rate aggiunto; la precedente suite condivisa restava bloccata per errori API e lock.
