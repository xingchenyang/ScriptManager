using System;
using System.Configuration;
using ScriptRunner.Helper;

namespace ScriptRunner
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                #region configuration initialization and parameter validation

                // Initialize log4net.
                log4net.Config.XmlConfigurator.Configure();

                LogHelper.LogInfo("");
                LogHelper.LogInfo("---------------------------------------------------");
                LogHelper.LogInfo("Script runner Start");

                if (args == null)
                {
                    PrintUsage();
                    return;
                }

                string connectionStringCode = string.Empty;
                string sqlPath = null;
                string sqlFile = null;
                string csFile = null;

                // read parameters
                for (int i = 0; i < args.Length; i += 2)
                {
                    var command = args[i];
                    var value = args[i + 1];

                    if (command == "/csName")
                        connectionStringCode = value;
                    if (command == "/sqlPath")
                        sqlPath = value;
                    if (command == "/sql")
                        sqlFile = value;
                    if (command == "/csFile")
                        csFile = value;
                }
                #endregion

                // mandatory parameters check
                if (string.IsNullOrEmpty(connectionStringCode) || (string.IsNullOrEmpty(sqlPath) && string.IsNullOrEmpty(sqlFile)))
                {
                    PrintUsage();
                    return;
                }

                // run scripts
                var runner = new ScriptRunner()
                {
                    ConnectionStringCode = connectionStringCode,
                    SqlPath = sqlPath,
                    SqlFile = sqlFile, 
                    CsFile = csFile
                };
                runner.RunScripts();

            }
            catch (Exception ex)
            {
                LogHelper.LogAndInfo("ERROR : " + ex.FormatForLog());
            }
        }

        private static void PrintUsage()
        {
            LogHelper.LogAndInfo("USAGE: ScriptRunner.exe /csName connectionStringName [/sqlPath pathToSqlDirectory] [/sql sqlFile] [/csFile pathToConfFile]");
            LogHelper.LogAndInfo("ex: ScriptRunner.exe /csName NameOfCS /sqlPath \"../../../../SQL/\" /csFile \"../../AgendisConfig/VS/Database.Config\" ");
            LogHelper.LogAndInfo("one of sqlPath or sql parameter is required");
            LogHelper.LogAndInfo("PARAMETERS: ");
            LogHelper.LogAndInfo(" - sqlPath: path of scripts folder to be run");
            LogHelper.LogAndInfo(" - sql: path to slq file to run");
            LogHelper.LogAndInfo(" - csFile: path to connection strings file (defaut ./Config/Database.config)");
            LogHelper.LogAndInfo(" - csName: name of connection string to be used (mandatory)");
            LogHelper.LogAndInfo("   --> Connection strings in default conf file are: ");
            for (int i = 0; i < ConfigurationManager.ConnectionStrings.Count; i++)
                LogHelper.LogAndInfo("      - " + ConfigurationManager.ConnectionStrings[i].Name);
        }

    }
}
