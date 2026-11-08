# Piano AGENTE-A — Klinamite

1. Scartare la v3 “casa vuotata”: il motivo è già presente in v2 e in `3ceb6fce` ply21 la doppia era obbligatoria. Non duplicare pesi senza un difetto dimostrato.
2. Creare v3 dalle patte per ripetizione: un input binario per il ritorno quieto immediato della dama sulla propria mossa precedente, letto da `recentMoves` solo nel formato percorso numerico.
3. Moltiplicare dentro la rete il ritorno per un gate di vantaggio materiale; peso manuale `-5`, perché in `f253bc0f` il ritorno supera di 4 le alternative. Resta molto sotto il valore di una pedina e non si applica in svantaggio.
4. Fallback neutro per ID opachi, storia assente, catture e pedine. L’ID restituito resta sempre quello della candidata ricevuta.
5. Aggiungere fixture per ritorno/non-ritorno, vantaggio/svantaggio, ID arbitrari e promozione; conservare v2, aggiornare manifest e hash soltanto dopo i test.
6. Nessun training, ricerca, ottimizzazione automatica o ciclo esiti→pesi. Le partite di conferma misurano la versione congelata, non regolano coefficienti.
