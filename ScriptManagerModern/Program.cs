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
                    switch (args[i].ToLowerInvariant())
                    {
                        case "/csname": options.ConnectionStringName = args[i + 1]; break;
                        case "/sqlpath": options.SqlPath = args[i + 1]; break;
                        case "/envcode": options.EnvironmentName = args[i + 1]; break;
                        case "/csfile": options.ConnectionStringFile = args[i + 1]; break;
                        case "/disablescriptdiff": options.DisableScriptDiff = args[i + 1] == "1"; break;
                        case "/version": options.Version = args[i + 1]; break;
                        default:
                            Console.Error.WriteLine("Unknown parameter: " + args[i]);
                            return PrintUsage();
                    }
                }

                if (string.IsNullOrWhiteSpace(options.ConnectionStringName) ||
                    string.IsNullOrWhiteSpace(options.SqlPath))
                    return PrintUsage();

                return new ScriptManager(options).Run();
            }
            catch (Exception ex)
            {
                Log.Error("Unhandled error", ex);
                return 1;
            }
        }

        private static int PrintUsage()
        {
            Console.WriteLine("USAGE: ScriptManager.exe /csName name [/sqlPath path] [/envCode code] [/csFile file] [/disableScriptDiff 1] [/version value]");
            return 2;
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
            Logger.Error(message, exception);
            Console.Error.WriteLine(message + ": " + exception.Message);
        }
    }
}
