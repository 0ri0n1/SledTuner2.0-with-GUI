using System.IO;
using System.Text;

namespace SledTunerProject
{
    public static class Utilities
    {
        /// <summary>
        /// Sanitizes a raw string for use as a filename by removing "(" and ")" 
        /// and replacing spaces, colons, forward slashes, and backslashes with underscores.
        /// </summary>
        /// <param name="rawName">The raw input string.</param>
        /// <returns>A safe filename string.</returns>
        public static string MakeSafeFileName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName))
                return "UnknownSled";

            // Remove parentheses
            string safe = rawName.Replace("(", "").Replace(")", "");
            // Replace spaces, colons, forward slashes, and backslashes with underscores
            safe = safe.Replace(" ", "_")
                       .Replace(":", "_")
                       .Replace("/", "_")
                       .Replace("\\", "_");

            return safe;
        }
    }
}
