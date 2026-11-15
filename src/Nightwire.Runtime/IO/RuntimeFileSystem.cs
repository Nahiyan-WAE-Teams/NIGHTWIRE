using System;
using System.IO;

namespace Nightwire.Runtime.IO
{
    /// <summary>
    /// Small filesystem compatibility helpers for the portable .NET Standard 2.1 runtime.
    /// </summary>
    internal static class RuntimeFileSystem
    {
        /// <summary>
        /// Publishes a file, replacing an existing destination through the platform replace operation.
        /// No delete-then-move fallback is used because that would create a data-loss window.
        /// </summary>
        public static void MoveFileReplacing(string sourcePath, string destinationPath)
        {
            if (File.Exists(destinationPath))
                File.Replace(sourcePath, destinationPath, null);
            else
                File.Move(sourcePath, destinationPath);
        }

        public static string TrimEndingDirectorySeparator(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string root = Path.GetPathRoot(fullPath) ?? string.Empty;
            while (fullPath.Length > root.Length &&
                   (fullPath[fullPath.Length - 1] == Path.DirectorySeparatorChar ||
                    fullPath[fullPath.Length - 1] == Path.AltDirectorySeparatorChar))
            {
                fullPath = fullPath.Substring(0, fullPath.Length - 1);
            }
            return fullPath;
        }
    }
}
