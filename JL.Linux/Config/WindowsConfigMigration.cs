using System.Text.RegularExpressions;
using JL.Core;

namespace JL.Linux.Config;

/// <summary>
/// Windows JL writes relative paths with backslashes ("Resources\\JMdict.xml").
/// On Linux a backslash is an ordinary filename character, so a config copied over
/// from Windows would point at files that don't exist. This rewrites relative
/// "Path" values in dicts.json / freqs.json to use forward slashes, once, before
/// JL.Core reads them. Absolute Windows paths (C:\...) are left alone.
/// </summary>
internal static partial class WindowsConfigMigration
{
    [GeneratedRegex("""("path"\s*:\s*")((?:[^"\\]|\\.)*)(")""", RegexOptions.IgnoreCase)]
    private static partial Regex PathPropertyRegex();

    [GeneratedRegex("""^[A-Za-z]:""")]
    private static partial Regex DriveLetterRegex();

    public static void Run()
    {
        string[] fileNames = ["dicts.json", "freqs.json"];
        foreach (string fileName in fileNames)
        {
            string file = Path.Join(AppInfo.ConfigPath, fileName);
            try
            {
                if (!File.Exists(file))
                {
                    continue;
                }

                string json = File.ReadAllText(file);
                string updated = PathPropertyRegex().Replace(json, static m =>
                {
                    string value = m.Groups[2].Value;
                    // In JSON text a single backslash is written as "\\".
                    if (!value.Contains(@"\\", StringComparison.Ordinal) || DriveLetterRegex().IsMatch(value) || value.StartsWith(@"\\\\", StringComparison.Ordinal))
                    {
                        return m.Value;
                    }

                    return m.Groups[1].Value + value.Replace(@"\\", "/", StringComparison.Ordinal) + m.Groups[3].Value;
                });

                if (updated != json)
                {
                    File.Copy(file, file + ".windows.bak", true);
                    File.WriteAllText(file, updated);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Couldn't check {file} for Windows paths: {ex.Message}");
            }
        }
    }
}
