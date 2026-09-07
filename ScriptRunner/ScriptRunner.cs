using ScriptRunner.Helper;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace ScriptRunner
{
    public class ScriptRunner
    {
        internal string ConnectionStringCode = string.Empty;
        internal string ConnectionStringValue = string.Empty;
        internal string SqlPath = string.Empty;
        internal string SqlFile = string.Empty;
        internal string CsFile = null;
        public string SqlOutput = null;
        public bool Verbose = true;

        internal void RunScripts()
        {
            // Resolve the connection string.
            string connectionString = ConnectionStringValue;

            if (string.IsNullOrEmpty(connectionString))
            {
                if (!string.IsNullOrEmpty(CsFile))
                {
                    // Read the named connection string from the supplied file.
                    var doc = XDocument.Load(CsFile);
                    foreach (var el in doc.Root.Elements())
                    {
                        if (el.Name != "add")
                            continue;
                        var name = el.Attribute("name");
                        if (name?.Value != ConnectionStringCode)
                            continue;
                        connectionString = el.Attribute("connectionString")?.Value;
                        break;
                    }

                    if (string.IsNullOrEmpty(connectionString))
                    {
                        Console.WriteLine("Error: No connection string " + ConnectionStringCode + " found in file " +
                                          CsFile);
                        return;
                    }
                }
                else
                {
                    if (ConfigurationManager.ConnectionStrings.Count == 0)
                    {
                        Console.WriteLine("Error: No connection string have been found in Config/Database.config");
                        return;
                    }

                    connectionString = ConfigurationManager.ConnectionStrings[ConnectionStringCode].ConnectionString;
                }

                if (connectionString.Contains("provider connection string=\""))
                    connectionString = connectionString.Split(new[] {"\""}, StringSplitOptions.None)[1];
            }

            if (Verbose)
            {
                Console.WriteLine("Using the following connection string: ");
                Console.WriteLine(connectionString);
            }

            // list scripts files to be run, with the correct order 
            List<string> listFichiers = new List<string>();

            if (!string.IsNullOrEmpty(SqlPath))
            {
                try
                {
                    listFichiers = ScriptDetector.FindFilesInDirectory(SqlPath);
                }
                catch (Exception ex)
                {
                    LogHelper.LogAndInfo("ERROR : " + ex.FormatForLog());
                    return;
                }
            }
            else if (!string.IsNullOrEmpty(SqlFile))
            {
                listFichiers.Add(SqlFile);
            }

            if (listFichiers == null || !listFichiers.Any(x => !string.IsNullOrEmpty(x)))
            {
                LogHelper.LogAndInfo("ERROR: No file to run");
                return;
            }

            #region run scripts and log execution

            int nbScript = 0, nbErreurs = 0, nbSucces = 0;
            foreach (string fichier in listFichiers)
            {
                nbScript++;
                LogHelper.LogAndInfo("RUN SCRIPT " + fichier);
                string messageErreur = string.Empty;
                try
                {
                    string scriptContent = FileHelper.GetFileContent(fichier);

                    using (SqlConnection connection = new SqlConnection(connectionString))
                    {
                        connection.Open();
                        //connection.InfoMessage += OnInfoMessageGenerated; // to view output
                        //connection.FireInfoMessageEventOnUserErrors = true;
                        using (var command = new SqlCommand(scriptContent, connection))
                        {
                            using (var adapter = new SqlDataAdapter(command))
                            {
                                using (var set = new DataSet())
                                {
                                    adapter.Fill(set);

                                    // save result eventually
                                    if (listFichiers.Count == 1 && !string.IsNullOrEmpty(SqlOutput)
                                        && set != null && set.Tables.Count > 0)
                                    {
                                        // TODO manage CSV properly, JSON, flat text
                                        const string columnSeparator = "\t";
                                        const string rowSeparator = "\r\n";
                                        using (var file = new StreamWriter(SqlOutput))
                                        {
                                            foreach (DataRow row in set.Tables[0].Rows)
                                            {
                                                var line = string.Join(columnSeparator, row.ItemArray) + rowSeparator;
                                                file.WriteLine(line);
                                            }
                                        }
                                    }
                                }

                            }
                        }
                    }
                    nbSucces++;
                }
                catch (Exception ex)
                {
                    messageErreur = ex.FormatForLog();
                    LogHelper.LogAndInfo("ERROR running script " + fichier + " " + messageErreur);
                    nbErreurs++;
                }
            }

            LogHelper.LogAndInfo(string.Format("FINISHED : {0} scripts run, {1} success and {2} errors", nbScript, nbSucces, nbErreurs));


            #endregion
        }

        private void OnInfoMessageGenerated(object sender, SqlInfoMessageEventArgs args)
        {
            foreach (SqlError err in args.Errors)
            {
                LogHelper.LogAndInfo(string.Format("Msg {0}, Level {1}, State {2}, Line {3}", err.Number, err.Class, err.State, err.LineNumber));
                LogHelper.LogAndInfo(string.Format("{0}", err.Message));
            }
        }
    }
}
