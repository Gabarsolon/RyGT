using System;
using System.IO;

namespace RyGT
{
    static class LogFiles
    {
        internal static readonly string[] Levels = { "Trace", "Debug", "Info", "Warning", "Error", "Critical" };
        internal static int LevelIndex(string value)
        {
            for (int i = 0; i < Levels.Length; ++i)
                if (String.Equals(value, Levels[i], StringComparison.OrdinalIgnoreCase)) return i;
            return 3; // Warning is the default, including invalid settings.
        }

        internal static string ArchiveName(string directory, string name, string stamp)
        {
            string path = Path.Combine(directory, name + "." + stamp);
            for (int i = 1; File.Exists(path); ++i)
                path = Path.Combine(directory, name + "." + stamp + "." + i);
            return path;
        }

        internal static string Prepare(string root, string name)
        {
            string directory = Path.Combine(root, "logs");
            Directory.CreateDirectory(directory);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", System.Globalization.CultureInfo.InvariantCulture);
            string legacy = Path.Combine(root, name);
            if (File.Exists(legacy)) File.Move(legacy, ArchiveName(directory, name, stamp + ".legacy"));
            string current = Path.Combine(directory, name);
            if (File.Exists(current)) File.Move(current, ArchiveName(directory, name, stamp));
            return current;
        }

    }
}
