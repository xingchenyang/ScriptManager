using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ScriptRunner
{
    public class ScriptDetector
    {

        /// <summary>
        /// Load files in the following order:
        /// 1) files directly inside /SQL;
        /// 2) files inside subdirectories of /SQL.
        /// </summary>
        public static List<string> FindFilesInDirectory(string directory)
        {
            List<string> res = new List<string>();
            string[] fichiers = Directory.GetFiles(directory);
            if (fichiers.Any())
                // Add SQL files in path order. Numeric prefixes such as 002- can control the sequence.
                // Files without a numeric prefix naturally sort according to their full path.
                res.AddRange(OrdonneFichiersDossiers(fichiers.Where(x => x.ToLower().EndsWith(".sql"))));

            string[] repertoires = Directory.GetDirectories(directory);
            foreach (var repertoire in OrdonneFichiersDossiers(repertoires))
                res.AddRange(FindFilesInDirectory(repertoire));
            return res;
        }

        private static IEnumerable<string> OrdonneFichiersDossiers(IEnumerable<string> fichiers)
        {
            return fichiers.Select(FileHelper.FormatFileString).OrderBy(x => x).ToList();
        }

    }
}
