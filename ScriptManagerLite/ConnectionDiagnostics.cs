using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Globalization;
using System.Text;

namespace ScriptManager
{
    /// <summary>
    /// Masks SQL credentials (user and password) in a connection string before it is logged.
    /// The string is scanned with a small key=value parser that understands quoted values;
    /// anything it cannot parse with confidence is hidden entirely rather than printed.
    /// </summary>
    internal static class ConnectionStringRedactor
    {
        internal const string Mask = "***";
        internal const string Unavailable = "<connection string hidden: not in key=value format>";
        internal const string UnsupportedKeyword = "<connection string hidden: unsupported keyword>";
        private const int MaxNesting = 3;

        private sealed class Entry
        {
            internal string Key;
            internal string RawValue;
            internal string Value;
            internal bool Suspicious;
        }

        internal static string Redact(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
                return string.Empty;
            try
            {
                string result;
                // On failure TryRedact leaves the fallback text in result.
                TryRedact(connectionString, 0, out result);
                return result;
            }
            catch
            {
                return Unavailable;
            }
        }

        /// <summary>Returns the user names and passwords found in the connection string.</summary>
        internal static IList<string> ExtractSecrets(string connectionString)
        {
            var secrets = new List<string>();
            try
            {
                CollectSecrets(connectionString, 0, secrets);
            }
            catch
            {
                // Diagnostics must never change application behavior.
            }
            return secrets;
        }

        private static void CollectSecrets(string connectionString, int depth, List<string> secrets)
        {
            var entries = new List<Entry>();
            if (string.IsNullOrEmpty(connectionString) || depth > MaxNesting || !TryParse(connectionString, entries))
                return;
            foreach (Entry entry in entries)
            {
                if (IsProviderConnectionString(entry.Key))
                    CollectSecrets(entry.Value, depth + 1, secrets);
                else if (IsSensitive(entry.Key) && entry.Value.Length > 0)
                    secrets.Add(entry.Value);
            }
        }

        private static bool TryRedact(string connectionString, int depth, out string result)
        {
            result = Unavailable;
            var entries = new List<Entry>();
            if (depth > MaxNesting || !TryParse(connectionString, entries))
                return false;
            if (!AllKeywordsSupported(entries, depth))
            {
                result = UnsupportedKeyword;
                return false;
            }
            // The hidden form is deliberately more conservative than the provider: it only has to
            // keep a possible credential out of the log. The connection string itself is untouched.
            if (entries.Exists(entry => entry.Suspicious))
                return false;

            var output = new StringBuilder();
            foreach (Entry entry in entries)
            {
                if (output.Length > 0)
                    output.Append(';');
                output.Append(entry.Key).Append('=');
                if (IsProviderConnectionString(entry.Key))
                {
                    string inner;
                    if (!TryRedact(entry.Value, depth + 1, out inner))
                    {
                        result = inner;
                        return false;
                    }
                    output.Append('"').Append(inner.Replace("\"", "\"\"")).Append('"');
                }
                else if (IsSensitive(entry.Key) && entry.Value.Length > 0)
                    output.Append(Mask);
                else
                    output.Append(entry.RawValue);
            }
            if (connectionString.TrimEnd().EndsWith(";", StringComparison.Ordinal))
                output.Append(';');
            result = output.ToString();
            return true;
        }

        private static bool TryParse(string text, List<Entry> entries)
        {
            int length = text.Length;
            int index = 0;
            while (index < length)
            {
                int keyStart = index;
                while (index < length && text[index] != '=' && text[index] != ';')
                    index++;
                string key = text.Substring(keyStart, index - keyStart);
                if (index >= length || text[index] == ';')
                {
                    if (key.Trim().Length > 0)
                        return false;
                    index++;
                    continue;
                }

                index++;
                int valueStart = index;
                int cursor = index;
                while (cursor < length && char.IsWhiteSpace(text[cursor]))
                    cursor++;

                string value;
                int end;
                bool suspicious = false;
                if (cursor < length && (text[cursor] == '"' || text[cursor] == '\''))
                {
                    char quote = text[cursor];
                    var unquoted = new StringBuilder();
                    int scan = cursor + 1;
                    bool closed = false;
                    while (scan < length)
                    {
                        if (text[scan] == quote)
                        {
                            if (scan + 1 < length && text[scan + 1] == quote)
                            {
                                unquoted.Append(quote);
                                scan += 2;
                                continue;
                            }
                            closed = true;
                            scan++;
                            break;
                        }
                        unquoted.Append(text[scan]);
                        scan++;
                    }
                    if (!closed)
                        return false;
                    // Only whitespace may follow the closing quote before ';' or the end.
                    end = scan;
                    while (end < length && text[end] != ';')
                    {
                        if (!char.IsWhiteSpace(text[end]))
                            return false;
                        end++;
                    }
                    value = unquoted.ToString();
                    // Quoting does not make an embedded assignment safe to print. The provider
                    // connection-string wrapper legitimately holds a whole inner connection string
                    // and is redacted recursively instead.
                    suspicious = !IsSensitive(key) && !IsProviderConnectionString(key)
                        && value.IndexOfAny(new[] { '=', '\r', '\n' }) >= 0;
                }
                else
                {
                    end = cursor;
                    while (end < length && text[end] != ';')
                        end++;
                    value = text.Substring(cursor, end - cursor).Trim();
                    // An unquoted value holding '=' or a line break may be a second assignment that
                    // lost its ';' (for example "Server=h Password=x"). Sensitive keys are masked
                    // whole, so only the values that would be printed need this check.
                    string raw = text.Substring(valueStart, end - valueStart);
                    suspicious = !IsSensitive(key) && raw.IndexOfAny(new[] { '=', '\r', '\n' }) >= 0;
                }

                entries.Add(new Entry
                {
                    Key = key,
                    RawValue = text.Substring(valueStart, end - valueStart),
                    Value = value,
                    Suspicious = suspicious
                });
                index = end + 1;
            }
            return true;
        }

        // Fail closed: a value is only printed when its keyword is one the SqlClient provider
        // accepts, because a misspelled or unknown keyword could carry a user name or password.
        // The provider itself is the source of truth (it also knows every synonym).
        private static bool AllKeywordsSupported(List<Entry> entries, int depth)
        {
            bool wrapper = depth == 0 && entries.Exists(entry => IsProviderConnectionString(entry.Key));
            var provider = new SqlConnectionStringBuilder();
            foreach (Entry entry in entries)
            {
                if (wrapper)
                {
                    // Keywords of the provider connection-string wrapper (EntityClient).
                    string name = NormalizeKey(entry.Key);
                    if (name == "metadata" || name == "provider" || name == "name" || name == "providerconnectionstring")
                        continue;
                    return false;
                }
                if (!provider.ContainsKey(entry.Key.Trim()))
                    return false;
            }
            return true;
        }

        private static string NormalizeKey(string key)
        {
            var normalized = new StringBuilder();
            foreach (char c in key)
                if (!char.IsWhiteSpace(c))
                    normalized.Append(char.ToLowerInvariant(c));
            return normalized.ToString();
        }

        private static bool IsSensitive(string key)
        {
            switch (NormalizeKey(key))
            {
                case "userid":
                case "uid":
                case "user":
                case "password":
                case "pwd":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsProviderConnectionString(string key)
        {
            return NormalizeKey(key) == "providerconnectionstring";
        }
    }

    /// <summary>
    /// Masks the credentials of the active connection string wherever they are quoted in free
    /// text (SQL Server reports a failed login as: Login failed for user 'name').
    /// </summary>
    internal sealed class SecretMasker
    {
        private readonly List<string> secrets = new List<string>();

        internal void Register(IEnumerable<string> values)
        {
            foreach (string value in values)
                if (!string.IsNullOrEmpty(value) && !secrets.Contains(value))
                    secrets.Add(value);
        }

        internal string Mask(string text)
        {
            if (string.IsNullOrEmpty(text) || secrets.Count == 0)
                return text;
            foreach (string secret in secrets)
                foreach (string quote in new[] { "'", "\"" })
                    text = text.Replace(quote + secret + quote, quote + ConnectionStringRedactor.Mask + quote);
            return text;
        }
    }

    /// <summary>
    /// Adds connection-failure context to exceptions and renders it for the log.
    /// Nothing here changes whether or how an exception propagates.
    /// </summary>
    internal static class SqlDiagnostics
    {
        private const string OperationKey = "ScriptManager.Operation";
        private const string PhaseKey = "ScriptManager.Phase";
        private const string PhaseMsKey = "ScriptManager.PhaseElapsedMs";
        private const string TotalMsKey = "ScriptManager.TotalElapsedMs";

        internal static void Annotate(Exception exception, string operation, string phase, long phaseElapsedMs, long totalElapsedMs)
        {
            try
            {
                if (exception == null || exception.Data.IsReadOnly || exception.Data.Contains(PhaseKey))
                    return;
                exception.Data[OperationKey] = operation;
                exception.Data[PhaseKey] = phase;
                exception.Data[PhaseMsKey] = phaseElapsedMs.ToString(CultureInfo.InvariantCulture);
                exception.Data[TotalMsKey] = totalElapsedMs.ToString(CultureInfo.InvariantCulture);
            }
            catch
            {
                // Diagnostics must never change application behavior.
            }
        }

        internal static string Describe(Exception exception)
        {
            var output = new StringBuilder();
            try
            {
                for (Exception current = exception; current != null; current = current.InnerException)
                {
                    if (current.Data.Contains(PhaseKey))
                        output.AppendLine(string.Format(
                            "Diagnostics: operation={0}, phase={1}, phaseElapsedMs={2}, totalElapsedMs={3}",
                            current.Data[OperationKey],
                            current.Data[PhaseKey],
                            current.Data[PhaseMsKey],
                            current.Data[TotalMsKey]));

                    var sqlException = current as SqlException;
                    if (sqlException != null)
                        DescribeSqlException(sqlException, output);
                }
            }
            catch
            {
                // Keep whatever was collected before the failure.
            }
            return output.ToString();
        }

        internal const string HiddenServer = "<hidden>";

        /// <summary>
        /// SqlException.Server and SqlError.Server can echo the client-supplied Data Source, so a
        /// malformed value could carry credential-like text. A server identifier is printed only when
        /// every character is a letter, a digit or one of . - _ \ , : ( ) [ ] $ # @ (this covers names
        /// such as (local)\SQL2022, host\instance, lpc:(local)\SQL2022 and host,1433). Anything else
        /// (=, ;, quotes, whitespace, line breaks, control characters) or an over-long value is
        /// replaced by a fixed marker. An empty value stays empty. This is a display filter only and
        /// never touches the connection string.
        /// </summary>
        internal static string SafeServer(string server)
        {
            try
            {
                if (string.IsNullOrEmpty(server))
                    return string.Empty;
                if (server.Length > 256)
                    return HiddenServer;
                foreach (char c in server)
                    if (!char.IsLetterOrDigit(c) && ".-_\\,:()[]$#@".IndexOf(c) < 0)
                        return HiddenServer;
                return server;
            }
            catch
            {
                return HiddenServer;
            }
        }

        private static void DescribeSqlException(SqlException exception, StringBuilder output)
        {
            output.AppendLine(string.Format(
                "SqlException: number={0}, state={1}, class={2}, lineNumber={3}, server={4}, procedure={5}, clientConnectionId={6}",
                exception.Number,
                exception.State,
                exception.Class,
                exception.LineNumber,
                SafeServer(exception.Server),
                exception.Procedure,
                exception.ClientConnectionId));
            if (exception.Errors == null)
                return;
            for (int index = 0; index < exception.Errors.Count; index++)
            {
                SqlError error = exception.Errors[index];
                output.AppendLine(string.Format(
                    "  SqlError[{0}]: number={1}, state={2}, class={3}, lineNumber={4}, server={5}, procedure={6}",
                    index,
                    error.Number,
                    error.State,
                    error.Class,
                    error.LineNumber,
                    SafeServer(error.Server),
                    error.Procedure));
            }
        }
    }
}
