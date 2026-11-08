# Revisione indipendente del test web

Revisione in sola lettura del REVISORE-METODO, 7 novembre 2026.
Nessuna partita creata dalla revisione o dalla compilazione.

- Contratti create/advance, token e forma delle risposte: corretti.
- Il catalogo espone ID, nome e descrizione, non l'endpoint: la health di un
  servizio separato non attesta da sola il modello chiamato dal web. Il
  lancio finale deve documentare e verificare configurazione e override
  effettivi del processo temporaneo prima della partita.
- Corretto il rischio di iniziare una partita prima di sapere se il report
  ? scrivibile: archivio e file preparati prima del POST; file CreateNew
  tenuto aperto e flush effettuato. ID salvato appena ricevuta la risposta.
  Una perdita della risposta di creazione resta un errore distribuito da
  riconciliare nel registro e nell'archivio isolato, non autorizza un retry.
- Directory classifica e stanze devono essere verificate nel lancio; il
  runner registra ora anche il percorso effettivo CompetitionStore.
- L'interruzione mantiene lo stesso ID e assegna zero punti. Reconcile
  normalmente ignora un record pi? vecchio; ? un aggiornamento successivo
  della stanza che potrebbe riattivare la partita. Corretto il commento.

Helper compilato in artifacts/challenge-webcheck-v2: zero errori/avvisi.
Il primo tentativo --no-restore in una nuova cartella ? fallito per assets
assenti; il build con restore ? riuscito. Nessun motore in gara ricompilato.
La prova web reale resta da eseguire sul candidato finale.
