# Runner effettivamente utilizzato

Pacchetto gestito del runner v6 conservato per ripetere i comandi documentati. Richiede runtime .NET 10 compatibile e servizi dei giocatori separati. Non contiene Kingsrow.

Esecuzione: `dotnet Dama.Challenge.dll CONFIG OUTPUT`; audit: `dotnet Dama.Challenge.dll --audit OUTPUT`. Ogni nuova esecuzione deve creare nuovi ID e registrare tutte le partite nel registro condiviso.

Il sorgente corrente del runner e successivo alla compilazione di questo binario. Non e stato trovato uno snapshot esatto del sorgente v6; non si attesta quindi una ricompilazione equivalente dal sorgente attuale. Non dedurre timeout o comportamento del runner congelato dal sorgente successivo.

Il manifest documenta i file copiati ora, compresi gli hash delle dipendenze: questi ultimi non dimostrano quali dipendenze fossero state caricate storicamente. Gli hash del motore Ametista e i suoi snapshot restano documentati separatamente.
