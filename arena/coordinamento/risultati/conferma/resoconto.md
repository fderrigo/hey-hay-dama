# Resoconto finale della sessione comune

Calendario eseguito: 294/294 partite, 98 per coppia, seed 149043909. Esiti naturali: 289; interruzioni a 240 semimosse: 5; errori di modello: 0. Il verbale resta `INCOMPLETO` perché le cinque interruzioni non sono state convertite in patte.

| Motore | Versione | Vittorie | Patte | Sconfitte | Interrotte | Punti 2/1/0 |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Agata | v9 | 69 | 91 | 31 | 5 | 229 |
| Opale C | manual-v3 | 40 | 107 | 48 | 1 | 187 |
| Klinamite | v3 | 30 | 102 | 60 | 4 | 162 |

Risultati di coppia, dal punto di vista del primo motore: Klinamite–Agata 15/43/36 con 4 interrotte; Klinamite–Opale 15/59/24; Agata–Opale 33/48/16 con 1 interrotta.

## Modifiche distinte

- Klinamite v3: unico motore con memoria; penalità manuale -5 al ritorno quieto immediato di dama, solo in vantaggio materiale, con fallback neutro per ID opachi. 16 test e fixture HTTP superati.
- Agata v9: rete satura stocastica senza storia; correzioni manuali a promozione e valore delle vittime in presa. 46 test superati secondo il manifest B.
- Opale manual-v3: rete deterministica senza storia, massimo dei percorsi con precedenze; guadagno di promozione nei percorsi già presenti. 22 controlli secondo il manifest C.

## Audit e limiti

Audit C: 294 ID unici verificati nel registro, 34.629 mosse ricostruite, tutte le scacchiere, turni ed esiti conformi; 147 coppie con colori invertiti e stesse aperture; seed e hash verificati. Anche il controllo A non ha trovato errori di hash, colori o aperture.

Agata v9 vince questo girone, con margine moderato e senza dimostrare superiorità definitiva. Analisi B sulle coppie complete: score 61,1% contro Klinamite (IC95 54,2–68,0; 45 coppie) e 58,3% contro Opale (IC95 51,6–65,1; 48 coppie). Le coppie con una partita interrotta sono escluse. Nessun Elo calcolato.

Non è dimostrato che v3 superi v2 per Klinamite o Opale, né che Agata v9 superi v7 sugli stessi rivali finali: servono confronti appaiati separati. Klinamite conserva 96 patte per ripetizione, 36 patte in vantaggio e quattro interruzioni; la correzione mirata non ha prodotto la classifica migliore.

La misura supplementare di B resta separata: usava ID partecipanti non stabili e rivali diversi da quelli finali. Non viene aggregata a questo verbale.
