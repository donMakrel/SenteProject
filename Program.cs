using FirebirdSql.Data.FirebirdClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.IO;
using System.Text;

namespace DbMetaTool
{
    public static class Program
    {
        // Przykładowe wywołania:
        // DbMetaTool build-db --db-dir "C:\db\fb5" --scripts-dir "C:\scripts"
        // DbMetaTool export-scripts --connection-string "..." --output-dir "C:\out"
        // DbMetaTool update-db --connection-string "..." --scripts-dir "C:\scripts"

        private const string DefaultUser = "sysdba";
        private const string DefaultPassword = "masterkey";
        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Użycie:");
                Console.WriteLine("  build-db --db-dir <ścieżka> --scripts-dir <ścieżka>");
                Console.WriteLine("  export-scripts --connection-string <connStr> --output-dir <ścieżka>");
                Console.WriteLine("  update-db --connection-string <connStr> --scripts-dir <ścieżka>");
                return 1;
            }

            try
            {
                var command = args[0].ToLowerInvariant();

                switch (command)
                {
                    case "build-db":
                        {
                            string dbDir = GetArgValue(args, "--db-dir");
                            string scriptsDir = GetArgValue(args, "--scripts-dir");

                            BuildDatabase(dbDir, scriptsDir);
                            Console.WriteLine("Baza danych została zbudowana pomyślnie.");
                            return 0;
                        }

                    case "export-scripts":
                        {
                            string connStr = GetArgValue(args, "--connection-string");
                            string outputDir = GetArgValue(args, "--output-dir");

                            ExportScripts(connStr, outputDir);
                            Console.WriteLine("Skrypty zostały wyeksportowane pomyślnie.");
                            return 0;
                        }

                    case "update-db":
                        {
                            string connStr = GetArgValue(args, "--connection-string");
                            string scriptsDir = GetArgValue(args, "--scripts-dir");

                            UpdateDatabase(connStr, scriptsDir);
                            Console.WriteLine("Baza danych została zaktualizowana pomyślnie.");
                            return 0;
                        }

                    default:
                        Console.WriteLine($"Nieznane polecenie: {command}");
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Błąd: " + ex.Message);
                return -1;
            }
        }

        private static string GetArgValue(string[] args, string name)
        {
            int idx = Array.IndexOf(args, name);
            if (idx == -1 || idx + 1 >= args.Length)
                throw new ArgumentException($"Brak wymaganego parametru {name}");
            return args[idx + 1];
        }

        /// <summary>
        /// Buduje nową bazę danych Firebird 5.0 na podstawie skryptów.
        /// </summary>
        public static void BuildDatabase(string databaseDirectory, string scriptsDirectory)
        {
            if (string.IsNullOrWhiteSpace(databaseDirectory))
                throw new ArgumentException("Podaj katalog bazy danych.", nameof(databaseDirectory));

            if (string.IsNullOrWhiteSpace(scriptsDirectory))
                throw new ArgumentException("Podaj katalog skryptów.", nameof(scriptsDirectory));

            if (!Directory.Exists(scriptsDirectory))
                throw new DirectoryNotFoundException($"Nie znaleziono katalogu skryptów: {scriptsDirectory}");

            Directory.CreateDirectory(databaseDirectory);

            var dbPath = Path.Combine(databaseDirectory, "meta.fdb");

            if (File.Exists(dbPath))
            {
                Console.WriteLine($"Uwaga: plik bazy {dbPath} już istnieje – zostanie usunięty.");
                File.Delete(dbPath);
            }

            var csb = new FbConnectionStringBuilder
            {
                DataSource = "localhost",          // serwer (tak jak w src.fdb)
                Database = dbPath,              // pełna ścieżka do meta.fdb
                UserID = DefaultUser,         // SYSDBA
                Password = DefaultPassword,     // masterkey
                ServerType = FbServerType.Default,
                Dialect = 3,
                Charset = "NONE"               // żeby nie było problemów jak przy WIN1250
            };


            Console.WriteLine($"Tworzenie nowej bazy danych: {dbPath}");
            FbConnection.CreateDatabase(csb.ToString(), pageSize: 16384, forcedWrites: true);

            Console.WriteLine("Wykonywanie skryptów z katalogu: " + scriptsDirectory);
            ExecuteScriptsAgainstDatabase(csb.ToString(), scriptsDirectory);

            Console.WriteLine("Budowa bazy danych zakończona.");
        }

        /// <summary>
        /// Generuje skrypty metadanych z istniejącej bazy danych Firebird 5.0.
        /// </summary>
        public static void ExportScripts(string connectionString, string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Podaj connection string.", nameof(connectionString));

            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("Podaj katalog wyjściowy.", nameof(outputDirectory));

            Directory.CreateDirectory(outputDirectory);

            using var connection = new FbConnection(connectionString);
            connection.Open();

            Console.WriteLine("Połączono z bazą. Eksport domen...");
            ExportDomains(connection, outputDirectory);

            Console.WriteLine("Eksport tabel...");
            ExportTables(connection, outputDirectory);

            Console.WriteLine("Eksport procedur...");
            ExportProcedures(connection, outputDirectory);

            Console.WriteLine("Eksport metadanych zakończony.");
        }

        /// <summary>
        /// Aktualizuje istniejącą bazę danych Firebird 5.0 na podstawie skryptów.
        /// </summary>
        public static void UpdateDatabase(string connectionString, string scriptsDirectory)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Podaj connection string.", nameof(connectionString));

            if (string.IsNullOrWhiteSpace(scriptsDirectory))
                throw new ArgumentException("Podaj katalog skryptów.", nameof(scriptsDirectory));

            if (!Directory.Exists(scriptsDirectory))
                throw new DirectoryNotFoundException($"Nie znaleziono katalogu skryptów: {scriptsDirectory}");

            Console.WriteLine("Aktualizacja bazy na podstawie skryptów...");
            ExecuteScriptsAgainstDatabase(connectionString, scriptsDirectory);
        }

        #region Helpers – wykonywanie skryptów

        private static void ExecuteScriptsAgainstDatabase(string connectionString, string scriptsDirectory)
        {
            var sqlFiles = Directory.GetFiles(scriptsDirectory, "*.sql", SearchOption.TopDirectoryOnly)
                                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                    .ToList();

            if (sqlFiles.Count == 0)
            {
                Console.WriteLine($"Brak plików .sql w katalogu {scriptsDirectory}");
                return;
            }

            using var connection = new FbConnection(connectionString);
            connection.Open();

            foreach (var file in sqlFiles)
            {
                var scriptText = File.ReadAllText(file, Encoding.UTF8);

                if (string.IsNullOrWhiteSpace(scriptText))
                    continue;

                Console.WriteLine($"Wykonywanie skryptu: {Path.GetFileName(file)}");

                using var cmd = new FbCommand(scriptText, connection)
                {
                    CommandType = CommandType.Text
                };

                try
                {
                    cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Błąd w pliku {Path.GetFileName(file)}: {ex.Message}");
                    throw;
                }
            }
        }

        #endregion

        #region Helpers – eksport domen, tabel, procedur

        private static void ExportDomains(FbConnection connection, string outputDirectory)
        {
            const string sql = @"
SELECT
    f.RDB$FIELD_NAME,
    f.RDB$FIELD_TYPE,
    f.RDB$FIELD_SUB_TYPE,
    f.RDB$FIELD_LENGTH,
    f.RDB$FIELD_PRECISION,
    f.RDB$FIELD_SCALE,
    f.RDB$DEFAULT_SOURCE,
    f.RDB$VALIDATION_SOURCE,
    f.RDB$NULL_FLAG
FROM RDB$FIELDS f
WHERE f.RDB$SYSTEM_FLAG = 0
ORDER BY f.RDB$FIELD_NAME";

            using var cmd = new FbCommand(sql, connection);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                string name = reader.GetString(0).Trim();

                if (name.StartsWith("RDB$", StringComparison.OrdinalIgnoreCase))
                    continue;

                short fieldType = reader.IsDBNull(1) ? (short)0 : reader.GetInt16(1);
                short fieldSubType = reader.IsDBNull(2) ? (short)0 : reader.GetInt16(2);
                short fieldLength = reader.IsDBNull(3) ? (short)0 : reader.GetInt16(3);
                short fieldPrecision = reader.IsDBNull(4) ? (short)0 : reader.GetInt16(4);
                short fieldScale = reader.IsDBNull(5) ? (short)0 : reader.GetInt16(5);
                string defaultSource = reader.IsDBNull(6) ? null : reader.GetString(6)?.Trim();
                string validationSource = reader.IsDBNull(7) ? null : reader.GetString(7)?.Trim();
                bool notNull = !reader.IsDBNull(8) && reader.GetInt16(8) == 1;

                string typeSql = MapFieldType(fieldType, fieldSubType, fieldLength, fieldPrecision, fieldScale);

                var sb = new StringBuilder();
                sb.Append("CREATE DOMAIN \"")
                  .Append(name)
                  .Append("\" AS ")
                  .Append(typeSql);

                if (!string.IsNullOrEmpty(defaultSource))
                {
                    sb.Append(" ").Append(defaultSource);
                }

                if (notNull)
                {
                    sb.Append(" NOT NULL");
                }

                if (!string.IsNullOrEmpty(validationSource))
                {
                    sb.Append(" ").Append(validationSource);
                }

                sb.Append(";").Append(Environment.NewLine);

                var fileName = Path.Combine(outputDirectory, $"01_domain_{name}.sql");
                File.WriteAllText(fileName, sb.ToString(), Encoding.UTF8);
            }
        }

        private sealed class ColumnInfo
        {
            public string ColumnName { get; set; } = "";
            public string DomainOrTypeSql { get; set; } = "";
            public bool NotNull { get; set; }
            public string DefaultSource { get; set; }
        }

        private static void ExportTables(FbConnection connection, string outputDirectory)
        {
            const string sql = @"
SELECT
    TRIM(r.RDB$RELATION_NAME) AS TABLE_NAME,
    TRIM(rf.RDB$FIELD_NAME)   AS COLUMN_NAME,
    TRIM(rf.RDB$FIELD_SOURCE) AS FIELD_SOURCE,
    rf.RDB$NULL_FLAG,
    rf.RDB$DEFAULT_SOURCE,
    fld.RDB$FIELD_TYPE,
    fld.RDB$FIELD_SUB_TYPE,
    fld.RDB$FIELD_LENGTH,
    fld.RDB$FIELD_PRECISION,
    fld.RDB$FIELD_SCALE,
    rf.RDB$FIELD_POSITION
FROM RDB$RELATIONS r
JOIN RDB$RELATION_FIELDS rf ON rf.RDB$RELATION_NAME = r.RDB$RELATION_NAME
JOIN RDB$FIELDS fld          ON fld.RDB$FIELD_NAME    = rf.RDB$FIELD_SOURCE
WHERE r.RDB$SYSTEM_FLAG = 0
ORDER BY TABLE_NAME, rf.RDB$FIELD_POSITION";

            using var cmd = new FbCommand(sql, connection);
            using var reader = cmd.ExecuteReader();

            var tables = new Dictionary<string, List<ColumnInfo>>(StringComparer.OrdinalIgnoreCase);

            while (reader.Read())
            {
                string tableName = reader.GetString(0).Trim();
                string columnName = reader.GetString(1).Trim();
                string fieldSource = reader.GetString(2).Trim();

                bool notNull = !reader.IsDBNull(3) && reader.GetInt16(3) == 1;
                string defaultSource = reader.IsDBNull(4) ? null : reader.GetString(4)?.Trim();

                short fieldType = reader.IsDBNull(5) ? (short)0 : reader.GetInt16(5);
                short fieldSubType = reader.IsDBNull(6) ? (short)0 : reader.GetInt16(6);
                short fieldLength = reader.IsDBNull(7) ? (short)0 : reader.GetInt16(7);
                short fieldPrecision = reader.IsDBNull(8) ? (short)0 : reader.GetInt16(8);
                short fieldScale = reader.IsDBNull(9) ? (short)0 : reader.GetInt16(9);

                string domainOrTypeSql;
                if (IsUserDomain(connection, fieldSource))
                {
                    domainOrTypeSql = $"\"{fieldSource}\"";
                }
                else
                {
                    domainOrTypeSql = MapFieldType(fieldType, fieldSubType, fieldLength, fieldPrecision, fieldScale);
                }

                if (!tables.TryGetValue(tableName, out var cols))
                {
                    cols = new List<ColumnInfo>();
                    tables[tableName] = cols;
                }

                cols.Add(new ColumnInfo
                {
                    ColumnName = columnName,
                    DomainOrTypeSql = domainOrTypeSql,
                    NotNull = notNull,
                    DefaultSource = defaultSource
                });
            }

            foreach (var kvp in tables)
            {
                string tableName = kvp.Key;
                var cols = kvp.Value;

                var sb = new StringBuilder();
                sb.Append("CREATE TABLE \"").Append(tableName).AppendLine("\" (");

                for (int i = 0; i < cols.Count; i++)
                {
                    var c = cols[i];
                    sb.Append("    \"").Append(c.ColumnName).Append("\" ")
                      .Append(c.DomainOrTypeSql);

                    if (!string.IsNullOrEmpty(c.DefaultSource))
                    {
                        sb.Append(" ").Append(c.DefaultSource);
                    }

                    if (c.NotNull)
                    {
                        sb.Append(" NOT NULL");
                    }

                    if (i < cols.Count - 1)
                        sb.Append(",");
                    sb.AppendLine();
                }

                sb.AppendLine(");");

                var fileName = Path.Combine(outputDirectory, $"02_table_{tableName}.sql");
                File.WriteAllText(fileName, sb.ToString(), Encoding.UTF8);
            }
        }

        private static void ExportProcedures(FbConnection connection, string outputDirectory)
        {
            const string procSql = @"
SELECT
    TRIM(p.RDB$PROCEDURE_NAME) AS PROCEDURE_NAME,
    p.RDB$PROCEDURE_SOURCE
FROM RDB$PROCEDURES p
WHERE p.RDB$SYSTEM_FLAG = 0
ORDER BY PROCEDURE_NAME";

            const string paramSql = @"
SELECT
    TRIM(pp.RDB$PROCEDURE_NAME) AS PROCEDURE_NAME,
    TRIM(pp.RDB$PARAMETER_NAME) AS PARAMETER_NAME,
    pp.RDB$PARAMETER_TYPE, -- 0 = input, 1 = output
    pp.RDB$PARAMETER_NUMBER,
    fld.RDB$FIELD_TYPE,
    fld.RDB$FIELD_SUB_TYPE,
    fld.RDB$FIELD_LENGTH,
    fld.RDB$FIELD_PRECISION,
    fld.RDB$FIELD_SCALE
FROM RDB$PROCEDURE_PARAMETERS pp
JOIN RDB$FIELDS fld ON fld.RDB$FIELD_NAME = pp.RDB$FIELD_SOURCE
WHERE pp.RDB$SYSTEM_FLAG = 0
ORDER BY PROCEDURE_NAME, pp.RDB$PARAMETER_TYPE, pp.RDB$PARAMETER_NUMBER";

            var parameters = new Dictionary<string, (List<string> inputs, List<string> outputs)>(StringComparer.OrdinalIgnoreCase);

            using (var cmdParams = new FbCommand(paramSql, connection))
            using (var reader = cmdParams.ExecuteReader())
            {
                while (reader.Read())
                {
                    string procName = reader.GetString(0).Trim();
                    string paramName = reader.GetString(1).Trim();
                    short paramType = reader.GetInt16(2);

                    short fieldType = reader.IsDBNull(4) ? (short)0 : reader.GetInt16(4);
                    short fieldSubType = reader.IsDBNull(5) ? (short)0 : reader.GetInt16(5);
                    short fieldLength = reader.IsDBNull(6) ? (short)0 : reader.GetInt16(6);
                    short fieldPrecision = reader.IsDBNull(7) ? (short)0 : reader.GetInt16(7);
                    short fieldScale = reader.IsDBNull(8) ? (short)0 : reader.GetInt16(8);

                    string typeSql = MapFieldType(fieldType, fieldSubType, fieldLength, fieldPrecision, fieldScale);
                    string paramDef = $"\"{paramName}\" {typeSql}";

                    if (!parameters.TryGetValue(procName, out var lists))
                    {
                        lists = (new List<string>(), new List<string>());
                        parameters[procName] = lists;
                    }

                    if (paramType == 0)
                        lists.inputs.Add(paramDef);
                    else
                        lists.outputs.Add(paramDef);
                }
            }

            using var cmd = new FbCommand(procSql, connection);
            using var procReader = cmd.ExecuteReader();

            while (procReader.Read())
            {
                string procName = procReader.GetString(0).Trim();
                string source = procReader.IsDBNull(1) ? null : procReader.GetString(1);

                string body = source?.Trim() ?? string.Empty;

                string header;
                if (body.StartsWith("CREATE", StringComparison.OrdinalIgnoreCase) ||
                    body.StartsWith("ALTER", StringComparison.OrdinalIgnoreCase))
                {
                    header = body;
                }
                else
                {
                    parameters.TryGetValue(procName, out var lists);
                    var inputs = lists.inputs ?? new List<string>();
                    var outputs = lists.outputs ?? new List<string>();

                    var sbHeader = new StringBuilder();
                    sbHeader.Append("CREATE OR ALTER PROCEDURE \"").Append(procName).Append("\"");

                    if (inputs.Count > 0)
                    {
                        sbHeader.AppendLine();
                        sbHeader.Append("    (")
                                .Append(string.Join(", ", inputs))
                                .Append(")");
                    }

                    if (outputs.Count > 0)
                    {
                        sbHeader.AppendLine();
                        sbHeader.Append("RETURNS (")
                                .Append(string.Join(", ", outputs))
                                .Append(")");
                    }

                    sbHeader.AppendLine();
                    sbHeader.AppendLine("AS");
                    sbHeader.AppendLine(body);

                    header = sbHeader.ToString();
                }

                var fullSql = header.TrimEnd();
                if (!fullSql.EndsWith(";"))
                    fullSql += ";";

                var fileName = Path.Combine(outputDirectory, $"03_procedure_{procName}.sql");
                File.WriteAllText(fileName, fullSql + Environment.NewLine, Encoding.UTF8);
            }
        }

        #endregion

        #region Helpers – mapowanie typów / domeny

        private static bool IsUserDomain(FbConnection connection, string domainName)
        {
            const string sql = @"
SELECT COUNT(*)
FROM RDB$FIELDS f
WHERE f.RDB$SYSTEM_FLAG = 0
  AND TRIM(f.RDB$FIELD_NAME) = @NAME";

            using var cmd = new FbCommand(sql, connection);
            cmd.Parameters.AddWithValue("@NAME", domainName);
            var count = Convert.ToInt32(cmd.ExecuteScalar());
            return count > 0;
        }

        private static string MapFieldType(short fieldType, short fieldSubType, short length, short precision, short scale)
        {
            switch (fieldType)
            {
                case 7:
                    return "SMALLINT";
                case 8:
                    return "INTEGER";
                case 16:
                    if (fieldSubType == 1 || fieldSubType == 2)
                    {
                        var prec = precision != 0 ? precision : (short)18;
                        var sc = scale < 0 ? -scale : scale;
                        return $"NUMERIC({prec},{sc})";
                    }
                    return "BIGINT";
                case 10:
                    return "FLOAT";
                case 27:
                    return "DOUBLE PRECISION";
                case 14:
                    return $"CHAR({length})";
                case 37:
                    return $"VARCHAR({length})";
                case 12:
                    return "DATE";
                case 13:
                    return "TIME";
                case 35:
                    return "TIMESTAMP";
                case 261:
                    return "BLOB";
                default:
                    return "BLOB";
            }
        }

        #endregion
    }
}
