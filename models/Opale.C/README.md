# Opale C — AGENTE-C

Motore neurale per la sessione di discussione/miglioramento del 6 novembre 2026.
Pesi e bias scritti per costruzione deterministica; nessun training, fine-tuning,
ottimizzatore, ricerca delle continuazioni o memoria di partite nell'inferenza.

## Risultato verificato

Nel confronto congelato, 48 partite per versione:

| Versione | Vittorie | Patte | Sconfitte |
| --- | ---: | ---: | ---: |
| Opale v1 | 11 | 8 | 29 |
| Opale C | 23 | 17 | 8 |

Contro Kimberlite le vittorie passano da 9/24 a 17/24; contro Agata da 2/24
a 6/24. Il campione e piccolo e non dimostra superiorita generale.
`results/frozen-20261106/report.json` contiene il confronto; `audit.json`
conferma tutte le 96 registrazioni condivise, 7741 mosse ricostruite,
12 aperture distinte e gli hash dei pesi. Alla ripresa dopo lo spegnimento
sono state completate solo verifiche, senza nuove partite o modifiche ai pesi.

## Cosa cambia

La rete usa 128 ingressi di occupazione e **5.640 unita ReLU**, con **39.398
parametri memorizzati**. I collegamenti possono provenire da ingressi o neuroni
precedenti. L'uscita e una combinazione lineare delle attivazioni.

I canali materiali/posizionali di Opale v1 restano invariati. I vecchi bonus di
presa vengono sostituiti da rilevatori di percorsi di una/due prese e da porte
neurali per numero di vittime e priorita del pezzo catturante dama. Una cascata
ReLU calcola il massimo del materiale catturabile: prese alternative non vengono
sommate. Materiale per vittima: pedina 100, dama 310, come nella base originale.

Il canale di chi muove pesa 0,90 del materiale catturabile, quello di chi attende
0,15: una presa immediata e obbligatoria, una minaccia successiva puo essere
evitata. Sono valori scelti a mano prima delle partite, non risultati di ricerca
automatica. Si sceglie `-Evaluate(afterstate, nextTurn)` fra le mosse ricevute.

Ispirazione: asimmetria del tratto da AGENTE-B, precisazione algebrica da AGENTE-A;
confronto comune su OR per vittima, massimo fra alternative e fixture minime.
La fixture della casa di partenza vuotata e stata proposta da AGENTE-B.

## Limiti dichiarati

- I rilevatori si fermano a due prese: sequenze piu lunghe sono approssimate.
- Il massimo del materiale non rappresenta la risposta successiva avversaria.
  Una presa non viene dichiarata sicura: sacrifici forzati piu profondi restano
  fuori dall'orizzonte.
- La ripetizione futura non e ricostruibile esattamente dai soli dati v1.
- Le fixture provano correttezza locale, non forza agonistica. I confronti
  appaiati usano avversari e pesi congelati; i risultati non sono stime Elo.
- Il registro comune puo bloccare l'avvio delle partite. Il runner aspetta la
  registrazione, senza giocare incontri non registrati.

## Comandi dalla radice del repository

Usare output di build isolati per non sovrascrivere DLL in uso da altri agenti:

```powershell
dotnet build models/Opale.C -c Release --artifacts-path models/Opale.C/artifacts/service-build -p:UseAppHost=false
dotnet models/Opale.C/artifacts/service-build/bin/Opale.C/release/Opale.C.dll --test
dotnet models/Opale.C/artifacts/service-build/bin/Opale.C/release/Opale.C.dll --export models/Opale.C/Models/opale-c-v3.json
dotnet models/Opale.C/artifacts/service-build/bin/Opale.C/release/Opale.C.dll --weights models/Opale.C/Models/opale-c-v3.json --urls http://127.0.0.1:9014
python models/Opale.C/verify_http.py http://127.0.0.1:9014
```

Il servizio espone `GET /health` e `POST /choose-move`, protocollo `dama-model/v1`.
Valida input e limiti, conserva gli ID opachi, supporta richieste concorrenti e
cancellazione. La configurazione condivisa del sito non e stata modificata.

Il runner separato dipende dagli avversari solo per la misura, non per il servizio:

```powershell
dotnet run --project models/Opale.C/Evaluation -c Release --artifacts-path models/Opale.C/artifacts/eval-build -- models/Opale.C/results/NEW-RUN FUTURE-UTC-DEADLINE
```

Audit senza nuove partite, sulla misura gia eseguita:

```powershell
dotnet run --project models/Opale.C/Evaluation -c Release --artifacts-path models/Opale.C/artifacts/eval-build -- --audit models/Opale.C/results/frozen-20261106
```

Una sola misura congelata: 12 aperture di 4/6/8 semimosse, colori appaiati,
Opale v1 e Opale C contro Kimberlite e Agata (temperatura zero), 96 partite.
Gli ID di classifica restano `opale`, `kimberlite`, `agata`; i verbali separati
identificano le versioni tramite SHA256. Incomplete/interrotte non danno punti.

Le fixture `--test` e le richieste HTTP sintetiche non giocano partite. Verificano
pedina/dama, alternative, doppia, promozione, priorita, rotazione-colore,
determinismo, JSON, concorrenza e il caso della casa vuotata. I pesi congelati
hanno SHA256 `47905feda728fb39c07e8c3a23ff903b422ca0189bcc1d0b4c2a4461db6318b8`.

Piano e registro delle decisioni: `arena/discussione/piano_AGENTE-C.md` e
`arena/discussione/changelog_AGENTE-C.md`.

## Revisione manual-v3, sessione comune

La misura storica sopra riguarda v2. Il costruttore corrente produce v3:
128 input di sola scacchiera, 5648 unita e 39484 parametri. Aggiunge otto unita
per riconoscere una promozione fra i percorsi di presa gia ammessi dalle
precedenze. Il guadagno e 310-100=210, conteggiato una sola volta anche con piu
alternative; coefficienti immediato .90 e differito .15 invariati. Non introduce
storia, casualita, ricerca o ottimizzazione dei pesi.

SHA256 v3: `3e25fd78df09920a87f2b0c6cdcc53084c5dcfbf7747dd64363a314fe8dfc1e9`.
Le fixture comprendono ora 22 controlli mirati e 2484 posizioni sintetiche;
16 posizioni con prese oltre due vittime restano escluse dall'invariante.
Il protocollo HTTP e stato verificato con ID opachi, input malformati e
32 richieste concorrenti. Nessuna di queste verifiche gioca partite.

La diagnosi comune v2 ha dato 3 vittorie,6 patte,3 sconfitte per ciascuno dei
tre motori: e un campione distinto dalla misura storica. La conferma v3 usa
il solo runner coordinato; risultati e limiti in `arena/coordinamento/risultati`.
Il ciclo di dame documentato nel piano non e risolto da questa revisione.

Conferma comune conclusa: Opale v3 40 vittorie,107 patte,48 sconfitte e
1 interruzione;187 punti, secondo dietro Agata v9 (229) e davanti Klinamite
v3 (162). Tutti i 294 incontri previsti sono stati avviati:289 esiti naturali,
5 interruzioni a240 semimosse senza punti. Audit indipendente di34629 mosse,
147 aperture appaiate e tutti i record/hash/seed:
`arena/coordinamento/audit_AGENTE-C.json`.
Questo girone non isola il miglioramento v2-v3: sono cambiati anche rivali e
aperture. La correzione e dimostrata dalle fixture, non da un aumento misurato
del win rate rispetto al baseline.

## Riferimento esterno: Kingsrow, 7 novembre 2026

Opale C v3 invariato ha perso tutte le 18 partite contro Kingsrow Italian
1.19e, nove per colore, senza patte o interruzioni. Kingsrow cercava per
200 ms e usava il libro incluso; è un confronto esterno con vincoli diversi,
non un aggiornamento dei pesi o un miglioramento di Opale.
[Protocollo, traiettorie e audit](../../arena/kingsrow/2026-11-07/resoconto.md).
