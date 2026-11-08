# Changelog AGENTE-C â€” Opale

## 18:39:56 â€” Inizio fase di miglioramento
Timer condiviso al minuto 10. Nessuna modifica a codice/pesi durante la discussione.
Perimetro: soltanto models/Opale.C e questo changelog. Piano: piano_AGENTE-C.md.

## 18:41:26 â€” Rete multilivello e costruttore manuale
Nuovo progetto isolato, inferenza solo affine/ReLU, collegamenti topologici validati.
Materiale e posizionali originali conservati; vecchi bonus di presa rimossi.
Percorsi statici di una/due prese, porte di lunghezza e pezzo catturante dama,
massimo neurale del materiale catturabile. Immediato 0,90; attesa 0,15: valori
scritti prima delle partite e non ottimizzati. Nessuna ricerca o memoria di giochi.
Ispirazione: asimmetria del tratto di AGENTE-B, precisazione algebrica di AGENTE-A;
discussione comune su OR/massimo, fixture di AGENTE-C e casa vuotata di AGENTE-B.

## 18:45:23 — Fixture superate e pesi congelati
17 controlli mirati e 2484 posizioni sintetiche concordano con l'arbitro fino a due prese; 16 casi piu lunghi esclusi e dichiarati. Verificati rotazione-colore, JSON, determinismo, terminali e concorrenza. Nessuna partita giocata nei test.
La rete contiene 5640 unita e 39398 parametri. Esportata in models/Opale.C/Models/opale-c-v2.json prima dei benchmark.
Ispirazione: fixture di AGENTE-C e casa di partenza vuotata di AGENTE-B. Aggiunto adattatore HTTP dama-model/v1; build isolate per non sovrascrivere DLL degli altri agenti.

## 18:47:56 — Confronto congelato avviato
Runner separato: 12 aperture nuove di 4/6/8 semimosse, entrambi i colori, v1/v2 contro Kimberlite e Agata a temperatura zero; 96 partite previste. Snapshot e SHA256 dei quattro file dei pesi. Nessun aggiornamento dei pesi e nessuna autopartita.
Ispirazione: protocollo appaiato e controllo del sovradattamento discussi con AGENTE-A e AGENTE-B. Tutte le partite usano CompetitionStore condiviso prima della prima mossa, con verbali completi.

## 18:54:23 — Riepilogo finale: arresto anticipato richiesto dall'utente
1. AGENTE-C si ferma al minuto 24; benchmark gia concluso e servizio di verifica arrestato, nessun processo altrui interrotto.
2. Opale C: 5640 unita ReLU e 39398 parametri manuali; asimmetria del tratto e massimo tra percorsi, da discussione con AGENTE-A e AGENTE-B; nessun training o ricerca.
3. Fixture: 17 controlli mirati e 2484 posizioni sintetiche superati; protocollo HTTP e 32 chiamate concorrenti verificati; pacchetto pubblicabile creato.
4. Confronto congelato completato prima dello stop: 96 partite registrate; v2 23 vittorie/17 patte/8 sconfitte contro v1 11/8/29, su 48 partite ciascuno; pesi invariati.
5. Contro Kimberlite vittorie 9/24 -> 17/24; contro Agata 2/24 -> 6/24. Campione piccolo, non stima Elo. Prove e limiti in models/Opale.C/results/frozen-20261106/report.json e README.md.

## 19:17:36 — Ripresa autorizzata dopo lo spegnimento e chiusura
Completate soltanto le verifiche rimaste: build isolata del runner separato, audit dei verbali gia esistenti, fixture e HTTP sul pacchetto pubblicabile. Nessuna nuova partita, nessun cambiamento ai pesi o al motore giocabile, nessun messaggio nel canale chiuso.
Ispirazione: verifica su protocollo appaiato richiesta nella discussione da AGENTE-A/AGENTE-B; completamento autorizzato dall'utente. Aggiunto il comando --audit al solo runner di verifica.

### Riepilogo finale (5 righe)
1. AGENTE-C ha completato Opale C mantenendo pesi manuali, assenza di training/ottimizzatori, ricerca e memoria di partite nel motore giocabile.
2. Confronto congelato: 23 vittorie, 17 patte, 8 sconfitte contro 11/8/29 dell'originale, su 48 partite per versione; campione piccolo, nessuna stima Elo.
3. Audit superato: 96 registrazioni condivise, 7741 mosse legali e posizioni ricostruite, 48 confronti appaiati, 12 aperture distinte, hash invariati.
4. Build del runner, fixture sul pacchetto pubblicato e test HTTP con 32 richieste concorrenti superati; nessuna nuova partita giocata alla ripresa.
5. Consegna in models/Opale.C; prove in results/frozen-20261106/report.json e audit.json; servizio di verifica arrestato e nessun file o processo altrui modificato.
