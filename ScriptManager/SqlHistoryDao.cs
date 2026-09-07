using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using ScriptManager.Helper;
using ScriptManager.Model;

namespace ScriptManager
{
    public static class SqlHistoryDao
    {

        public const string LogTableName = "dbo.HistoriqueScriptSql";

        public static void CreateLogTableIfNotExists(string connectionString)
        {

            try
            {
                using (var conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    var cmd = new SqlCommand("select 1 from " + LogTableName, conn);
                    cmd.ExecuteNonQuery();
                    conn.Close();
                }
            }
            catch (Exception ex)
            {
                if (ex is SqlException)
                {
                    // Create the table when it does not exist.
                    LogHelper.LogAndInfo("WARNING : table " + LogTableName + " does not exists and will be created");
                    try
                    {
                        using (var conn = new SqlConnection(connectionString))
                        {
                            conn.Open();
                            var cmd = new SqlCommand("CREATE TABLE " + LogTableName + "(DateExecution DATETIME2, NomScript VARCHAR(500), MessageErreur VARCHAR(MAX))", conn);
                            cmd.ExecuteNonQuery();
                            conn.Close();
                        }
                    }
                    catch (Exception e)
                    {
                        LogHelper.LogAndInfo("ERROR while creating table " + LogTableName);
                        LogHelper.LogError(e.FormatForLog());
                        throw;
                    }
                }
                else
                {
                    LogHelper.LogAndInfo("ERROR : cannot read table " + LogTableName);
                    LogHelper.LogError(ex.FormatForLog());
                    throw;
                }
            }
        }

        public static List<HistoriqueScriptSql> ListScriptsDejaPasses(string connectionString)
        {
            var scriptsDejaPasses = new List<HistoriqueScriptSql>();
            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();
                var cmd = new SqlCommand("select * from " + LogTableName, conn);
                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        scriptsDejaPasses.Add(new HistoriqueScriptSql
                        {
                            DateExecution = (DateTime) rdr["DateExecution"],
                            MessageErreur = Convert.ToString(rdr["MessageErreur"]),
                            NomScript = Convert.ToString(rdr["NomScript"])
                        });
                    }
                }
                conn.Close();
            }
            return scriptsDejaPasses;
        }

        public static void InsertLog(string fichier, string messageErreur, string connectionString)
        {
            // Insert the execution result into the script log table.
            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();
                SqlCommand cmdInsert = new SqlCommand(@"
                            INSERT INTO " + LogTableName + @"(DateExecution, NomScript, MessageErreur)
                            VALUES (getdate(), '" + FileHelper.FormatFileString(fichier.Replace("'", "''")) + "', '" + messageErreur.Replace("'", "''") + @"') ", conn);
                cmdInsert.ExecuteNonQuery();
                conn.Close();
            }
        }

            
    }
}
