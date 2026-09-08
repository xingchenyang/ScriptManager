using System;
using System.Configuration;

namespace ScriptManager
{
    internal static class Program
    {
        private const string DefaultSqlPath = "SQL/";

        private static int Main(string[] args)
        {
            log4net.Config.XmlConfigurator.Configure();
            try
            {
                if (args == null || args.Length % 2 != 0)
                    return PrintUsage();

                var options = new RunnerOptions { SqlPath = DefaultSqlPath };
                for (int i = 0; i < args.Length; i += 2)
                {
                    switch (args[i])
                    {
                        case "/csName": options.ConnectionStringName = args[i + 1]; break;
                        case "/sqlPath": options.SqlPath = args[i + 1]; break;
                        case "/envCode": options.EnvironmentName = args[i + 1]; break;
                        case "/csFile": options.ConnectionStringFile = args[i + 1]; break;
                        case "/disableScriptDiff": options.DisableScriptDiff = args[i + 1] == "1"; break;
                        case "/version": options.Version = args[i + 1]; break;
                    }
                }

                if (string.IsNullOrEmpty(options.ConnectionStringName) ||
                    string.IsNullOrEmpty(options.SqlPath))
                    return PrintUsage();

                if (!ConfirmLaunch())
                {
                    Console.WriteLine("Lancement annule.");
                    return 3;
                }

                return new ScriptManager(options).Run();
            }
            catch (Exception ex)
            {
                Log.Error("Unhandled error", ex);
                return 0;
            }
        }

        private static int PrintUsage()
        {
            Console.WriteLine("USAGE: ScriptManager.exe /csName name [/sqlPath path] [/envCode code] [/csFile file] [/disableScriptDiff 1] [/version value]");
            return 0;
        }

        private static bool ConfirmLaunch()
        {
            Console.Write("Saisissez O pour confirmer le lancement : ");
            string confirmation = Console.ReadLine();
            return string.Equals(confirmation?.Trim(), "O", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class RunnerOptions
    {
        internal string ConnectionStringName;
        internal string SqlPath;
        internal string EnvironmentName;
        internal string ConnectionStringFile;
        internal bool DisableScriptDiff;
        internal string Version;
    }

    internal static class Log
    {
        private static readonly log4net.ILog Logger = log4net.LogManager.GetLogger(typeof(Program));

        internal static void Info(string message)
        {
            Logger.Info(message);
            Console.WriteLine(message);
        }

        internal static void Error(string message, Exception exception)
        {
            string output = message + " " + FormatException(exception);
            Logger.Error(output);
            Console.Error.WriteLine(output);
        }

        internal static string FormatException(Exception exception)
        {
            var output = new System.Text.StringBuilder();
            while (exception != null)
            {
                output.AppendLine("Server: " + Environment.MachineName);
                output.AppendLine("ExceptionType: " + exception.GetType());
                output.AppendLine("Message: " + exception.Message);
                output.AppendLine("Source: " + exception.Source);
                output.AppendLine("Target site: " + Convert.ToString(exception.TargetSite));
                output.AppendLine(exception.StackTrace);
                exception = exception.InnerException;
                if (exception != null)
                    output.Append("Nested Exception----");
            }
            return output.ToString();
        }
    }
}
