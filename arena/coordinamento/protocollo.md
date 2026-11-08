# Sessione comune — protocollo approvato all'unanimità

Approvazione esplicita di AGENTE-A, AGENTE-B e AGENTE-C nel canale alle 19:29. Inizio ufficiale: Unix `1793989899`, ore locali **19:31:39**. Il tempo si calcola solo con `echo $(( ($(date +%s) - $(cat arena/coordinamento/START)) / 60 ))`.

## Calendario (60 minuti)

- **0–5 — preparazione e congelamento:** ogni agente pubblica versione, hash SHA256 dei pesi, configurazione e comando di avvio in `pronto_AGENTE-X.md`. Dopo il congelamento la versione del primo girone non cambia.
- **5–20 — girone diagnostico:** 6 partite per coppia, tre coppie di colori e stesse aperture nella coppia. Agata, Opale C e Klinamite: 3 coppie, 18 partite.
- **20–30 — analisi comune:** risultati per coppia e colore, patte/interruzioni e due replay significativi. Errori tattici, finali e ripetizioni senza attribuzioni non verificate.
- **30–45 — miglioramento manuale:** una modifica ragionata alla volta, nuova versione e hash. Vietati training, fine-tuning, evoluzione, grid search e cicli automatici esiti→pesi.
- **45–58 — girone di conferma:** 98 partite per coppia, versioni congelate al minuto 45, seed generato dopo il freeze e pubblicato prima delle partite. Se il registro impedisce il completamento, il risultato è dichiarato incompleto senza selezionare campioni favorevoli.
- **58–60 — resoconto:** risultati separati per versione, limiti statistici, modifiche adottate o scartate e problemi infrastrutturali.

## Abbinamenti

1. Klinamite–Agata.
2. Klinamite–Opale C.
3. Agata–Opale C.

Seed apertura diagnostico: `731941`. Sei semimosse casuali condivise dalla coppia di partite; colori invertiti. Il seed del girone finale è generato dal runner soltanto dopo il freeze, senza guardare esiti. Temperatura e parametri restano quelli dei manifest. Agata usa un solo generatore per processo: tutte le partite e le richieste sono sequenziali.

## Regole tecniche

- Un solo runner HTTP, gestito da AGENTE-A sulla base di `arena/torneo`, dopo verifica e adattamento a ID stabili ed esecuzione sequenziale.
- Ogni partita è creata tramite `CompetitionStore` prima della prima mossa e completata una sola volta con il risultato dell'arbitro.
- Origine `benchmark`, directory `CompetitionStore.DefaultDirectory()` condivisa. ID stabili nel registro: `opale`, `agata`, `klinamite`; versione e SHA256 nei manifest e nei verbali. La classifica aggregata non separa le versioni e non è fonte dei risultati del torneo.
- Punteggio condiviso: vittoria 2, patta 1, sconfitta 0. Niente Elo o percentuali aggregate fra versioni diverse.
- Limite 240 semimosse: partita interrotta e conteggiata separatamente, mai convertita automaticamente in patta.
- Traiettorie complete in `arena/coordinamento/risultati/<girone>/`, con match ID del registro.
- Il runner non modifica pesi. Le sfide sono soltanto misurazione; le revisioni avvengono manualmente nella finestra prevista.

## Responsabilità

- **AGENTE-A:** calendario, runner centrale, registrazione e pubblicazione dei risultati; Klinamite.
- **AGENTE-B:** manifest Agata, riproducibilità/temperatura e analisi dei finali di dame.
- **AGENTE-C:** manifest Opale C, audit degli accoppiamenti e fixture tattiche condivise.
- **Kimberlite:** escluso da questa sessione perché non raggiungibile e senza manifest.

Canale operativo: `arena/coordinamento/chat.md`, solo append, intestazione `### [HH:MM:SS] AGENTE-X`, massimo 15 righe. Modifiche al calendario soltanto dopo nuovo accordo esplicito.
