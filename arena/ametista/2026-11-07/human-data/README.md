# Esperimento sui dati umani

Autorizzazione successiva alla prima fase: [estensione](../authorization-extension.json), [piano prefissato](../human-data-plan.json). Nessuna modifica ai candidati v14/v17 congelati.

Fonti: archivi dei campionati assoluti italiani 2004 e 2005, offerti al download dalla [FID](https://www.fid.it/download/download.asp?lang=en). Download gratuito verificato, licenza aperta non individuata. I PDN e le posizioni estratte non vengono redistribuiti qui; sono conservati localmente in artifacts. Sono pubblicati i metadati tecnici, hash, motivi di scarto e codice originale.

L'importatore ricostruisce partite storiche tramite l'arbitro del progetto: non crea nuove partite e non inserisce risultati storici nella classifica. 333 partite accettate su 343, 20.931 posizioni. Gli scarti per terminale locale non provano errori dell'archivio originale.

Il modello separato ha 128 ingressi e un'uscita tanh, inizializzato a zero. Configurazione prefissata, nessun caricamento di pesi esistenti. Training e validazione divisi per partita sul 2004; verifica temporale sul 2005. Il filtro fra insiemi elimina anche posizioni equivalenti dopo inversione dei colori e rotazione della scacchiera.

Training concluso: 100 epoche da zero; esecuzione offline cronometrata di 1,62 secondi sul logico 0 dopo la fine della conferma, inclusa la valutazione. [Rapporto](learned-v1/report.json) e [modello appreso](learned-v1/model.json).

| Insieme | Partite residue | Posizioni | MSE appreso | MSE costante |
| --- | ---: | ---: | ---: | ---: |
| Training 2004 |149|9078|0,421419|0,429523|
| Validazione 2004 |36|1724|0,331591|0,333299|
| Test 2005 |147|8808|0,439062|0,442163|

Escluse 446 posizioni dalla validazione e 875 dal test per sovrapposizione. Una partita 2005 perde tutte le posizioni dopo il filtro. Miglioramenti piccoli; nessuna dimostrazione di forza in partita.

La [decisione manuale](manual-play-decision.json) ammette la combinazione esplorativa v18: valore manuale intatto più `round(100 * predizione)`, rischio invariato. Il coefficiente 100 è fissato prima delle prove; il neurone è addestrato sull'esito completo e può duplicare segnali già nel valore manuale. Il test 2005, utilizzato come filtro di accesso, non è una verifica indipendente della scelta del motore finale.

Verifiche: 34 controlli della combinazione; 512 confronti Python/C# con errore massimo 2,08e-17; 9.015 posizioni e 40.898 transizioni, oltre a 32 confronti minimax nella verifica della ricerca. Rapporti: [combinazione](combination-verification.json), [ponte numerico](cross-language-verification.json), [ricerca](search-verification.json).

Le sei partite v18 su tre aperture nuove, origine `test`, sono concluse e auditate dopo il [congelamento](../human-exploratory-freeze.json): **1V/1P/4S**, score 25%, 712 semimosse e nessuna interruzione. Il [resoconto](../human-exploratory-v18/summary.json) e l'[audit](../human-exploratory-v18/audit.json) verificano sei ID unici, tre coppie colore complete, replay legali e registrazione condivisa. I 22 file congelati e le relative copie conservano gli hash previsti.

Dopo le prime due sconfitte v18 è stata dichiarata un'[estensione esplorativa](../human-control-extension.json) con sei controlli v14 sulle stesse aperture e colori. Anche questi sono conclusi e [auditati](../human-control-v14/audit.json): **1V/3P/2S**, score 41,67%, 668 semimosse e nessuna interruzione. Il [rapporto appaiato](../human-exploratory-paired-report.json) verifica tutti i 12 replay e record `finished`; la differenza descrittiva v18 meno v14 è **-16,67 punti percentuali**. Non emerge un miglioramento e v18 non è promossa: v14 resta il candidato principale.

La decisione di aggiungere il controllo non era nel piano iniziale e il controllo segue il candidato nel tempo. Tre aperture non consentono gli intervalli appaiati del runner e non sostengono una prova causale generale; il risultato resta descrittivo. Nessuna ripetizione selettiva, regolazione automatica o modifica ai congelamenti.

Il [secondo calcolo appaiato](../human-comparison-report.json), con controlli di manifest, aperture e registro, concorda sui dodici risultati. L'[inventario della sessione](../final-session-match-inventory.json) separa questi incontri dai dati umani storici.

La [misura sulle DLL effettive](inference-cost.json), successiva alle partite, usa 1.000 posizioni sintetiche e sette round a ordine alternato: mediana di 834,534 ns per valutazione manuale e 1.107,583 ns con il contributo appreso, rapporto 1,327. Il costo statico aumenta di circa il 33%; questo non misura la ricerca completa e non stabilisce la causa degli esiti delle partite.

La [risoluzione del coordinamento](../human-control-coordination-resolution.json) documenta due esecuzioni concorrenti con lo stesso nome operativo. Una ha arrestato il servizio sperimentale dopo l'audit e avviato il controllo; il secondo tentativo è stato respinto prima di creare partite. Il controllo health successivo non era disponibile all'altra esecuzione; questa limitazione resta nel relativo [audit](../human-exploratory-integrity.json).
