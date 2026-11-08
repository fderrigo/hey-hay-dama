# Ametista: protezione del contatto tattico

Variante supplementare `search-v17-contactguard`, separata dal candidato
`models/Ametista` v14 già confermato. Stessi pesi manual-v4, nessun training.

La sola modifica decisionale impedisce la riduzione LMR quando una mossa
tranquilla crea una cattura obbligatoria nella posizione figlia. La stima
di rischio del padre non descrive necessariamente quel nuovo contatto.
La maggiore precisione può costare profondità nel tempo disponibile.

Board, rete e compilatore sono collegati ai nostri sorgenti v14 conservati.
Search e verifica derivano dai nostri sorgenti, con la guardia e il contatore
aggiunti; Program aggiunge il contatore alla diagnostica HTTP. Nessun codice,
peso, libro o database di Kingsrow è usato nell'implementazione.

La variante è sperimentale e non sostituisce il modello nel catalogo web.
Il piano, le verifiche e il confronto registrato sono in
[`arena/ametista/2026-11-07`](../../arena/ametista/2026-11-07):
`supplemental-plan.json`, `contact-freeze.json`,
`contact-package-verification.json`, `contact-comparison-selection.json`.
Le partite di sviluppo sono state seguite da una conferma separata di 72
partite, 36 per versione sulle stesse 18 aperture. V14 ottiene 1V/7P/28S;
v17 ottiene 0V/6P/30S. La differenza appaiata non soddisfa il criterio di
miglioramento e nessuna versione è superiore a Kingsrow. V17 resta quindi un
esperimento conservato e non sostituisce v14. Esiti e controlli:
`supplemental-confirmation-report.json` e `supplemental-final-integrity.json`.

Conferma successiva completata: v17 0V?6P?30S contro v14 1V?7P?28S,
36 partite per versione. Nessun miglioramento dimostrato; variante non
adottata. Rapporto e decisione in `supplemental-confirmation-report.json`
e `supplemental-manual-decision.json` nella cartella sopra collegata.
