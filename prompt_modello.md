# Prompt per lo sviluppo indipendente di un modello

Copia il testo seguente in una nuova conversazione e allega `specifiche_modello.md`. Per confrontare implementazioni indipendenti, fornisci a ciascuna AI lo stesso prompt e la stessa versione delle specifiche, senza allegare il codice degli altri modelli.

---

Sviluppa da zero una rete neurale competitiva per la dama italiana, utilizzabile in partite contro esseri umani e altre reti.

Il file allegato `specifiche_modello.md` definisce il contratto di integrazione. Consideralo la fonte normativa per regole, codifica delle posizioni, richiesta, risposta, configurazione, limiti ed errori. Implementa un servizio completo compatibile con `dama-model/v1`; non modificare il contratto o l'applicazione di gioco.

Vincoli:

- La rete neurale è obbligatoria. Progetta tu l'architettura e scrivi direttamente pesi e bias. Non usare training, fine-tuning, gradient descent, ottimizzazione automatica dei parametri o modelli preaddestrati.
- Puoi scegliere linguaggio e architettura neurale. Consegna i pesi numerici effettivi, non soltanto una descrizione o pseudocodice, e un'inferenza eseguibile che li utilizzi. È ammesso un costruttore deterministico dei pesi, purché non impari da partite o esempi.
- Durante le partite le valutazioni devono provenire dai passaggi della rete. Sono ammessi codifica della posizione, filtro delle mosse legali, simulazione della singola mossa candidata, riconoscimento delle condizioni terminali e selezione del punteggio massimo. Non sostituire la rete con minimax, ricerca delle continuazioni, una tabella di mosse o chiamate a un'altra AI. Eventuali motori di ricerca sono ammessi soltanto come avversari separati per la valutazione.
- Per ogni richiesta valida restituisci una delle mosse ricevute in `legalMoves`, entro il limite previsto. Gestisci entrambi i colori, prese multiple e richieste concorrenti senza contaminare lo stato fra partite.
- Non assumere una specifica forza dell'avversario. Misura il comportamento e riporta separatamente risultati osservati, ipotesi e limiti. Non attribuire al modello livelli di gioco o garanzie che non hai verificato.

Consegna:

1. Codice sorgente completo del modello e del servizio HTTP.
2. Dipendenze e istruzioni esatte per avviare il servizio.
3. Configurazione da aggiungere a `Arena:Models`, senza credenziali reali.
4. Descrizione dell'architettura, dei pesi e del procedimento usato per costruirli, con numero di ingressi, strati, neuroni, attivazioni e parametri.
5. Test di conformità al protocollo e alle condizioni indicate nelle specifiche.
6. Valutazione riproducibile con metodo, budget di calcolo, risultati effettivi e limiti.

Procedi con l'implementazione. Se trovi un'ambiguità sostanziale nelle specifiche, segnalala precisamente; non introdurre modifiche incompatibili senza dichiararle.

Registrazione delle partite:

Per ogni partita effettivamente avviata durante lo sviluppo, registra l'origine `test` o `benchmark` nella classifica condivisa secondo `specifiche_modello.md` e `AGENTS.md`. Usa l'API di gioco oppure il registro comune `Dama.Competition`, aprendo una registrazione prima della prima mossa e aggiornandola con l'esito dell'arbitro. Non avviare cicli di partite senza registrazione. I dati artificiali usati per verificare formule o interfaccia devono restare in fixture isolate e non diventare risultati fittizi.

