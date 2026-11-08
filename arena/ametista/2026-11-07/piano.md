# Ametista — piano iniziale

T0: 2026-11-07 10:28:21 UTC; scadenza inderogabile: 18:28:21 UTC.
Nell'ultima ora congelare il candidato, concludere misura, audit ed editoriale.
Accordo dei concorrenti nel nuovo canale `arena/kingsrow-challenge/chat.md`.

## Preservazione

Opale C v3 rimane immutata; i suoi sorgenti e pesi sono elencati con SHA256
in `session.json`. Ametista ha cartella, nome, ID `ametista`, porta 9016 e
versioni proprie. Nessun file di Kingsrow viene usato per costruire la rete.

## Architettura scelta dopo l'autorizzazione alla ricerca

Rappresentazione compatta a bitboard e generazione originale delle mosse,
verificata contro l'arbitro condiviso. Ricerca con approfondimento iterativo,
ordinamento, tabella di trasposizione, estensione delle prese obbligatorie.
Valutatore neurale a pesi manuali con canali relazionali: materiale,
mobilità, connessione, blocco, pressione sulle vie di promozione.
Una seconda uscita segnala instabilità per limitare le riduzioni selettive;
questa è un'ipotesi progettuale da misurare, non una novità scientifica rivendicata.
Memoria limitata alla partita e alla ricerca, nessuna distillazione, nessuna
tabella esterna e nessun apprendimento automatico. Prima versione senza tabelle
dei finali. Eventuali revisioni saranno motivate manualmente prima delle prove.

La diagnosi dei vecchi incontri segnala combinazioni oltre due prese e promozioni:
la ricerca estende interamente le prese, invece di affidarsi al limite dei
rilevatori di Opale. Il nuovo valutatore non ricopia quello precedente.

## Verifica

Fixture e posizioni sintetiche non sono partite e non producono record di classifica.
Verificare la parità delle mosse, delle transizioni, dei risultati e dei contatori
con `Dama.Engine`, compresi precedenze, ostacoli, promozioni e ripetizioni.
Ogni partita reale passa per `TrackedMatch` e il lock comune prima della prima mossa.
Sviluppo `source=test`, conferma `source=benchmark`; nessuna interruzione trasformata in patta.

Misura principale: 1000 ms effettivi per ciascun giocatore, un thread, nessun
ponder, memoria inferiore a 1 GiB con picco misurato. Tutti i servizi e la
configurazione di Kingsrow sono congelati prima del test finale.
Almeno 36 partite su 18 aperture nuove, colori invertiti, versioni e hash salvati.
Il set finale non diventa materiale per scegliere i pesi del candidato.

Progresso rispetto a Opale, prima vittoria e superiorità su Kingsrow sono
tre conclusioni diverse. Dichiarare solo quelle sostenute dalle misure;
calcolare l'incertezza usando la coppia di aperture, non le singole partite.
Un risultato negativo deve rimanere nei dati e nel racconto editoriale esterno.
