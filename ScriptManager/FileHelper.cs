using System.IO;

namespace ScriptRunner
{
    public class FileHelper
    {

        /// <summary>
        /// Read content of file, utf8 only
        /// </summary>
        /// <param name="fichier">Path of the file to read.</param>
        /// <returns></returns>
        public static string GetFileContent(string fichier)
        {
            // Read the file as UTF-8.
            FileInfo file = new FileInfo(fichier);
            return file.OpenText().ReadToEnd();
        }


        public static string FormatFileString(string input)
        {
            return input.Replace("\\", "/").Replace("//", "/").Replace("//", "/");
        }
    }
}
