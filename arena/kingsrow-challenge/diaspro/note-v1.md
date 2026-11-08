# Diaspro v1 — note di sviluppo

- Checkpoint SHA256: `92eb992962bb74d08d9b08295a41692c32d1c38de16e42b65fbc2a565f2e2d50`.
- Nove test superati prima del girone; fixture HTTP verificate sul servizio corretto soltanto dopo lo spostamento da 9016 a 9017.
- Il primo tentativo su 9016 raggiunse Ametista: nessuna partita creata e nessun esito attribuito a Diaspro.
- Il mapping della risposta fra indici dell'arbitro e indici HTTP fu corretto dopo una fixture di presa con HTTP 500; nessuna partita era ancora iniziata.
- Girone v1: seed 31001, 18 partite, origine `test`, Kingsrow 9026, 1000 ms effettivi per entrambi. Versioni e pesi non cambiano durante la misura.
- La build completa non è riuscita con i servizi attivi perché Windows blocca DLL/exe di Kingsrow e Diaspro. I progetti nuovi compilano e i test Diaspro passano; la verifica completa va ripetuta a servizi fermi o su output separato.
