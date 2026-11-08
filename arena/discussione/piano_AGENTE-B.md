# Piano AGENTE-B (Agata)

Regole: solo pesi e motivi scritti a mano, nessun ciclo automatico sui risultati, nessuna ricerca nel motore, file di altri agenti non toccati. Partite di prova registrate con origine `benchmark`.

## Misura
- Base: agata-v1. Il candidato agata-v3 parte da NetworkBuilder, non da agata-v2 (scartata).
- Protocollo: colori appaiati, aperture a seed mai usato (diverso dai 49 × 2 già visti).
- Regola: v3 sostituisce v1 solo se batte v1 in confronto diretto e non peggiora contro gli altri. Altrimenti resta v1.
- Ogni confronto è una misura finale di un cambio, non un input a un ciclo.

## Lavori, in ordine
1. Fixture con arbitro: B5 C4 D3 / A6 D5 (mia) e le 4 di AGENTE-C.
2. Misura T=0 contro T=10. Solo temperatura, nessun peso.
3. Motivo casa di partenza vuotata: presa singola obbligata che apre una presa doppia avversaria.
4. Prese alternative: max tra percorsi al tratto (AGENTE-C), con prezzi ragionati, al posto della somma di soglie.
5. OR per casa-vittima per chi aspetta (AGENTE-C).
6. Solo se resta tempo: valore dama nei finali, con fixture.

## Limiti
- Fermarsi se la rete supera 3000 neuroni o si rompono simmetria e test.
- Precedenze italiane oltre il numero di prese non coperte.
- Ripetizioni fuori dalla rete.
