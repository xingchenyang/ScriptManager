using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ScriptManager
{
    internal sealed class ScriptManager
    {
        private const string HistoryTable = "dbo.HistoriqueScriptSql";
        private readonly RunnerOptions options;

        internal ScriptManager(RunnerOptions options)
        {
            this.options = options;
        }

        internal int Run()
        {
            Log.Info(string.Empty);
            Log.Info(string.Empty);
            Log.Info("---------------------------------------------------");
            string connectionString = ResolveConnectionString();
            Log.Info("Using the following connection string: ");
            Log.Info(connectionString);
            if (!Directory.Exists(options.SqlPath))
                throw new DirectoryNotFoundException(options.SqlPath);

            if (!options.DisableScriptDiff)
                EnsureHistoryTable(connectionString);

            var previousScripts = options.DisableScriptDiff
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : ReadHistory(connectionString);
            var files = FindSqlFiles(options.SqlPath)
                .Where(file => !previousScripts.Contains(HistoryPath(file)))
                .ToList();

            int errors = 0;
            foreach (string file in files)
            {
                string error = string.Empty;
                Log.Info("RUN SCRIPT " + file);
                try
                {
                    string sql = ReadSqlFile(file);
                    using (var connection = new SqlConnection(connectionString))
                    {
                        connection.Open();
                        foreach (string batch in SplitSqlBatches(sql))
                        {
                            using (var command = new SqlCommand(batch, connection))
                            {
                                command.CommandTimeout = 2592000;
                                command.ExecuteNonQuery();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors++;
                    error = Log.FormatException(ex);
                    Log.Error("ERROR running script " + file, ex);
                }

                if (!options.DisableScriptDiff)
                    InsertHistory(connectionString, HistoryPath(file), error);
            }

            Log.Info(string.Format(
                "FINISHED : {0} scripts run, {1} success and {2} errors",
                files.Count,
                files.Count - errors,
                errors));

            if (!string.IsNullOrEmpty(options.Version))
            {
                try
                {
                    UpdateVersion(connectionString, options.Version);
                }
                catch (Exception ex)
                {
                    Log.Error("ERROR updating version number information:", ex);
                }
            }

            return 0;
        }

        private string ResolveConnectionString()
        {
            string value = null;
            if (!string.IsNullOrEmpty(options.ConnectionStringFile))
            {
                var document = XDocument.Load(options.ConnectionStringFile);
                var element = document.Root.Elements("add")
                    .FirstOrDefault(item => (string)item.Attribute("name") == options.ConnectionStringName);
                value = (string)element?.Attribute("connectionString");
            }
            else
            {
                value = ConfigurationManager.ConnectionStrings[options.ConnectionStringName]?.ConnectionString;
            }

            if (string.IsNullOrWhiteSpace(value))
                throw new ConfigurationErrorsException("Connection string not found: " + options.ConnectionStringName);
            if (value.Contains("provider connection string=\""))
                value = value.Split(new[] { '\"' }, StringSplitOptions.None)[1];
            return value;
        }

        private IEnumerable<string> FindSqlFiles(string directory)
        {
            foreach (string file in Directory.GetFiles(directory, "*.sql").OrderBy(path => path))
                if (IsAllowedForEnvironment(file))
                    yield return Normalize(file);
            foreach (string child in Directory.GetDirectories(directory).OrderBy(path => path))
                foreach (string file in FindSqlFiles(child))
                    yield return file;
        }

        private bool IsAllowedForEnvironment(string file)
        {
            if (!file.Contains("="))
                return true;
            var match = Regex.Match(Path.GetFileName(file), "=(.*)=");
            string environment = NormalizeEnvironmentName(
                options.EnvironmentName ?? ConfigurationManager.AppSettings["EnvironmentName"] ?? string.Empty);
            string fileEnvironment = match.Groups[1].Value;
            return environment == fileEnvironment || environment.StartsWith(fileEnvironment + "-");
        }

        private static string NormalizeEnvironmentName(string environment)
        {
            return environment
                .Replace("/", "-")
                .Replace("\\", "-")
                .Replace("?", "-")
                .Replace(":", "-")
                .Replace("*", "-")
                .Replace("\"", "-")
                .Replace("<", "-")
                .Replace(">", "-");
        }

        private string HistoryPath(string file)
        {
            string root = Normalize(Path.GetFullPath(options.SqlPath)).TrimEnd('/') + "/";
            string full = Normalize(Path.GetFullPath(file));
            return "SQL/" + full.Substring(root.Length);
        }

        private static string Normalize(string path) { return path.Replace('\\', '/'); }

        internal static string ReadSqlFile(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return new UTF8Encoding(false, true).GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                // Historical Agendis scripts include Windows-1252 files. Keep them runnable
                // without forcing a repository-wide conversion to UTF-8 with BOM.
                return Encoding.GetEncoding(1252).GetString(bytes);
            }
        }

        internal static IReadOnlyList<string> SplitSqlBatches(string script)
        {
            var batches = new List<string>();
            var current = new System.Text.StringBuilder();
            bool inString = false;
            bool inBracketIdentifier = false;
            bool inQuotedIdentifier = false;
            int blockCommentDepth = 0;

            using (var reader = new StringReader(script ?? string.Empty))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    int repeatCount;
                    if (!inString && !inBracketIdentifier && !inQuotedIdentifier && blockCommentDepth == 0 &&
                        TryReadGoSeparator(line, out repeatCount))
                    {
                        string batch = current.ToString().Trim();
                        if (batch.Length > 0)
                            for (int repeat = 0; repeat < repeatCount; repeat++)
                                batches.Add(batch);
                        current.Clear();
                        continue;
                    }

                    current.AppendLine(line);
                    UpdateSqlLexicalState(
                        line,
                        ref inString,
                        ref inBracketIdentifier,
                        ref inQuotedIdentifier,
                        ref blockCommentDepth);
                }
            }

            string finalBatch = current.ToString().Trim();
            if (finalBatch.Length > 0)
                batches.Add(finalBatch);
            return batches;
        }

        private static bool TryReadGoSeparator(string line, out int repeatCount)
        {
            var match = Regex.Match(line, @"^\s*GO(?:\s+(\d+))?\s*(?:--.*)?$", RegexOptions.IgnoreCase);
            repeatCount = 1;
            if (!match.Success)
                return false;
            if (match.Groups[1].Success &&
                (!int.TryParse(match.Groups[1].Value, out repeatCount) || repeatCount < 1))
                throw new InvalidOperationException("Invalid GO repeat count: " + line);
            return true;
        }

        private static void UpdateSqlLexicalState(
            string line,
            ref bool inString,
            ref bool inBracketIdentifier,
            ref bool inQuotedIdentifier,
            ref int blockCommentDepth)
        {
            for (int index = 0; index < line.Length; index++)
            {
                char current = line[index];
                char next = index + 1 < line.Length ? line[index + 1] : '\0';

                if (blockCommentDepth > 0)
                {
                    if (current == '/' && next == '*') { blockCommentDepth++; index++; }
                    else if (current == '*' && next == '/') { blockCommentDepth--; index++; }
                    continue;
                }
                if (inString)
                {
                    if (current == '\'' && next == '\'') index++;
                    else if (current == '\'') inString = false;
                    continue;
                }
                if (inBracketIdentifier)
                {
                    if (current == ']' && next == ']') index++;
                    else if (current == ']') inBracketIdentifier = false;
                    continue;
                }
                if (inQuotedIdentifier)
                {
                    if (current == '"' && next == '"') index++;
                    else if (current == '"') inQuotedIdentifier = false;
                    continue;
                }

                if (current == '-' && next == '-') break;
                if (current == '/' && next == '*') { blockCommentDepth++; index++; }
                else if (current == '\'') inString = true;
                else if (current == '[') inBracketIdentifier = true;
                else if (current == '"') inQuotedIdentifier = true;
            }
        }

        private static void EnsureHistoryTable(string connectionString)
        {
            const string sql = @"IF OBJECT_ID(N'dbo.HistoriqueScriptSql', N'U') IS NULL
CREATE TABLE dbo.HistoriqueScriptSql(DateExecution DATETIME2, NomScript VARCHAR(500), MessageErreur VARCHAR(MAX));";
            Execute(connectionString, sql, null);
        }

        private static HashSet<string> ReadHistory(string connectionString)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            using (var connection = new SqlConnection(connectionString))
            using (var command = new SqlCommand(
                "SELECT NomScript FROM " + HistoryTable,
                connection))
            {
                connection.Open();
                using (var reader = command.ExecuteReader())
                    while (reader.Read()) result.Add(Normalize(Convert.ToString(reader[0])));
            }
            return result;
        }

        private static void InsertHistory(string connectionString, string file, string error)
        {
            const string sql = @"INSERT INTO dbo.HistoriqueScriptSql(DateExecution, NomScript, MessageErreur)
VALUES (GETDATE(), @File, @Error);";
            Execute(connectionString, sql, command =>
            {
                command.Parameters.Add("@File", SqlDbType.VarChar, 500).Value = file;
                command.Parameters.Add("@Error", SqlDbType.VarChar, -1).Value = error;
            });
        }

        private static void UpdateVersion(string connectionString, string version)
        {
            const string sql = @"IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=0 AND minor_id=0 AND name=N'Version')
    EXEC sys.sp_addextendedproperty @name=N'Version', @value=@Version;
ELSE
    EXEC sys.sp_updateextendedproperty @name=N'Version', @value=@Version;";
            Execute(connectionString, sql, command =>
                command.Parameters.Add("@Version", SqlDbType.VarChar, 100).Value = version);
            Log.Info("Version number has been set to " + version);
        }

        private static void Execute(string connectionString, string sql, Action<SqlCommand> configure)
        {
            using (var connection = new SqlConnection(connectionString))
            using (var command = new SqlCommand(sql, connection))
            {
                configure?.Invoke(command);
                connection.Open();
                command.ExecuteNonQuery();
            }
        }
    }
}
