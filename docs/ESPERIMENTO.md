# Hey Hay Dama: guida all'esperimento

Questa pagina indicizza le fonti tecniche pubbliche. Non è una bozza dell'articolo. I nomi storici e i messaggi degli agenti sono mantenuti per conservare il contesto, anche quando descrivono errori o scelte poi scartate.

L'obiettivo del promotore è osservare tre modelli diversi prima al lavoro indipendentemente, poi nel confronto reciproco, senza indicazioni tecniche umane sulle soluzioni. La dama costituisce il banco di verifica. La supervisione umana riguarda obiettivo, vincoli, richiami a proseguire ed estensione degli strumenti consentiti in risposta agli stalli. La classifica dei motori è un risultato dell'esperimento, non una misura universale della qualità dei modelli che li hanno costruiti.

## Regole e implementazioni

[Prompt iniziale](../prompt_modello.md), [contratto HTTP e requisiti](../specifiche_modello.md), [registrazione delle partite](../AGENTS.md).

Le regole sono cambiate tra le sessioni: le prime prove comprendevano apprendimento dagli esiti; le sessioni successive richiedevano pesi manuali e vietavano ottimizzazione automatica. La presenza dei runner di apprendimento non significa che i motori manuali finali siano stati addestrati con essi.

Il codice degli esperimenti è in [experiments](../experiments); i motori giocabili sono in [models](../models) e [src](../src). Versioni e configurazioni sono distinte: il server avviato con la configurazione predefinita non riproduce automaticamente gli endpoint usati nel torneo.

## Fonti per sessione

| Fase | Documentazione e dati |
|---|---|
| Prime prove di apprendimento | [Metodo e limiti Opale](../experiments/Dama.Learn/README.md), [riepiloghi conservati](evidence/README.md), runner `Dama.SelfImprove` e `Dama.Evolution` |
| Discussione e revisioni manuali | [Chat integrale](../arena/discussione/chat.md), [piani e changelog](../arena/discussione), [confronto Opale v1-v2](../models/Opale.C/results/frozen-20261106/report.json), [audit](../models/Opale.C/results/frozen-20261106/audit.json) |
| Sessione comune | [Chat integrale](../arena/coordinamento/chat.md), [protocollo](../arena/coordinamento/protocollo.md), [diagnostico](../arena/coordinamento/risultati/diagnosi/verbale.md), [risultato finale](../arena/coordinamento/risultati/conferma/resoconto.md), [audit indipendente](../arena/coordinamento/audit_AGENTE-C.json) |
| Nuovi motori contro Kingsrow | [Canale condiviso](../arena/kingsrow-challenge/chat.md), [Diaspro](../arena/kingsrow-challenge/diaspro/), [Avventurina e diario](../models/Avventurina/DIARIO.md), [Ametista: risultati e limiti](../arena/ametista/2026-11-07/RESULTS.md) |

Le estensioni successive autorizzano ricerca originale e poi esperimenti di apprendimento: non modificano retroattivamente i protocolli delle serie già congelate. Tempo aggiuntivo, strumenti e risorse cambiano insieme al confronto fra agenti; il materiale documenta scambi di idee ma non isola causalmente il vantaggio della collaborazione. Le conversazioni editoriali con il promotore, le bozze e lo ZIP per la scrittura restano fuori dal repository.

Le traiettorie del confronto v1-v2 e del torneo comune rimangono nei rispettivi percorsi originali. I manifest indicano versioni, hash e configurazioni. Alcuni verbali riportano percorsi assoluti della macchina originale: sono riferimenti storici, non credenziali né percorsi richiesti per una nuova installazione.

## Risultati da interpretare separatamente

Il [confronto con Kingsrow Italian del 7 novembre 2026](../arena/kingsrow/2026-11-07/resoconto.md)
aggiunge un riferimento esterno: 18 vittorie di Kingsrow contro ciascuno di
Opale C v3, Agata v9 e Klinamite v3, per 54 vittorie su 54, nessuna patta o
interruzione. Audit di 2.959 mosse e nove aperture appaiate. I tre motori
non sono stati modificati o addestrati. Kingsrow usa ricerca e conoscenza
precalcolata: questa fase non ha gli stessi vincoli della sfida originaria.
Manifest, hash, traiettorie e audit sono pubblici sotto `arena/kingsrow/2026-11-07`.

- Nel confronto congelato v1-v2: Opale originale 11 vittorie, 8 patte e 29 sconfitte; Opale C v2 23 vittorie, 17 patte e 8 sconfitte, su 48 partite per versione.
- Nel torneo comune: Agata v9 229 punti, Opale C v3 187, Klinamite v3 162. Sono stati avviati tutti i 294 incontri; 289 hanno un esito naturale e 5 sono interrotti senza punti.
- Il torneo finale non isola il miglioramento di ogni versione rispetto alla precedente: sono diversi anche avversari e aperture.
- Le vittorie ottenute ripetendo continuazioni memorizzate non dimostrano generalizzazione.
- La misura aggiuntiva di Agata va tenuta separata: gli ID non stabili e l'etichetta storica di un rivale sono limiti riconosciuti nella discussione.

I risultati negativi, i tentativi interrotti e le correzioni di metodo fanno parte della documentazione, non vengono rimossi dalla pubblicazione.

## Verifica e disponibilità dei dati

Gli audit registrati descrivono le verifiche eseguite durante le sessioni. Alcuni comandi di audit confrontano anche il registro condiviso originale: `App_Data` non è pubblicata perché contiene le stanze e i dati degli utenti. Un clone pulito non dispone quindi automaticamente di quel registro completo; non presentare il mancato accesso come un nuovo esito dell'esperimento.

I riepiloghi delle prime prove sono copiati da output locali ignorati da Git. Il relativo inventario elenca provenienza e hash; le grandi raccolte grezze di quelle prime prove non sono incluse nelle copie riepilogative. I dati già presenti sotto `arena/` e `models/*/results/` restano pubblicabili senza un'esclusione generale dei risultati.
