# Conferma e controllo Opale — piano prima dei risultati

Registrato il 7 novembre 2026 durante lo sviluppo, prima di generare le
18 aperture riservate in `confirmation-plan.json`.

La scadenza resta 18:28:21 UTC. Per lasciare spazio anche a un controllo
omogeneo di Opale, l'obiettivo operativo è congelare Ametista entro le
15:30 UTC, anticipando il limite massimo comune delle 17:00 UTC.

1. Congelare codice, pesi, configurazione, binari e hash del candidato.
2. Eseguire il controllo Opale C manual-v3 contro Kingsrow sulle 18 aperture
   riservate, a colori invertiti, origine `benchmark`, stesso budget di
   1000 ms effettivi e stessa coppia di affinità/processi dell'avversario.
3. Eseguire le 36 partite di Ametista contro Kingsrow sullo stesso set.
   Nessuna modifica al candidato dopo aver visto il controllo Opale.
4. Audit completo dei replay e del registro; distinguere confronto con
   Opale e superiorità su Kingsrow. Nessuna interruzione diventa una patta.
5. Aggiornare resoconto pubblico, istruzioni di utilizzo e racconto privato
   con risultati, revisioni scartate, risorse e limiti effettivamente osservati.

Se i processi richiedono più tempo del previsto, anticipare il congelamento
o ridurre altre prove di sviluppo; non ridurre silenziosamente il set di
conferma. Il processo di ricerca deve fermarsi entro la scadenza stabilita.
La conferma non è un nuovo ciclo di scelta dei pesi.

Il budget HTTP tiene conto della riserva effettiva dei diversi adattatori:
Opale C sottrae 25 ms, Ametista e Kingsrow sottraggono 100 ms. Il runner
usa quindi `protocolReserveMs: 25` per Opale e `100` per gli altri, con
`searchMs: 1000` in tutti i casi. Opale può restituire la propria valutazione
prima di esaurire quel limite: il controllo non ne modifica l'algoritmo.
