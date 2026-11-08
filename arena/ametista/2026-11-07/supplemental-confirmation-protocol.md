# Conferma supplementare condizionata

Protocollo dichiarato durante lo sviluppo, prima di una scelta del candidato
e prima di generare le 18 aperture riservate 570301–570318. Non autorizza
l'avvio se il confronto di sviluppo non soddisfa il criterio già fissato.

Prerequisiti: tutte le 16 partite di sviluppo concluse e auditate, punteggio
v17 strettamente maggiore di v14, scelta manuale motivata, integrità di
entrambi i candidati e disponibilità di tempo entro le 18:28:21 UTC.

Per verificare il confronto con v14 oltre alla prestazione contro Kingsrow,
il calendario eventuale comprende **36 partite per versione**, sulle stesse
18 aperture riservate, ciascuna nei due colori. La prima conferma di v14
resta separata e non viene sostituita.

Rispetto al piano iniziale sul solo processore logico 0, si propone una
seconda corsia sul logico 4, dichiarato libero da AGENTE-B alle 17:13:12.
Questo è un cambiamento esplicito delle risorse per la sola eventuale
conferma, motivato dal tempo necessario per 72 partite. Nessun risultato
del supplemento di sviluppo viene modificato.

- Semi alternati fra le due corsie: nove aperture per corsia.
- Per ogni apertura entrambe le versioni giocano sullo stesso processore
  logico contro la stessa istanza Kingsrow, sempre nei due colori.
- Ordine delle versioni alternato fra le aperture; niente partite concorrenti
  sulla stessa corsia. Al massimo due partite concorrenti sul computer.
- Ogni motore conserva un solo thread di ricerca, 1000 ms configurati,
  nessun ponder e limite di memoria dichiarato di 1 GiB per processo.
- Le istanze delle due corsie devono usare gli stessi binari e pesi, con
  hash, PID, endpoint, affinità, configurazioni e lock distinti registrati
  prima di avviare la conferma. L'ulteriore istanza Kingsrow serve soltanto
  da avversario; nessuna sua risorsa decisionale entra in Ametista.
- Origine `benchmark`, registrazione prima della prima mossa e audit di
  ogni coppia; identificatori nuovi. Nessun aggiornamento dei candidati.

Il confronto statistico usa 18 differenze fra punteggi delle coppie;
intervallo t approssimato e limite Hoeffding come nella prima conferma.
Un miglioramento confermato richiede entrambi i limiti inferiori sopra zero.
La superiorità su Kingsrow è una domanda distinta e richiede entrambi i
limiti inferiori del punteggio della versione sopra 0,5. Nessuna conclusione
generale da serie parziali, da un solo colore o dalle prove di sviluppo.

I processori logici non costituiscono macchine isolate. Carico condiviso,
riscaldamento del runtime e differenze fra corsie restano limiti. Ogni
versione attraversa entrambe le corsie con gli stessi semi: questo evita
di assegnare sistematicamente un processore a una sola versione, ma non
dimostra identico tempo CPU. Se il calendario non termina entro la scadenza,
si conservano gli esiti e le interruzioni senza dichiararlo completo.
