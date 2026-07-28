using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Common;
using FieldDataPluginFramework;
using log4net;
using ServiceStack;
using ServiceStack.Text;
using ILog = log4net.ILog;

namespace PluginPackager
{
    public class Packager
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public Context Context { get; set; }
        private IFieldDataPlugin Plugin { get; set; }

        public void CreatePackage()
        {
            ResolveDefaults();

            CreateOutputPackage();
        }

        private void ResolveDefaults()
        {
            ResolveAssemblyFolder();
            ResolveAssemblyPath();
            ResolveAssemblyQualifiedTypeName();
            ResolveDeployedFolderName();
            ResolveDescription();
        }

        private void ResolveAssemblyFolder()
        {
            if (!string.IsNullOrEmpty(Context.AssemblyFolder))
            {
                if (!Directory.Exists(Context.AssemblyFolder))
                    throw new ExpectedException($"'{Context.AssemblyFolder}' is not a valid directory.");

                return;
            }

            if (!File.Exists(Context.AssemblyPath))
                throw new ExpectedException($"'{Context.AssemblyPath}' is not a valid file.");

            var file = new FileInfo(Context.AssemblyPath);

            Context.AssemblyFolder = file.DirectoryName;

            if (string.IsNullOrEmpty(Context.AssemblyFolder) || !Directory.Exists(Context.AssemblyFolder))
                throw new Exception($"Can't infer existing folder from '{Context.AssemblyPath}'");
        }

        private void ResolveAssemblyPath()
        {
            var path = !string.IsNullOrEmpty(Context.AssemblyPath)
                ? Context.AssemblyPath
                : Context.AssemblyFolder;

            var pluginLoader = new PluginLoader {Log = Log4NetLogger.Create(Log)};

            var loadedPlugin = pluginLoader.LoadPlugins(new List<string> {path}).Single();

            Plugin = loadedPlugin.Plugin;

            if (string.IsNullOrEmpty(Context.AssemblyPath))
                Context.AssemblyPath = Plugin.GetType().GetAssemblyPath();
        }

        private void ResolveAssemblyQualifiedTypeName()
        {
            if (!string.IsNullOrWhiteSpace(Context.AssemblyQualifiedTypeName))
                return;

            Context.AssemblyQualifiedTypeName = Plugin.GetType().AssemblyQualifiedName;
        }

        private void ResolveDeployedFolderName()
        {
            if (!string.IsNullOrWhiteSpace(Context.DeployedFolderName))
                return;

            var filename = Path.GetFileNameWithoutExtension(Context.AssemblyPath);

            if (string.IsNullOrEmpty(filename))
                throw new Exception($"Can't parse filename from '{Context.AssemblyPath}'");

            var tidiedName = new Regex(@"(plugin|plug-in)", RegexOptions.IgnoreCase).Replace(filename, string.Empty);

            Context.DeployedFolderName = tidiedName.Split(new[] {'.'}, StringSplitOptions.RemoveEmptyEntries).Last();
        }

        private void ResolveDescription()
        {
            if (!string.IsNullOrWhiteSpace(Context.Description))
                return;

            Context.Description = $"The {Context.DeployedFolderName} plugin";
        }

        private void CreateOutputPackage()
        {
            var directory = Path.GetDirectoryName(Context.OutputPath);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Log.Info($"Creating '{Context.OutputPath}' ...");

            File.Delete(Context.OutputPath);

            using (var zipArchive = ZipFile.Open(Context.OutputPath, ZipArchiveMode.Create))
            {
                Log.Info($"Adding {PluginManifest.EntryName} ...");
                var manifestEntry = zipArchive.CreateEntry(PluginManifest.EntryName);
                using (var writer = new StreamWriter(manifestEntry.Open()))
                {
                    var manifest = CreateManifest();

                    writer.Write(manifest.ToJson().IndentJson());
                }

                var assemblyFolder = new DirectoryInfo(Context.AssemblyFolder);
                var includeRegexes = Context.Include.Select(CreateRegexFromDosPattern).ToList();
                var excludeRegexes = Context.Exclude.Select(CreateRegexFromDosPattern).ToList();

                foreach (var file in assemblyFolder.GetFiles("*", SearchOption.AllDirectories))
                {
                    var filename = file.Name;
                    var relativePath = file.FullName.Substring(assemblyFolder.FullName.Length + 1);

                    if (excludeRegexes.Any(r => r.IsMatch(filename)))
                    {
                        Log.Info($"Excluding '{relativePath}' ...");
                        continue;
                    }

                    if (includeRegexes.Any() && !includeRegexes.Any(r => r.IsMatch(filename)))
                    {
                        Log.Info($"Skipping '{relativePath}' ...");
                        continue;
                    }

                    Log.Info($"Adding '{relativePath}' ...");
                    zipArchive.CreateEntryFromFile(file.FullName, relativePath);
                }
            }

            Log.Info($"Successfully created '{Context.OutputPath}'.");
        }

        private PluginManifest CreateManifest()
        {
            return new PluginManifest
            {
                AssemblyQualifiedTypeName = Context.AssemblyQualifiedTypeName,
                Description = Context.Description,
                PluginFolderName = Context.DeployedFolderName
            };
        }

        private static Regex CreateRegexFromDosPattern(string pattern)
        {
            if (pattern.EndsWith(".*"))
                pattern = pattern.Substring(0, pattern.Length - 2);

            pattern = pattern
                .Replace(".", "\\.")
                .Replace("*", ".*");

            return new Regex(pattern, RegexOptions.IgnoreCase);
        }
    }
}
