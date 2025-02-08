using System.IO;
using System.Text;

namespace SledTunerProject
{
    public static class Utilities
    {
        /// <summary>
        /// Sanitizes a raw string for use as a filename by replacing invalid characters with underscores.
        /// </summary>
        public static string MakeSafeFileName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName))
                return "UnknownSled";

            char[] invalidChars = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(rawName);
            foreach (char c in invalidChars)
            {
                sb.Replace(c, '_');
            }

            string safe = sb.ToString().Trim();
            return string.IsNullOrEmpty(safe) ? "UnknownSled" : safe;
        }
    }
}
