# DbMetaTool (SenteProject)

## Funkcjonalność

Aplikacja posiada trzy komendy:

1. **build-db**

Buduje nową bazę danych Firebird na podstawie wcześniej wygenerowanych skryptów.

Parametry:

- `--db-dir` – katalog, w którym ma powstać plik bazy (`meta.fdb`),
- `--scripts-dir` – katalog ze skryptami `.sql` (domeny, tabele, procedury).

2. **export-scripts**

Eksportuje metadane z istniejącej bazy Firebird do plików `.sql`.

Parametry:

- `--connection-string` – connection string do istniejącej bazy,
- `--output-dir` – katalog, do którego zostaną zapisane skrypty.

3. **update-db**

Aktualizuje istniejącą bazę danych wykonując skrypty `.sql` z katalogu.

Parametry:

- `--connection-string` – connection string do istniejącej bazy,
- `--scripts-dir` – katalog ze skryptami `.sql`.

Zgodnie z treścią zadania obsługiwane są tylko:

- **domeny**,
- **tabele (z kolumnami)**,
- **procedury składowane**.

Pozostałe są pominięte.

---

## Wymagania

- .NET 8.0 SDK
- Firebird 5.0 (serwer)
- 32-bitowa biblioteka klienta Firebird (`fbclient.dll`) – wymagana przez IBExpert i dostawcę ADO.NET
- IBExpert (opcjonalnie, ale użyte do tworzenia i weryfikacji bazy testowej)

---

## Sposób użycia (przykład na podstawie mojej bazy)

# Export scripts
dotnet run -- export-scripts \
  --connection-string "Database=localhost:E:\database\src.fdb;User=sysdba;Password=masterkey;Dialect=3;ServerType=0;" \
  --output-dir "E:\database\scripts"

# Build new database from scripts
dotnet run -- build-db \
  --db-dir "E:\database\newdb" \
  --scripts-dir "E:\database\scripts"

# Update existing database from scripts
dotnet run -- update-db \
  --connection-string "Database=localhost:E:\database\newdb\meta.fdb;User=sysdba;Password=masterkey;Dialect=3;ServerType=0;" \
  --scripts-dir "E:\database\scripts"

  --connection-string "Database=localhost:E:\database\src.fdb;User=sysdba;Password=masterkey;Dialect=3;ServerType=0;" \
  --output-dir "E:\database\scripts"
