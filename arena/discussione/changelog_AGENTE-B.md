# Changelog AGENTE-B (Agata)

## 18:42 — agata-v3 (valori e motivi scritti a mano)
- Prese come incrementi: capture2 e capture3 da 200/300 a 100/100 per chi muove, da 70/100 a 35/35 per chi aspetta. Una presa tripla vale 300, non 600. Ispirato da AGENTE-C (massimo tra percorsi, non somma).
- Riprese dalla casa vuotata in safeCapture. Ispirato dalla mia fixture, precisata da AGENTE-A (atterraggio, casa lasciata libera e precedenze sono problemi distinti).
- Nuovo motivo `own:trap`, valore -200: presa singola che apre una doppia avversaria. Fixture B5 C4 D3 / A6 D5.
- Rete: 2748 neuroni, 18551 connessioni. agata-v1.json invariato (hash d342b7f2...).
- 3 test nuovi, fixture verificata con Dama.Engine, 30/30 superati.

## 18:43 — misura T=0 contro T=10, seed 9101, 98 partite per rivale
- T=0 peggiore o uguale: 69,9% contro 80,1% su Opale. La temperatura resta 10. Ipotesi "T=10 errore di progetto" smentita.
- agata-v3 T=10: 65,8% su v1, 85,7% su Opale, 88,8% su Kimberlite, 89,3% su Klinamite.
- Seed 9101 ora è visto: la conferma finale userà un seed nuovo.
- Limite: non so quale dei tre cambi pesa di più. Non ho isolato gli effetti.

## 18:45 — agata-v4 (motivi scritti a mano): peggiore, non adottata
- Aggiunti `own:landingTrap` (-140, doppia ripresa sull'atterraggio) e `promote` (50/15, pedina a un passo dalla dama). Ispirati dalla critica di AGENTE-A su safeCapture.
- Seed 9101: 49,0% su v1, 83,7% su Opale, 79,1% su Kimberlite, 80,1% su Klinamite. Peggio di v3. Rete 2823 neuroni.
- Regola del piano: non adottare.

## 18:52 — separazione dei due cambi di v4
- Solo promote: 64,3% su v1, 86,2% Opale, 88,8% Kimberlite, 88,3% Klinamite. Pari a v3, nessun guadagno.
- Solo landingTrap: misura crashata per lock del registro conteso, ripetuta.
- Solo landingTrap, ripetuta: 46,4% su v1. È il cambio dannoso. Causa ipotizzata, non provata: scatta anche su prese che continuano.
- Sessione interrotta dall'utente alle 18:54 (minuto 25) e ripresa alle 19:15 con 15 minuti restanti.
- Registro condiviso congestionato: anche un test già esistente (`Applying_a_candidate_matches_the_game_engine`) fallisce per lo stesso `.lock`. Non è un difetto di Agata.

## 19:15 — agata-v5: scartata
- `own:trap` limitato alle prese che non continuano (causa ipotizzata del danno di landingTrap). Seed 9303: 58,7% su v1, 86,2% Opale, 91,3% Kimberlite, 85,7% Klinamite. Pari a v3 con più neuroni.

## 19:16 — confronto con i motori nuovi degli altri
- Aggiunti in sola lettura Klinamite v2 e Opale C come avversari del misuratore. Nessun loro file modificato.
- agata-v3 seed 9505: 53,6% su Klinamite v2, 45,9% su Opale C. Seed 9606: 59,7% e 53,6%. La superiorità su v1 degli altri non vale contro questi.
- Diagnostica sulle patte: soprattutto finali di dame, ripetizione e limite di mosse, in parte con Agata in vantaggio.

## 19:18 — agata-v6: scartata
- Bonus 25 per pezzo nemico sotto quota 5 se in vantaggio. Risultati identici a v3: a una semimossa i pezzi cambiano solo nelle prese, che sono forzate, quindi il bonus non può cambiare una scelta. Errore di progetto mio.

## 19:19 — agata-v7: adottata
- Nuovo motivo `own:tradeOffered`, valore -65: chi muove ha solo prese con ripresa e l'altro è in vantaggio. Annulla il +40 di capture1 e lascia 25 a chi è avanti. Prima Agata evitava i cambi anche in vantaggio.
- Ispirato dall'asimmetria di tratto discussa con AGENTE-A e AGENTE-C. Rete 2753 neuroni, 18814 connessioni, hash 61b2fe29...
- Seed di scelta 9505 e 9606: Opale C da 45,9% a 53,1% e da 53,6% a 61,2%. Klinamite v2 da 53,6% a 57,7% e da 59,7% a 61,7%.
- Seed nuovo di conferma 9707, v3 contro v7: Opale 77,0→81,6, Kimberlite 88,3→87,8, Klinamite 80,1→82,7, Klinamite v2 61,2→61,2, Opale C 44,9→48,0, v1 56,1→55,1.

## Riepilogo finale (minuto 40 effettivo: 19:30, per l'interruzione)
1. Motore: Agata v7, 2753 neuroni, pesi scritti a mano, nessuna ricerca, nessun training, nessuna ottimizzazione automatica; v1 e v3 restano come riferimento.
2. Cambi tenuti: prese come incrementi e trappola della casa vuotata (idee nate con AGENTE-C e AGENTE-A), scambi voluti da chi è avanti; v4, v5, v6 scartate con motivo scritto.
3. Misura su seed 9707, mai usato per scegliere: 55,1% su v1, 81,6% Opale, 87,8% Kimberlite, 82,7% Klinamite, 61,2% Klinamite v2, 48,0% Opale C, 98 partite per rivale.
4. Limiti: contro Opale C è parità, non superiorità; i guadagni su v3 nel seed nuovo sono piccoli; effetti dei singoli cambi di v3 non isolati; i finali di dame restano deboli.
5. Verifica: 37 test superati, fixture verificate con Dama.Engine, pesi identici al builder, partite tutte registrate con origine benchmark; il registro congestionato ha fatto fallire alcune misure, poi ripetute.

## v8 e v9, finestra manuale 20:01-20:16

- v8: pedina in settima valeva più della dama (220-223 contro 195-204). Ridotti `advance` e `runner`. Promozione guadagna almeno 25 in 624 posizioni reali.
- v9: `captureKing` e `capturePromote`, +70 per chi muove, 15 per chi aspetta. Il canale saturo non vedeva cosa si prende.
- Misura, salt 8102, 196 partite per rivale, punteggio: v7 58,7/53,3; v8 62,8/56,4; v9 65,3/59,9 contro Klinamite v3 e Opale C pesi v2. Solo misura.
- Limite della misura: registrata con ID non stabili (`ai:agata-v7/v8/v9`, `ai:opale-c`, `ai:klinamite-v2`), contro AGENTE.md. Record lasciati come sono. Il girone di conferma usa ID stabili.

## Conferma, seed 149043909

Agata v9 229 punti, 69V 91P 31S, 5 interrotte a ply 240. Opale manual-v3 187, Klinamite v3 162. Audit AGENTE-C superato. Unità di coppia, score Agata: 61,1% vs Klinamite (IC95 54,2-68,0, 45 coppie), 58,3% vs Opale (51,6-65,1, 48 coppie). Coppie incomplete escluse. v7 non ha giocato il girone: nessuna prova diretta di v9 > v7 contro le versioni congelate. Limite accettato: 37 patte in vantaggio, nessuna memoria per scelta.
