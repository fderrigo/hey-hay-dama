# Revisione del rapporto finale

Revisione in sola lettura del 7 novembre 2026, durante la conferma Ametista,
eseguita dal revisore di metodo della sessione. Nessuna partita avviata
e nessuna modifica a codice o pesi dei motori.

Il confronto usa 18 coppie di aperture, con entrambi i colori. Il revisore
ha confermato la coerenza dell'intervallo t approssimato e del limite
Hoeffding sulle differenze di punteggio in `[-1, 1]`. Il secondo è molto
conservativo: con 18 coppie il margine è circa 0,6402. Un vantaggio osservato
non implica quindi che il criterio prescelto dimostri un miglioramento.
Le ipotesi di campionamento e il carico variabile restano limiti separati.

| Rilievo | Intervento o verifica ancora necessaria |
| --- | --- |
| Python ottimizzato disabilita gli `assert` | Lo script ora rifiuta l'esecuzione con `not __debug__`; controllo mirato superato |
| Riserva del protocollo Kingsrow non confrontata | Richiesti 100 ms in entrambe le serie; configurazione divergente rifiutata nel controllo mirato |
| Audit accettato senza legame crittografico ai dati | Il rapporto include gli hash di piano, manifest, partite e audit; gli audit devono comunque essere ricalcolati sulle serie concluse |
| Identità congelata fuori dal controllo statistico | Verifica separata di sorgenti, pesi, binari e configurazioni contro `final-freeze.json`, da ripetere alla conclusione |

Il criterio statistico e il calendario non sono stati cambiati. Gli hash
attestano quali file sono stati analizzati; da soli non dimostrano che un
audit precedente riguardi quei dati. Per questo l'esecuzione finale prevede
prima il replay e il controllo del registro, poi il rapporto statistico.

Evidenze già disponibili:

- `final-comparison-guard-check.json`: controlli mirati, zero partite create.
- `confirmation-opale/audit.json`: ricalcolato alle 15:27:23 UTC,
  36 partite concluse e 1.944 mosse verificate.
- `confirmation-integrity-midrun.json`: 26 file conformi durante la conferma,
  inclusi sette file originali di Opale; non è il controllo conclusivo.

La conferma Ametista è ancora in corso al momento della revisione.
Questo documento non attesta il completamento della sfida.

## Riesame dopo la conferma

La seconda revisione ha letto il rapporto concluso: 3V/4P/29S Ametista e
0V/0P/36S Opale; 4.205 e 1.944 mosse, 18 coppie per serie. Il revisore
conferma numeri, identità dichiarate, congelamento antecedente alle due
serie e distinzione fra vantaggio osservato e miglioramento dimostrato.
Nessuna mancanza critica documentale rilevata. Il controllo diretto
successivo dei file e del registro è in `final-integrity-audit.json`.
Non sono state avviate nuove partite per questa revisione.
