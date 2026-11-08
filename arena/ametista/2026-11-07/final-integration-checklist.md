# Verifiche Ametista — prima conferma congelata

Il confronto di sviluppo search-v14/v16 è concluso e il candidato v14/manual-v4
è congelato. La tabella sottostante riguarda questa prima conferma.
Il tentativo supplementare successivo è descritto in `supplemental-plan.json`;
questo documento non ne attesta il completamento né lo sostituisce.

1. Chiudere e verificare tutte le 32 partite del confronto di sviluppo.
   Scegliere manualmente la versione, riportando anche eventuali regressioni.
2. Allineare il file di pesi predefinito del servizio e la verifica senza
   argomenti alla versione selezionata. Conservare tutti i file precedenti.
   Aggiornare README e istruzioni di avvio; non usare risultati finali per scegliere.
3. Compilare/pubblicare in una directory nuova. Verificare che i pesi siano
   inclusi e che il servizio avviato senza --weights esponga hash e versioni
   corretti. Congelare sorgenti, pesi, binari, configurazioni e risorse.
4. Dopo il congelamento, eseguire le 36 partite Opale e le 36 Ametista
   previste da confirmation-schedule.md, senza cambiare il candidato.
   Registrare ogni partita e verificare replay, aperture, esiti e archivio.
5. Verificare l'integrazione web con un servizio e un archivio delle stanze
   temporanei separati, mantenendo la directory di classifica condivisa.
   Il test deve creare una partita source=test, osservare la registrazione
   prima della prima mossa, eseguire una mossa Ametista e verificarla con
   l'arbitro. Prima della creazione, salvare PID e comando effettivo del web,
   hash della sua configurazione e override espliciti: endpoint Ametista
   identico al ModelBaseUrl verificato, CompetitionDirectory assoluta uguale
   a DAMA_COMPETITION_DIRECTORY del runner e DataDirectory temporanea nuova.
   Il solo catalogo pubblico non attesta l'endpoint del servizio chiamato. Arrestare quel server prima di segnare la stessa partita
   interrotta, senza punti. Non riavviare il suo archivio delle stanze.
6. Verificare nuovamente i sette hash originali di Opale; distinguere i
   risultati di sviluppo, il confronto con Opale e la forza contro Kingsrow.
7. Pubblicare nel repository dati tecnici, istruzioni e discussioni;
   completare il racconto tecnico personale solo nella cartella editoriale
   esterna. Non dichiarare vittoria o miglioramento senza evidenza adeguata.

Scadenza: 2026-11-07T18:28:21Z. Obiettivo operativo di congelamento:
15:30Z. Nessun punto di questo elenco autorizza a superare la scadenza,
ritoccare pesi automaticamente, copiare altri motori o distribuire Kingsrow.

## Stato verificato durante la conferma

| Requisito | Evidenza | Stato |
| --- | --- | --- |
| Confronto di sviluppo completo e scelta manuale | `search-comparison-recovery-report.json`, `final-candidate-selection.json` | Completato: 32 esiti appaiati, due tentativi originali conservati separatamente |
| Default del servizio e verifica coerenti | `final-service-ready.json`, `final-package-verification.json` | Completato: entrambi manual-v4 |
| Pacchetto e congelamento antecedenti alla conferma | `final-freeze.json`, `confirmation-start.json` | Completato; hash ricontrollati prima delle due serie |
| Controllo Opale | `confirmation-opale/audit.json` | Completato: 36 sconfitte, 1944 mosse verificate |
| Conferma Ametista e confronto finale | `confirmation-ametista/audit.json`, `final-comparison.json` | Completato: 36 partite, 3V/4P/29S, 4205 mosse |
| Mossa reale via web e chiusura senza punti | `final-web-integration-summary.json`, `web-final-check-v3.json.interrupted.json` | Completato, con due tentativi precedenti falliti documentati |
| Opale preservato | `final-integrity-audit.json`, `session.json` | Ricontrollo conclusivo passato: sette file originali immutati |
| Resoconto della conferma pubblico ed editoriale | `RESULTS.md`, racconto esterno `RACCONTO_AMETISTA.md` | Aggiornati con esiti e limiti; nessuna superiorità rivendicata |

La tolleranza HTTP del web non modifica la ricerca congelata né i budget
del runner. Il test API non costituisce una verifica completa del browser.
La macchina condivisa e i cambiamenti del carico restano limiti dichiarati.

## Tentativo supplementare

| Requisito | Evidenza | Stato |
| --- | --- | --- |
| Variante originale separata, stessi pesi e verifiche tattiche | `contact-freeze.json`, `contact-package-verification.json` | Completato; v14 e Opale preservati |
| Confronto di sviluppo completo e scelta manuale | `contact-comparison-report.json`, `contact-manual-selection.json` | Completato: v14 1V/0P/7S, v17 1V/1P/6S |
| Calendario nuovo, risorse e congelamento antecedenti | `supplemental-confirmation-protocol.md`, `supplemental-confirmation-selection.json`, `supplemental-confirmation-freeze.json` | Registrati prima dell'avvio, 72 partite previste |
| Conferma completa e rapporto appaiato | `supplemental-confirmation-report.json` | Completato: 72 partite; v14 1V/7P/28S, v17 0V/6P/30S; nessun miglioramento dimostrato |
| Integrità dopo la seconda conferma | `supplemental-final-integrity.json` | Completato: 86 file, 72 registrazioni, hash e Opale preservati |
| Decisione conclusiva e documentazione coerente | README, RESULTS e diario | V17 non promossa; v14 resta il candidato principale; prima conferma invariata |

La prima conferma resta una misura autonoma. Il supplemento non autorizza
a cambiarne gli esiti, a sommare campioni di sviluppo come prove indipendenti
o a dichiarare una superiorità non sostenuta dal criterio stabilito.

## Esperimento con dati umani e chiusura

| Requisito | Evidenza | Stato |
| --- | --- | --- |
| Estensione autorizzata e metodo dichiarato prima del training | `authorization-extension.json`, `human-data-plan.json` | Completato; learner da zero, nessun peso precedente caricato |
| Importazione storica distinta dalle partite reali | `human-data/audit.json` | 333 partite accettate, 20.931 posizioni; zero inserimenti storici nella classifica |
| Training e controlli numerici | `human-data/learned-v1/report.json`, `human-data/cross-language-verification.json`, `human-data/search-verification.json` | Completati; piccolo beneficio offline, nessuna prova automatica di forza |
| Sei prove e sei controlli appaiati | `human-comparison-report.json` | Completati e registrati; v18 1V/1P/4S, v14 1V/3P/2S |
| Limite metodologico del controllo aggiunto | `human-control-extension.json` | Dichiarato dopo due risultati parziali; analisi esplorativa |
| Coordinamento delle esecuzioni concorrenti | `human-control-coordination-resolution.json` | Attribuzione dichiarata conservata, tentativo duplicato respinto senza partite aggiuntive |
| Costo statico misurato dopo le partite | `human-data/inference-cost.json` | Rapporto 1,327 su fixture sintetiche; non prova causale degli esiti |
| Inventario e preservazione finali | `final-session-match-inventory.json`, `final-preservation-check.json` | 309 ID unici, 305 conclusi e 4 interrotti; 309 registrazioni immutate e originali preservati |
| Decisione finale | `final-engine-decision.json` | Resta v14/manual-v4; v17 e v18 non adottate, tutte conservate |
| Materiale tecnico ed editoriale | `RESULTS.md`, README, racconto e diario esterni | Aggiornati; cartella editoriale verificata fuori da Git, nessun deploy o push dichiarato |
