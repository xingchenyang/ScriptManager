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
                string connectionStringValue = string.Empty;
                string sqlPath = null;
                string sqlFile = null;
                string csFile = null;
                bool verbose = true;
                string sqlOutput = null;

                // read parameters
                for (int i = 0; i < args.Length; i += 2)
                {
                    var command = args[i];
                    var value = args[i + 1];

                    if (command == "/csName")
                        connectionStringCode = value;
                    if (command == "/cs")
                        connectionStringValue = value;
                    if (command == "/sqlPath")
                        sqlPath = value;
                    if (command == "/sql")
                        sqlFile = value;
                    if (command == "/csFile")
                        csFile = value;
                    if (command == "/sqlOutput")
                        sqlOutput = value;
                    if (command == "/verbose")
                        verbose = value == "1";
                }
                #endregion

                // mandatory parameters check
                if ((string.IsNullOrEmpty(connectionStringCode) && string.IsNullOrEmpty(connectionStringValue)) || 
                    (string.IsNullOrEmpty(sqlPath) && string.IsNullOrEmpty(sqlFile)))
                {
                    PrintUsage();
                    return;
                }

                // run scripts
                var runner = new ScriptRunner
                {
                    ConnectionStringCode = connectionStringCode,
                    ConnectionStringValue = connectionStringValue, 
                    SqlPath = sqlPath,
                    SqlFile = sqlFile, 
                    CsFile = csFile, 
                    SqlOutput = sqlOutput, 
                    Verbose = verbose
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
            LogHelper.LogAndInfo("USAGE: ScriptRunner.exe "+ 
                                 "[/cs connectionString] [/csName connectionStringName] [/csFile pathToConfFile] " +
                                 "[/sqlPath pathToSqlDirectory] [[/sql sqlFile] [/sqlOutput outputFile]] " + 
                                 "[/verbose 1/0]");
            LogHelper.LogAndInfo("ex: ScriptManager.exe /csName NameOfCS /sqlPath \"../../../../SQL/\" /csFile \"../../Config/VS/Database.Config\" ");
            LogHelper.LogAndInfo("one of sqlPath or sql parameter is required");
            LogHelper.LogAndInfo("PARAMETERS: ");
            LogHelper.LogAndInfo(" - cs: connection string");
            LogHelper.LogAndInfo(" - csName: name of connection string to be used");
            LogHelper.LogAndInfo(" - csFile: path to connection strings file (defaut ./Config/Database.config)");
            LogHelper.LogAndInfo(" - sqlPath: path of scripts folder to be run");
            LogHelper.LogAndInfo(" - sql: path to slq file to run");
            LogHelper.LogAndInfo(" - sqlOutput: File to store output of query");
            LogHelper.LogAndInfo(" - verbose: 1 to show extended information");
            LogHelper.LogAndInfo(" --> one of cs, csName must be set");
            LogHelper.LogAndInfo(" --> one of sqlPath, sql must be set");
            LogHelper.LogAndInfo(" --> Files must be UTF-8 encoded");
        }

    }
}
