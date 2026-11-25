using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityProfilerCSVExporter
{
    [InitializeOnLoad]
    internal static class GitExcludeInstaller
    {
        static GitExcludeInstaller()
        {
            AddRules();
        }

        private static void AddRules()
        {
            try
            {
                var assetsPath = Path.GetFullPath(Application.dataPath);
                var directory = Directory.GetParent(assetsPath);
                string repositoryRoot = null;
                string gitDirectory = null;

                while (directory != null)
                {
                    var gitEntry = Path.Combine(directory.FullName, ".git");
                    if (Directory.Exists(gitEntry))
                    {
                        repositoryRoot = directory.FullName;
                        gitDirectory = Path.GetFullPath(gitEntry);
                        break;
                    }

                    if (File.Exists(gitEntry))
                    {
                        var contents = File.ReadAllText(gitEntry).Trim();
                        const string prefix = "gitdir:";
                        if (contents.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            var path = contents.Substring(prefix.Length).Trim();
                            if (!Path.IsPathRooted(path))
                                path = Path.Combine(directory.FullName, path);

                            repositoryRoot = directory.FullName;
                            gitDirectory = Path.GetFullPath(path);
                            break;
                        }
                    }

                    directory = directory.Parent;
                }

                if (gitDirectory == null)
                    return;

                var commonDirectoryFile = Path.Combine(gitDirectory, "commondir");
                if (File.Exists(commonDirectoryFile))
                {
                    var commonDirectory = File.ReadAllText(commonDirectoryFile).Trim();
                    if (!string.IsNullOrEmpty(commonDirectory))
                        gitDirectory = Path.GetFullPath(Path.Combine(gitDirectory, commonDirectory));
                }

                var relativeAssetsPath = Path.GetRelativePath(repositoryRoot, assetsPath)
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/');
                var packagePath = relativeAssetsPath + "/UnityProfilerCSVExporter";
                AddMissingRules(
                    Path.Combine(gitDirectory, "info", "exclude"),
                    new[] { "/" + packagePath + "/", "/" + packagePath + ".meta" });
            }
            catch (IOException exception)
            {
                Debug.LogWarning("Could not exclude Profiler CSV Exporter from Git: " + exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                Debug.LogWarning("Could not exclude Profiler CSV Exporter from Git: " + exception.Message);
            }
            catch (ArgumentException exception)
            {
                Debug.LogWarning("Could not exclude Profiler CSV Exporter from Git: " + exception.Message);
            }
            catch (NotSupportedException exception)
            {
                Debug.LogWarning("Could not exclude Profiler CSV Exporter from Git: " + exception.Message);
            }
        }

        private static void AddMissingRules(string path, IEnumerable<string> rules)
        {
            var contents = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            var missingRules = new List<string>();
            foreach (var rule in rules)
            {
                if (!ContainsRule(contents, rule))
                    missingRules.Add(rule);
            }

            if (missingRules.Count == 0)
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var prefix = contents.Length == 0 || contents.EndsWith("\n", StringComparison.Ordinal)
                ? string.Empty
                : Environment.NewLine;
            File.AppendAllText(path, prefix + string.Join(Environment.NewLine, missingRules) + Environment.NewLine);
            Debug.Log("Profiler CSV Exporter added its Assets folder to this repository's local Git exclude file.");
        }

        private static bool ContainsRule(string contents, string rule)
        {
            using (var reader = new StringReader(contents))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.Equals(line.Trim(), rule, StringComparison.Ordinal))
                        return true;
                }
            }

            return false;
        }
    }
}
