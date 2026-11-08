# Piano AGENTE-C — Opale

Sessione: START = 1793986188 (2026-11-06 17:29:48 UTC).
Discussione termina 17:39:48 UTC; modifiche terminano 18:09:48 UTC.

## Lezioni e perimetro

1. AGENTE-B e AGENTE-A: l'asimmetria del tratto deve essere nei pesi tattici;
   cambiare prospettiva da sola non modifica una valutazione antisimettrica.
2. Discussione con AGENTE-A/B: saturare i doppioni e prendere il massimo fra
   alternative; sommare solo le vittime di uno stesso percorso di presa.
3. AGENTE-B: una casa di partenza vuotata puo aprire una doppia avversaria.
   I motivi locali non dimostrano sicurezza tattica oltre il loro orizzonte.

Scrivo soltanto in `models/Opale.C` e nel mio changelog. Nessuna modifica ai
file degli altri, al motore condiviso, alla classifica o alla configurazione web.
Nessun training, fine-tuning, ricerca evolutiva, griglia, gradiente o memoria
di partite nel candidato. Nessun minimax nel motore giocabile.

## Implementazione ragionata

- Mantengo i canali manuali materiali e posizionali di Opale v1, rimuovendo
  i vecchi bonus di presa per evitare doppio conteggio.
- Nuova rete ReLU sparsa con collegamenti fra neuroni, input 128 occupazioni.
  Costruttore deterministico, non dipendente da risultati di partite.
- Rilevatori statici di percorsi da una/due prese: direzione, vittima ammessa,
  atterraggi vuoti, ostacoli delle vittime, arresto alla promozione.
- Porte per la priorita della lunghezza e del pezzo catturante nei percorsi
  rappresentati; massimo del materiale catturabile, espresso tramite ReLU.
- Presa disponibile al tratto: 90% del materiale della sequenza; minaccia
  differibile: 15%. Sono scelte manuali motivate dal fatto che una presa ora
  e obbligatoria, una futura puo essere evitata. Nessuna scansione dei valori.
- Valuto le posizioni candidate dal lato che deve muovere, cambiando segno
  per il giocatore che sceglie. Solo simulazione della mossa candidata.
- Espongo il protocollo dama-model/v1 e un runner di verifica indipendente.

## Verifica e criterio di arresto

- Fixture geometriche discusse: pedina contro dama; due attaccanti sulla
  stessa vittima; due prese alternative; doppia concatenata; promozione;
  ostacoli catturati; esempio della casa vuotata di AGENTE-B.
- Confronto con le regole, simmetria rotazione/cambio colore, serializzazione,
  determinismo del costruttore, risposte legali e validazione HTTP.
- Congelo il candidato dopo le fixture. Confronto abbinato con Opale v1
  contro Kimberlite e Agata su nuove aperture, colori appaiati, stessa
  temperatura avversaria. Nessuna partita Opale contro Opale.
- Ogni partita reale viene registrata prima della prima mossa e conclusa
  solo con il risultato dell'arbitro; interruzioni senza punti. Verbali e
  hash identificano versioni e protocollo senza affidarsi alla classifica.
- Una misura delle prestazioni, senza ciclo automatico che cambi i pesi.
  Riporto anche regressioni e limiti: due prese non coprono tutta la tattica.
- Alle 18:09:48 UTC fermo il lavoro e aggiungo cinque righe finali al changelog.
