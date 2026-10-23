using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Reflection;

namespace ScriptManager.Tests
{
    // Minimal offline checks for ConnectionDiagnostics.cs. All connection strings are synthetic.
    // Run: dotnet run --project ScriptManagerLite.Tests  (exit code 0 = all executed checks passed)
    internal static class Program
    {
        private static int passed;
        private static int failed;
        private static int skipped;

        private static int Main()
        {
            Redaction();
            Masking();
            N1();
            F1();
            FNew1();
            QuotedAndServer();
            SqlSelection();
            Diagnostics();
            Console.WriteLine("RESULT: {0} PASS, {1} FAIL, {2} SKIPPED", passed, failed, skipped);
            return failed == 0 ? 0 : 1;
        }

        private static void Redaction()
        {
            string r = ConnectionStringRedactor.Redact(
                "Data Source=(local)\\SQL2022;Initial Catalog=Db1;User ID=synthUser;Password=synthPass;Connect Timeout=30");
            Check("sql login: user and password masked", !r.Contains("synthUser") && !r.Contains("synthPass"));
            Check("sql login: non-sensitive fields kept",
                r.Contains("Data Source=(local)\\SQL2022") && r.Contains("Initial Catalog=Db1") && r.Contains("Connect Timeout=30"));

            r = ConnectionStringRedactor.Redact("Data Source=h;Initial Catalog=d;Integrated Security=True");
            Check("integrated security unchanged", r == "Data Source=h;Initial Catalog=d;Integrated Security=True");

            r = ConnectionStringRedactor.Redact("Server=h;Password=\"a;b=c;PWD=zzz\";uid=synthUser2;Database=d");
            Check("semicolons and keywords inside quoted password", !r.Contains("a;b") && !r.Contains("zzz") && !r.Contains("synthUser2"));
            Check("fields after quoted password kept", r.EndsWith("Database=d"));

            r = ConnectionStringRedactor.Redact("Server=h;Password='it''s;secret';User='synthUser3'");
            Check("single-quoted values with doubled quote", !r.Contains("secret") && !r.Contains("synthUser3"));

            foreach (string key in new[] { "User ID", "UID", "User", "uid", "USER ID", "Password", "PWD", "pwd", " Password " })
            {
                r = ConnectionStringRedactor.Redact("Server=h;" + key + "=synthSecretValue;Database=d");
                Check("alias masked: '" + key.Trim() + "'", !r.Contains("synthSecretValue") && r.Contains("Database=d"));
            }

            r = ConnectionStringRedactor.Redact("Server=h;User ID=;Password=;Database=d");
            Check("empty credentials stay empty and harmless", r == "Server=h;User ID=;Password=;Database=d");

            r = ConnectionStringRedactor.Redact("Server=h;Password=synthPass2;Database=d;");
            Check("trailing semicolon kept", r.EndsWith(";") && !r.Contains("synthPass2"));

            foreach (string bad in new[] { "Server=h;Password", "Server=h;Password=\"unterminated;Database=d", "garbage synthPass3", "Server=h;App=\"open;Password=synthPass4" })
            {
                r = ConnectionStringRedactor.Redact(bad);
                Check("malformed hidden: " + bad.Substring(0, Math.Min(bad.Length, 18)),
                    r == ConnectionStringRedactor.Unavailable && !r.Contains("synthPass"));
            }

            r = ConnectionStringRedactor.Redact(
                "metadata=res://*/M.csdl;provider=System.Data.SqlClient;provider connection string=\"Data Source=h;User Id=synthUser5;Password=synthPass5;Initial Catalog=d\"");
            Check("EDMX wrapper: inner credentials masked", !r.Contains("synthUser5") && !r.Contains("synthPass5"));
            Check("EDMX wrapper: inner fields kept", r.Contains("Data Source=h") && r.Contains("Initial Catalog=d") && r.Contains("provider=System.Data.SqlClient"));

            Check("null and empty", ConnectionStringRedactor.Redact(null) == string.Empty && ConnectionStringRedactor.Redact("") == string.Empty);
        }

        private static void N1()
        {
            string hidden = ConnectionStringRedactor.UnsupportedKeyword;

            string r = ConnectionStringRedactor.Redact("Server=h;User Name=synU;Password=synP");
            Check("N1-A unsupported username keyword hidden", r == hidden && !r.Contains("synU") && !r.Contains("synP") && !r.Contains("Server=h"));

            r = ConnectionStringRedactor.Redact("Server=h;User ID=synU;Passwrd=synP");
            Check("N1-B misspelled password keyword hidden", r == hidden && !r.Contains("synU") && !r.Contains("synP"));

            r = ConnectionStringRedactor.Redact("Data Source=h;Initial Catalog=d;User ID=synU2;Password=synP2;Secret Token=synT");
            Check("N1-C mixed supported and unsupported hidden",
                r == hidden && !r.Contains("synU2") && !r.Contains("synP2") && !r.Contains("synT") && !r.Contains("Data Source"));

            r = ConnectionStringRedactor.Redact("Server=h;Custom=\"a;b=c\"\"d;PWD=zzz\";Database=d");
            Check("N1-D quoted unknown value hidden", r == hidden && !r.Contains("a;b") && !r.Contains("zzz") && !r.Contains("c\"\"d"));
            r = ConnectionStringRedactor.Redact("Server=h;Custom='x;y=z''w';Database=d");
            Check("N1-D single-quoted unknown value hidden", r == hidden && !r.Contains("x;y"));
            r = ConnectionStringRedactor.Redact("Server=h;Custom=unquoted=with=equals;Database=d");
            Check("N1-D unquoted unknown value with equals hidden", r == hidden && !r.Contains("equals"));

            r = ConnectionStringRedactor.Redact("Data Source=(local)\\SQL2022;Initial Catalog=Db1;User ID=synU3;Password=synP3;Connect Timeout=30;Encrypt=True");
            Check("N1-E valid string: useful fields kept, credentials masked",
                r == "Data Source=(local)\\SQL2022;Initial Catalog=Db1;User ID=***;Password=***;Connect Timeout=30;Encrypt=True");

            r = ConnectionStringRedactor.Redact("Data Source=h;Initial Catalog=d;Integrated Security=SSPI");
            Check("N1-F integrated security unchanged", r == "Data Source=h;Initial Catalog=d;Integrated Security=SSPI");

            var supported = new[]
            {
                "Data Source=h", "Server=h", "Address=h", "Addr=h", "Network Address=h", "Initial Catalog=d", "Database=d",
                "Integrated Security=true", "Trusted_Connection=true", "Connect Timeout=15", "Connection Timeout=15", "Timeout=15",
                "Encrypt=true", "TrustServerCertificate=true", "Application Name=a", "App=a", "MultipleActiveResultSets=true",
                "Persist Security Info=false", "Pooling=true", "Max Pool Size=50", "Min Pool Size=1", "Packet Size=8192",
                "Workstation ID=w", "Current Language=us_english", "Failover Partner=p", "ApplicationIntent=ReadOnly",
                "MultiSubnetFailover=true", "ConnectRetryCount=1", "ConnectRetryInterval=10", "Authentication=Active Directory Password",
                "Network Library=dbmssocn", "Asynchronous Processing=false", "Enlist=true", "Connection Reset=true", "Type System Version=Latest"
            };
            foreach (string option in supported)
            {
                string shown = ConnectionStringRedactor.Redact("Data Source=h;" + option);
                if (ProviderAccepts(option))
                    Check("supported keyword kept: " + option.Split('=')[0], shown == "Data Source=h;" + option);
                else
                {
                    skipped++;
                    Console.WriteLine("SKIPPED supported keyword '" + option.Split('=')[0] + "' (not accepted by the SqlClient provider on this runtime)");
                }
            }

            r = ConnectionStringRedactor.Redact(
                "metadata=res://*/M.csdl;provider=System.Data.SqlClient;provider connection string=\"Data Source=h;User Name=synU4;Password=synP4\"");
            Check("N1 wrapper with unsupported inner keyword hidden", r == hidden && !r.Contains("synU4") && !r.Contains("synP4"));
            r = ConnectionStringRedactor.Redact("metadata=m;Extra=synU5;provider connection string=\"Data Source=h;Password=synP5\"");
            Check("N1 wrapper with unknown outer keyword hidden", r == hidden && !r.Contains("synU5") && !r.Contains("synP5"));

            Check("N1 unsupported keyword still yields secrets for free-text masking",
                ConnectionStringRedactor.ExtractSecrets("Server=h;User ID=synU6;Passwrd=synP6").Contains("synU6"));

            AppLevel();
        }

        private static bool ProviderAccepts(string option)
        {
            try { new SqlConnectionStringBuilder("Data Source=h;" + option); return true; }
            catch { return false; }
        }

        // Runs the built application without a database: the synthetic SQL directory does not
        // exist, and the application checks it before it opens any connection.
        private static void AppLevel()
        {
            string source = null;
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int up = 0; up < 6 && dir != null; up++, dir = System.IO.Path.GetDirectoryName(dir))
            {
                string candidate = System.IO.Path.Combine(dir, "ScriptManagerLite", "bin", "Release", "net48");
                if (System.IO.File.Exists(System.IO.Path.Combine(candidate, "ScriptManager.exe"))) { source = candidate; break; }
            }
            if (source == null)
            {
                skipped++;
                Console.WriteLine("SKIPPED N1-G application-level run (Release build of ScriptManagerLite not found)");
                return;
            }

            foreach (string connectionString in new[]
            {
                "Data Source=synthetic.invalid;User Name=synUApp;Password=synPApp",
                "Data Source=synthetic.invalid;User ID=synUApp;Passwrd=synPApp",
                "Data Source=synthetic.invalid;Initial Catalog=SynDb;User ID=synUApp;Password=synPApp;Connect Timeout=5",
                "Data Source=\"synthetic.invalid\" User ID=synUApp;Password=synPApp",
                "Data Source='synthetic.invalid' User ID=synUApp;Password=synPApp",
                "Data Source=\"synthetic.invalid\"  ;Initial Catalog=SynDb;User ID=synUApp;Password=synPApp;Connect Timeout=5"
            })
            {
                string work = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sml-n1-" + Guid.NewGuid().ToString("N"));
                try
                {
                    CopyDirectory(source, work);
                    string configFile = System.IO.Path.Combine(work, "synthetic.config");
                    System.IO.File.WriteAllText(configFile,
                        "<connectionStrings><add name=\"Syn\" connectionString=\"" + System.Security.SecurityElement.Escape(connectionString) + "\" /></connectionStrings>");
                    var start = new System.Diagnostics.ProcessStartInfo(System.IO.Path.Combine(work, "ScriptManager.exe"),
                        "/csName Syn /csFile \"" + configFile + "\" /sqlPath \"" + System.IO.Path.Combine(work, "no-such-sql-dir") + "\"")
                    {
                        WorkingDirectory = work, UseShellExecute = false, RedirectStandardInput = true,
                        RedirectStandardOutput = true, RedirectStandardError = true
                    };
                    string output;
                    int exitCode;
                    using (var process = System.Diagnostics.Process.Start(start))
                    {
                        process.StandardInput.WriteLine("O");
                        process.StandardInput.Close();
                        string error = process.StandardError.ReadToEnd();
                        output = process.StandardOutput.ReadToEnd() + error;
                        if (!process.WaitForExit(30000)) { process.Kill(); }
                        exitCode = process.ExitCode;
                    }
                    string log = System.IO.File.ReadAllText(System.IO.Path.Combine(work, "Logs", "ScriptManager-INFO.log"));
                    bool unsupported = connectionString.Contains("User Name") || connectionString.Contains("Passwrd");
                    bool trailing = connectionString.Contains("\" User ID") || connectionString.Contains("' User ID");
                    bool quotedValid = connectionString.Contains("\"  ;");
                    string label = unsupported ? "unsupported keyword"
                        : trailing ? "F1 text after closing quote: " + connectionString[connectionString.IndexOf("synthetic.invalid") - 1]
                        : quotedValid ? "F1 valid quoted value" : "supported keywords";
                    Check("N1-G app (" + label + "): no secret in console", !output.Contains("synUApp") && !output.Contains("synPApp"));
                    Check("N1-G app (" + label + "): no secret in log file", !log.Contains("synUApp") && !log.Contains("synPApp"));
                    if (trailing)
                        Check("N1-G app (" + label + "): fail-closed fallback, string hidden",
                            output.Contains(ConnectionStringRedactor.Unavailable) && log.Contains(ConnectionStringRedactor.Unavailable)
                            && !output.Contains("synthetic.invalid") && !log.Contains("synthetic.invalid"));
                    else if (quotedValid)
                        Check("N1-G app (" + label + "): valid string parsed, credentials masked",
                            log.Contains("Data Source=\"synthetic.invalid\"  ;Initial Catalog=SynDb;User ID=***;Password=***;Connect Timeout=5"));
                    else if (unsupported)
                        Check("N1-G app (" + label + "): fallback shown, string hidden",
                            output.Contains(ConnectionStringRedactor.UnsupportedKeyword) && log.Contains(ConnectionStringRedactor.UnsupportedKeyword)
                            && !output.Contains("synthetic.invalid") && !log.Contains("synthetic.invalid"));
                    else
                        Check("N1-G app (" + label + "): masked string with useful fields",
                            log.Contains("Data Source=synthetic.invalid;Initial Catalog=SynDb;User ID=***;Password=***;Connect Timeout=5"));
                    Check("N1-G app (" + label + "): exit code unchanged (0)", exitCode == 0);
                }
                finally
                {
                    try { System.IO.Directory.Delete(work, true); } catch { }
                }
            }
        }

        private static void CopyDirectory(string from, string to)
        {
            System.IO.Directory.CreateDirectory(to);
            foreach (string file in System.IO.Directory.GetFiles(from))
                System.IO.File.Copy(file, System.IO.Path.Combine(to, System.IO.Path.GetFileName(file)));
            foreach (string sub in System.IO.Directory.GetDirectories(from))
                CopyDirectory(sub, System.IO.Path.Combine(to, System.IO.Path.GetFileName(sub)));
        }

        private static void F1()
        {
            string malformed = ConnectionStringRedactor.Unavailable;

            string r = ConnectionStringRedactor.Redact("Server=\"h\" Password=synP;Database=d");
            Check("F1-A text after double quote hidden", r == malformed && !r.Contains("synP") && !r.Contains("Server"));

            r = ConnectionStringRedactor.Redact("Server='h' User ID=synU;Database=d");
            Check("F1-B text after single quote hidden", r == malformed && !r.Contains("synU"));

            r = ConnectionStringRedactor.Redact("Server='h'x;Database=d;User ID=synU");
            Check("F1-C non-sensitive garbage after quote hidden", r == malformed && !r.Contains("synU") && !r.Contains("Database"));

            r = ConnectionStringRedactor.Redact("Server=\"h\"  ;Database=d;User ID=synU;Password=synP");
            Check("F1-D whitespace after closing quote accepted",
                r == "Server=\"h\"  ;Database=d;User ID=***;Password=***");

            r = ConnectionStringRedactor.Redact("Application Name=\"a \"\"quoted\"\" b\";Password=synP;Database=d");
            Check("F1-E double-quote escapes preserved", r == "Application Name=\"a \"\"quoted\"\" b\";Password=***;Database=d");
            r = ConnectionStringRedactor.Redact("Application Name='a ''quoted'' b';Password=synP;Database=d");
            Check("F1-E single-quote escapes preserved", r == "Application Name='a ''quoted'' b';Password=***;Database=d");
            r = ConnectionStringRedactor.Redact("Application Name=\"a\"\"\";Database=d");
            Check("F1-E escaped quote right before closing quote", r == "Application Name=\"a\"\"\";Database=d");

            r = ConnectionStringRedactor.Redact("Server=\"h\"");
            Check("F1-F closing quote then end of input", r == "Server=\"h\"");
            r = ConnectionStringRedactor.Redact("Server=\"h\"   ");
            Check("F1-F closing quote then whitespace only", r.StartsWith("Server=\"h\"") && !r.Contains("hidden"));
            r = ConnectionStringRedactor.Redact("Server=\"h\";Password=synP");
            Check("F1-F closing quote then semicolon", r == "Server=\"h\";Password=***");
            r = ConnectionStringRedactor.Redact("Server=\"h\"\tDatabase=d;Password=synP");
            Check("F1-F tab then text hidden", r == malformed && !r.Contains("synP"));
            r = ConnectionStringRedactor.Redact("Server=\"h\"x");
            Check("F1-F closing quote then invalid character hidden", r == malformed);
            r = ConnectionStringRedactor.Redact("Password=\"synP\" Server=h");
            Check("F1-F sensitive quoted value with trailing text hidden", r == malformed && !r.Contains("synP"));

            r = ConnectionStringRedactor.Redact(
                "metadata=m;provider connection string=\"Data Source=\"\"h\"\" User ID=synU;Password=synP\"");
            Check("F1-G wrapper: invalid quoted value inside hidden", r == malformed && !r.Contains("synU") && !r.Contains("synP"));
            r = ConnectionStringRedactor.Redact("metadata='m' x;provider connection string=\"Data Source=h;Password=synP\"");
            Check("F1-G wrapper: invalid quoted value outside hidden", r == malformed && !r.Contains("synP"));
            r = ConnectionStringRedactor.Redact("metadata=m;provider connection string=\"Data Source=h;Password=synP\" junk");
            Check("F1-G wrapper: text after wrapper quote hidden", r == malformed && !r.Contains("synP"));

            Check("F1 secrets not extracted from rejected string", ConnectionStringRedactor.ExtractSecrets("Server=\"h\" Password=synP").Count == 0);
        }

        private static void FNew1()
        {
            string malformed = ConnectionStringRedactor.Unavailable;

            // G1: a missing ';' lets a credential assignment hide inside a non-sensitive value.
            var leaks = new[]
            {
                new[] { "Server=h Password=synP;Database=d", "synP" },
                new[] { "Server=h User ID=synU;Database=d", "synU" },
                new[] { "Data Source=h;Initial Catalog=d Password=synP", "synP" },
                new[] { "Server=h;Application Name=a=Password=synP", "synP" },
                new[] { "Server=h\nPassword=synP;Database=d", "synP" },
                new[] { "Server=h\r\nPassword=synP;Database=d", "synP" },
                new[] { "Server=\nPassword=synP;Database=d", "synP" },
                new[] { "Server=h\nsynP;Database=d", "synP" },
                new[] { "Data Source=synthetic.invalid User ID=synUApp;Password=synPApp", "synUApp" }
            };
            foreach (string[] leak in leaks)
            {
                string r = ConnectionStringRedactor.Redact(leak[0]);
                Check("F-NEW-1 hidden: " + leak[0].Replace("\r", "<CR>").Replace("\n", "<LF>"),
                    r == malformed && !r.Contains(leak[1]) && !r.Contains("Server") && !r.Contains("Data Source") && !r.Contains("Database"));
            }

            // G2: provider wrapper, outer keywords and inner string.
            foreach (string wrapper in new[]
            {
                "metadata=m Password=synP;provider connection string=\"Data Source=h\"",
                "provider=System.Data.SqlClient Password=synP;provider connection string=\"Data Source=h\"",
                "name=a User ID=synU;provider connection string=\"Data Source=h\"",
                "metadata=m;provider connection string=\"Data Source=h Password=synP;Initial Catalog=d\"",
                "metadata=m;provider connection string=\"Data Source=h;Application Name=a=Password=synP\"",
                "metadata=m;provider connection string=\"Data Source=h\nPassword=synP\""
            })
            {
                string r = ConnectionStringRedactor.Redact(wrapper);
                Check("F-NEW-1 wrapper hidden: " + wrapper.Substring(0, Math.Min(40, wrapper.Length)).Replace("\n", "<LF>"),
                    r == malformed && !r.Contains("synP") && !r.Contains("synU") && !r.Contains("metadata"));
            }

            // G3: ordinary valid input stays useful.
            string ok = ConnectionStringRedactor.Redact("Data Source=server01;Initial Catalog=Agendis;Integrated Security=True");
            Check("F-NEW-1 valid integrated security unchanged", ok == "Data Source=server01;Initial Catalog=Agendis;Integrated Security=True");
            ok = ConnectionStringRedactor.Redact("Server=server01;Database=Agendis;User ID=synU;Password=synP");
            Check("F-NEW-1 valid sql login masked", ok == "Server=server01;Database=Agendis;User ID=***;Password=***");
            ok = ConnectionStringRedactor.Redact("Server=server01;Database=Agendis;Application Name=My Agendis Tool;Integrated Security=True");
            Check("F-NEW-1 valid value with spaces kept", ok == "Server=server01;Database=Agendis;Application Name=My Agendis Tool;Integrated Security=True");
            ok = ConnectionStringRedactor.Redact("Server=h;User ID=syn U;Password=ab==cd=;Database=d");
            Check("F-NEW-1 credentials containing '=' or spaces masked, not hidden", ok == "Server=h;User ID=***;Password=***;Database=d");
            ok = ConnectionStringRedactor.Redact("Server=h;Password=a\nb;Database=d");
            Check("F-NEW-1 masked credential with line break not hidden", ok == "Server=h;Password=***;Database=d");
            ok = ConnectionStringRedactor.Redact(
                "metadata=res://*/M.csdl|res://*/M.ssdl|res://*/M.msl;provider=System.Data.SqlClient;provider connection string=\"Data Source=h;User ID=synU;Password=synP\"");
            Check("F-NEW-1 valid wrapper keeps outer fields and masks inner",
                ok.StartsWith("metadata=res://*/M.csdl|res://*/M.ssdl|res://*/M.msl;provider=System.Data.SqlClient;")
                && ok.Contains("User ID=***") && !ok.Contains("synU") && !ok.Contains("synP"));

            // G4: conservative fallback. The provider may accept such a value; only the log form is hidden.
            string unusual = "Server=h;Application Name=a=b;Database=d";
            bool providerAccepts;
            try { new SqlConnectionStringBuilder(unusual); providerAccepts = true; } catch { providerAccepts = false; }
            string shown = ConnectionStringRedactor.Redact(unusual);
            Check("F-NEW-1 conservative: unusual '=' value hidden in the log form", shown == malformed);
            Console.WriteLine("INFO    SqlConnectionStringBuilder accepts '{0}': {1}", unusual, providerAccepts);

            // G5/G6 previous behaviors are covered by the F1 and N1 tests, which run unchanged.
            // G7 documented limitation: no '=' and no line break, so not distinguishable by syntax.
            string limitation = ConnectionStringRedactor.Redact("Server=h Password synP");
            Console.WriteLine("INFO    documented limitation, not a pass criterion: 'Server=h Password synP' -> '{0}'", limitation);

            AppLevelFNew1();
        }

        private static void AppLevelFNew1()
        {
            string source = null;
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int up = 0; up < 6 && dir != null; up++, dir = System.IO.Path.GetDirectoryName(dir))
            {
                string candidate = System.IO.Path.Combine(dir, "ScriptManagerLite", "bin", "Release", "net48");
                if (System.IO.File.Exists(System.IO.Path.Combine(candidate, "ScriptManager.exe"))) { source = candidate; break; }
            }
            if (source == null)
            {
                skipped++;
                Console.WriteLine("SKIPPED F-NEW-1 application-level run (Release build of ScriptManagerLite not found)");
                return;
            }

            // { connection string, expected: "hidden" or "masked", label }
            var cases = new[]
            {
                new[] { "Data Source=synthetic.invalid User ID=synUApp;Password=synPApp", "hidden", "exact reviewer reproduction" },
                new[] { "Data Source=synthetic.invalid Password=synPApp;Initial Catalog=SynDb", "hidden", "missing semicolon before password" },
                new[] { "Data Source=synthetic.invalid;Application Name=a=Password=synPApp", "hidden", "embedded '=' in non-sensitive field" },
                new[] { "Data Source=synthetic.invalid\nPassword=synPApp;User ID=synUApp", "hidden", "line break with credential assignment" },
                new[] { "Data Source=synthetic.invalid;Initial Catalog=SynDb;User ID=synUApp;Password=synPApp;Connect Timeout=5", "masked", "valid ordinary string" },
                new[] { "Data Source=\"synthetic.invalid\" User ID=synUApp;Password=synPApp", "hidden", "F1 invalid quoted value" },
                new[] { "Data Source=\"synthetic.invalid User ID=synUApp Password=synPApp\";Initial Catalog=SynDb;Connect Timeout=5", "hidden", "Q2-D1 V3 quoted Data Source with assignments" }
            };
            foreach (string[] item in cases)
            {
                string work = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sml-fnew1-" + Guid.NewGuid().ToString("N"));
                try
                {
                    CopyDirectory(source, work);
                    string configFile = System.IO.Path.Combine(work, "synthetic.config");
                    string attribute = System.Security.SecurityElement.Escape(item[0]).Replace("\r", "&#13;").Replace("\n", "&#10;");
                    System.IO.File.WriteAllText(configFile,
                        "<connectionStrings><add name=\"Syn\" connectionString=\"" + attribute + "\" /></connectionStrings>");
                    string sqlDir = System.IO.Path.Combine(work, "no-such-sql-dir");
                    var start = new System.Diagnostics.ProcessStartInfo(System.IO.Path.Combine(work, "ScriptManager.exe"),
                        "/csName Syn /csFile \"" + configFile + "\" /sqlPath \"" + sqlDir + "\"")
                    {
                        WorkingDirectory = work, UseShellExecute = false, RedirectStandardInput = true,
                        RedirectStandardOutput = true, RedirectStandardError = true
                    };
                    string output;
                    int exitCode;
                    using (var process = System.Diagnostics.Process.Start(start))
                    {
                        process.StandardInput.WriteLine("O");
                        process.StandardInput.Close();
                        string error = process.StandardError.ReadToEnd();
                        output = process.StandardOutput.ReadToEnd() + error;
                        if (!process.WaitForExit(30000)) { process.Kill(); }
                        exitCode = process.ExitCode;
                    }
                    string log = System.IO.File.ReadAllText(System.IO.Path.Combine(work, "Logs", "ScriptManager-INFO.log"));
                    string label = "F-NEW-1 app (" + item[2] + ")";
                    Check(label + ": no secret in console", !output.Contains("synUApp") && !output.Contains("synPApp"));
                    Check(label + ": no secret in log file", !log.Contains("synUApp") && !log.Contains("synPApp"));
                    if (item[1] == "hidden")
                        Check(label + ": fallback shown, nothing of the string printed",
                            output.Contains(ConnectionStringRedactor.Unavailable) && log.Contains(ConnectionStringRedactor.Unavailable)
                            && !output.Contains("synthetic.invalid") && !log.Contains("synthetic.invalid"));
                    else
                        Check(label + ": masked with useful fields",
                            log.Contains("Data Source=synthetic.invalid;Initial Catalog=SynDb;User ID=***;Password=***;Connect Timeout=5"));
                    Check(label + ": exit code unchanged (0)", exitCode == 0);
                    Check(label + ": stopped at the missing SQL directory before any connection (DirectoryNotFoundException)",
                        output.Contains("System.IO.DirectoryNotFoundException") && !output.Contains("RUN SCRIPT") && !log.Contains("RUN SCRIPT"));
                }
                finally
                {
                    try { System.IO.Directory.Delete(work, true); } catch { }
                }
            }
        }

        private static void QuotedAndServer()
        {
            const string uid = "SMLQ_FAKE_UID_Q2D1";
            const string pwd = "SMLQ_FAKE_PWD_Q2D1";
            string malformed = ConnectionStringRedactor.Unavailable;

            // Group A: quoted non-sensitive values must be treated like unquoted ones (Q2-D1 V3).
            string r = ConnectionStringRedactor.Redact("Data Source=\"srv User ID=" + uid + " Password=" + pwd + "\";Initial Catalog=d;Integrated Security=True");
            Check("A1 quoted Data Source with User ID and Password assignments hidden", r == malformed && !r.Contains(uid) && !r.Contains(pwd) && !r.Contains("Initial Catalog"));
            r = ConnectionStringRedactor.Redact("Server=\"a=b\";Database=d");
            Check("A2 quoted non-sensitive value with '=' hidden", r == malformed && !r.Contains("a=b"));
            r = ConnectionStringRedactor.Redact("Server=\"a\rb\";Database=d");
            Check("A3 quoted non-sensitive value with CR hidden", r == malformed);
            r = ConnectionStringRedactor.Redact("Server=\"a\nb " + pwd + "\";Database=d");
            Check("A4 quoted non-sensitive value with LF hidden", r == malformed && !r.Contains(pwd));
            r = ConnectionStringRedactor.Redact("Server='a\r\nb " + uid + "';Database=d");
            Check("A5 quoted non-sensitive value with CRLF hidden (single quotes)", r == malformed && !r.Contains(uid));
            r = ConnectionStringRedactor.Redact("Application Name=\"My Tool\";Database=d");
            Check("A6 ordinary quoted value unchanged", r == "Application Name=\"My Tool\";Database=d");
            r = ConnectionStringRedactor.Redact("Server=h;Password=\"a=b=c\";Database=d");
            Check("A7 quoted Password with '=' masked, string not hidden", r == "Server=h;Password=***;Database=d");
            r = ConnectionStringRedactor.Redact("Server=h;User ID=\"u=1\";Password=p;Database=d");
            Check("A8 quoted User ID with '=' masked, string not hidden", r == "Server=h;User ID=***;Password=***;Database=d");
            string accepted = "Server=h;Application Name=\"a=b\";Database=d";
            r = ConnectionStringRedactor.Redact(accepted);
            bool providerStillAccepts;
            try { new SqlConnectionStringBuilder(accepted); providerStillAccepts = true; } catch { providerStillAccepts = false; }
            Check("A9 provider-accepted quoted value is conservatively hidden in the log form", r == malformed);
            Check("A9 the original input string is still accepted by the provider after redaction", providerStillAccepts);
            r = ConnectionStringRedactor.Redact("Data Source=\"lpc:(local)\\SMLQ_NOSUCH_Q2D1 User ID=" + uid + " Password=" + pwd + "\";Initial Catalog=ZZ_SMLQ_Q2_001;Integrated Security=True;Connect Timeout=5");
            Check("A10 exact Q2-D1 V3 reproduction hidden", r == malformed && !r.Contains(uid) && !r.Contains(pwd) && !r.Contains("ZZ_SMLQ_Q2_001"));
            r = ConnectionStringRedactor.Redact("metadata=m;provider connection string=\"Data Source=h;Application Name=\"\"a=" + pwd + "\"\"\"");
            Check("A11 wrapper: quoted '=' value inside the inner string hidden", r == malformed && !r.Contains(pwd));
            r = ConnectionStringRedactor.Redact("metadata=m;provider connection string=\"Data Source=h;Application Name=\"\"My Tool\"\";Password=p\"");
            Check("A12 wrapper: ordinary inner quoted value still shown, password masked",
                r.Contains("My Tool") && r.Contains("Password=***") && r != malformed);
            r = ConnectionStringRedactor.Redact("Custom=\"a=b\";Database=d");
            Check("A13 unsupported keyword keeps priority over suspicious quoted value", r == ConnectionStringRedactor.UnsupportedKeyword);

            // Group C: Server diagnostic sanitizer.
            Check("C1 ordinary local server", SqlDiagnostics.SafeServer("(local)") == "(local)");
            Check("C1 ordinary local server with lpc prefix", SqlDiagnostics.SafeServer(@"lpc:(local)\SQL2022") == @"lpc:(local)\SQL2022");
            Check("C2 ordinary named instance", SqlDiagnostics.SafeServer(@"POLE2024P008\SQL2022") == @"POLE2024P008\SQL2022");
            Check("C2 dot and local-machine forms", SqlDiagnostics.SafeServer(@".\SQL2022") == @".\SQL2022" && SqlDiagnostics.SafeServer(@"(local)\SQL2022") == @"(local)\SQL2022");
            Check("C3 server with numeric port", SqlDiagnostics.SafeServer("tcp:host.example,1433") == "tcp:host.example,1433" && SqlDiagnostics.SafeServer("SRV01,1433") == "SRV01,1433");
            Check("C4 empty and null server stay empty", SqlDiagnostics.SafeServer("") == string.Empty && SqlDiagnostics.SafeServer(null) == string.Empty);
            string hidden = SqlDiagnostics.HiddenServer;
            Check("C5 server with '=' hidden", SqlDiagnostics.SafeServer("srv=" + pwd) == hidden);
            Check("C6 server with ';' hidden", SqlDiagnostics.SafeServer("srv;" + uid) == hidden);
            Check("C7 server with CR, LF or CRLF hidden", SqlDiagnostics.SafeServer("srv\r" + uid) == hidden && SqlDiagnostics.SafeServer("srv\n" + uid) == hidden && SqlDiagnostics.SafeServer("srv\r\n" + uid) == hidden);
            Check("C8 credential-like text hidden", SqlDiagnostics.SafeServer("srv User ID=" + uid + " Password=" + pwd) == hidden);
            Check("C9 several suspicious tokens hidden", SqlDiagnostics.SafeServer("a=b;c=d e\tf\u0001\"g'") == hidden);
            Check("C9 control character and over-long value hidden", SqlDiagnostics.SafeServer("srv\u0007") == hidden && SqlDiagnostics.SafeServer(new string('a', 300)) == hidden);

            SqlException ex = TryCreateSqlExceptionWithServer("srv User ID=" + uid + " Password=" + pwd);
            if (ex == null)
            {
                skipped++;
                Console.WriteLine("SKIPPED C10 SqlException with Server (could not be created by reflection)");
            }
            else
            {
                string text = SqlDiagnostics.Describe(ex);
                Check("C10 exception-level and per-error Server hidden, canaries absent", !text.Contains(uid) && !text.Contains(pwd) && text.Split(new[] { "server=<hidden>" }, StringSplitOptions.None).Length - 1 == 2);
                Check("C10 other diagnostic fields kept", text.Contains("number=53") && text.Contains("state=7") && text.Contains("class=20") && text.Contains("clientConnectionId="));
            }
            SqlException ok = TryCreateSqlExceptionWithServer(@"(local)\SQL2022");
            if (ok == null) { skipped++; Console.WriteLine("SKIPPED C10 ordinary Server through Describe"); }
            else Check("C10 ordinary Server still printed in both places", SqlDiagnostics.Describe(ok).Split(new[] { @"server=(local)\SQL2022" }, StringSplitOptions.None).Length - 1 == 2);
        }

        private static SqlException TryCreateSqlExceptionWithServer(string server)
        {
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                ConstructorInfo errorCtor = typeof(SqlError).GetConstructors(flags).FirstOrDefault(c => c.GetParameters().Length == 7);
                if (errorCtor == null) return null;
                object error = errorCtor.Invoke(new object[] { 53, (byte)7, (byte)20, server, "synthetic message", "", 0 });
                var collection = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), true);
                typeof(SqlErrorCollection).GetMethod("Add", flags).Invoke(collection, new[] { error });
                MethodInfo create = typeof(SqlException).GetMethod("CreateException", flags, null, new[] { typeof(SqlErrorCollection), typeof(string) }, null);
                return create == null ? null : (SqlException)create.Invoke(null, new object[] { collection, "11.0.0" });
            }
            catch
            {
                return null;
            }
        }

        // N1: strict SQL extension selection. The checks call the product's own FindSqlFiles on a
        // synthetic folder through reflection, so they exercise the real discovery code of the built
        // executable (override the executable with SMLITE_EXE_UNDER_TEST for a negative control).
        private static void SqlSelection()
        {
            string exe = Environment.GetEnvironmentVariable("SMLITE_EXE_UNDER_TEST");
            if (string.IsNullOrEmpty(exe))
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                for (int up = 0; up < 6 && dir != null && string.IsNullOrEmpty(exe); up++, dir = System.IO.Path.GetDirectoryName(dir))
                {
                    string candidate = System.IO.Path.Combine(dir, "ScriptManagerLite", "bin", "Release", "net48", "ScriptManager.exe");
                    if (System.IO.File.Exists(candidate)) exe = candidate;
                }
            }
            if (string.IsNullOrEmpty(exe) || !System.IO.File.Exists(exe))
            {
                Check("N1 product executable found for the discovery tests", false);
                return;
            }

            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sml-n1-sel-" + Guid.NewGuid().ToString("N"));
            try
            {
                string[] names =
                {
                    "01.sql", "02.sql", "a.sql", "b.SQL", "g.Sql", "c.sqlbak", "d.sql_old", "e.sql.bak", "f.txt", "noext",
                    @"sub\n1.sql", @"sub\n2.sqlold", @"sub\n5.sql_bak", @"sub\inner\n3.SQL", @"sub\inner\n4.sql_bak"
                };
                foreach (string name in names)
                {
                    string path = System.IO.Path.Combine(root, name);
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                    System.IO.File.WriteAllText(path, "SELECT 1;\r\n");
                }
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(root, "empty.sql"), new byte[0]);

                List<string> selected = DiscoverWithProduct(exe, root);
                string Rel(string p) { return p.Replace('\\', '/').Substring(root.Replace('\\', '/').TrimEnd('/').Length + 1); }
                List<string> rel = selected.Select(Rel).ToList();

                Check("N1 a.sql selected", rel.Contains("a.sql"));
                Check("N1 b.SQL selected (upper-case extension)", rel.Contains("b.SQL"));
                Check("N1 g.Sql selected (mixed-case extension)", rel.Contains("g.Sql"));
                Check("N1 c.sqlbak excluded", !rel.Contains("c.sqlbak"));
                Check("N1 d.sql_old excluded", !rel.Contains("d.sql_old"));
                Check("N1 e.sql.bak excluded", !rel.Contains("e.sql.bak"));
                Check("N1 f.txt excluded", !rel.Contains("f.txt"));
                Check("N1 file without extension excluded", !rel.Contains("noext"));
                Check("N1 empty valid .sql file stays eligible", rel.Contains("empty.sql"));
                Check("N1 nested valid files selected", rel.Contains("sub/n1.sql") && rel.Contains("sub/inner/n3.SQL"));
                Check("N1 nested invalid extensions excluded", !rel.Contains("sub/n2.sqlold") && !rel.Contains("sub/n5.sql_bak") && !rel.Contains("sub/inner/n4.sql_bak"));
                Check("N1 exact selection and established order (files of a folder, then sub-folders depth-first)",
                    string.Join("|", rel) == "01.sql|02.sql|a.sql|b.SQL|empty.sql|g.Sql|sub/n1.sql|sub/inner/n3.SQL");
                Check("N1 only valid files are returned (count)", rel.Count == 8);

                string onlyInvalid = System.IO.Path.Combine(root, "onlyinvalid");
                System.IO.Directory.CreateDirectory(onlyInvalid);
                foreach (string n in new[] { "x.sqlbak", "y.sql_old", "z.sql.bak" }) System.IO.File.WriteAllText(System.IO.Path.Combine(onlyInvalid, n), "SELECT 1;");
                Check("N1 a folder with only invalid extensions yields nothing", DiscoverWithProduct(exe, onlyInvalid).Count == 0);
            }
            finally
            {
                try { System.IO.Directory.Delete(root, true); } catch { }
            }
        }

        private static List<string> DiscoverWithProduct(string exe, string directory)
        {
            var flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Assembly product = Assembly.LoadFile(exe);
            Type managerType = product.GetType("ScriptManager.ScriptManager");
            Type optionsType = product.GetType("ScriptManager.RunnerOptions");
            object options = Activator.CreateInstance(optionsType, true);
            optionsType.GetField("EnvironmentName", flags).SetValue(options, "");
            optionsType.GetField("SqlPath", flags).SetValue(options, directory);
            object manager = Activator.CreateInstance(managerType, flags, null, new[] { options }, null);
            return ((IEnumerable<string>)managerType.GetMethod("FindSqlFiles", flags).Invoke(manager, new object[] { directory })).ToList();
        }

        private static void Masking()
        {
            IList<string> secrets = ConnectionStringRedactor.ExtractSecrets(
                "Server=h;User ID=synthUser6;Password='syn;thPass6';Database=d");
            Check("extract secrets", secrets.Count == 2 && secrets.Contains("synthUser6") && secrets.Contains("syn;thPass6"));
            Check("extract from malformed does not throw", ConnectionStringRedactor.ExtractSecrets("Server=h;Password=\"x").Count == 0);

            var masker = new SecretMasker();
            masker.Register(secrets);
            string text = masker.Mask("Login failed for user 'synthUser6'. Other 'value' stays.");
            Check("quoted login name masked in message", !text.Contains("synthUser6") && text.Contains("'***'") && text.Contains("'value'"));
            Check("masker without secrets is a no-op", new SecretMasker().Mask("abc 'x'") == "abc 'x'");
            Check("masker null-safe", masker.Mask(null) == null);
        }

        private static void Diagnostics()
        {
            var plain = new InvalidOperationException("boom");
            Check("plain exception: no diagnostics text", SqlDiagnostics.Describe(plain) == string.Empty);

            var inner = new TimeoutException("inner");
            var outer = new Exception("outer", inner);
            SqlDiagnostics.Annotate(inner, "script-execution", "connection-open", 15012, 15020);
            SqlDiagnostics.Annotate(inner, "ignored", "ignored", 1, 1);
            string text = SqlDiagnostics.Describe(outer);
            Check("annotation found through inner exception",
                text.Contains("operation=script-execution") && text.Contains("phase=connection-open")
                && text.Contains("phaseElapsedMs=15012") && text.Contains("totalElapsedMs=15020"));
            Check("first annotation wins", !text.Contains("ignored"));
            Check("annotate/describe null-safe", SqlDiagnostics.Describe(null) == string.Empty);
            SqlDiagnostics.Annotate(null, "a", "b", 0, 0);
            Check("annotation does not alter message", inner.Message == "inner");

            SqlException sql = TryCreateSqlException(53, 7, 20);
            if (sql == null)
            {
                skipped++;
                Console.WriteLine("SKIPPED SqlException fields (SqlException could not be created by reflection)");
                return;
            }
            string described = SqlDiagnostics.Describe(sql);
            Check("SqlException number/state/class reported",
                described.Contains("number=53") && described.Contains("state=7") && described.Contains("class=20"));
            Check("SqlException client connection id reported", described.Contains("clientConnectionId="));
        }

        private static SqlException TryCreateSqlException(int number, byte state, byte errorClass)
        {
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                Type errorType = typeof(SqlError);
                ConstructorInfo errorCtor = errorType.GetConstructors(flags)
                    .FirstOrDefault(c => c.GetParameters().Length == 7);
                if (errorCtor == null) return null;
                object error = errorCtor.Invoke(new object[] { number, state, errorClass, "synthServer", "synthetic message", "", 0 });

                var collection = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), true);
                typeof(SqlErrorCollection).GetMethod("Add", flags).Invoke(collection, new[] { error });

                MethodInfo create = typeof(SqlException).GetMethod(
                    "CreateException", flags, null, new[] { typeof(SqlErrorCollection), typeof(string) }, null);
                return create == null ? null : (SqlException)create.Invoke(null, new object[] { collection, "11.0.0" });
            }
            catch
            {
                return null;
            }
        }

        private static void Check(string name, bool condition)
        {
            if (condition) passed++; else failed++;
            Console.WriteLine("{0} {1}", condition ? "PASS   " : "FAIL   ", name);
        }
    }
}
