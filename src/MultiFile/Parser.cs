using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Common;
using FieldDataPluginFramework;
using FieldDataPluginFramework.Context;
using FieldDataPluginFramework.Results;
using FieldDataPluginFramework.Serialization;
using ServiceStack;

namespace MultiFile
{
    public class Parser
    {
        public Parser(ILog logger, IFieldDataResultsAppender resultsAppender)
        {
            // LogExtensions.DebugEnabled = true;
            Log = logger;
            ResultsAppender = new DelayedAppender(resultsAppender);
        }

        private ILog Log { get; }
        private DelayedAppender ResultsAppender { get; }

        public ParseFileResult Parse(Stream stream, LocationInfo locationInfo = null)
        {
            try
            {
                var zipArchive = GetZipArchiveOrNull(stream);

                if (zipArchive == null)
                    return ParseFileResult.CannotParse();

                using (zipArchive)
                {
                    if (zipArchive.Entries.Any(e => e.Name != e.FullName))
                        return ParseFileResult.CannotParse();

                    if (!zipArchive.Entries.Any())
                        return ParseFileResult.CannotParse();

                    var plugins = LoadPlugins();

                    if (!plugins.Any())
                        return ParseFileResult.CannotParse();

                    var result = ParseArchive(zipArchive, locationInfo, plugins);

                    if (result.Status != ParseFileStatus.SuccessfullyParsedAndDataValid)
                    {
                        return result;
                    }

                    // Now append all the visits
                    ResultsAppender.AppendAllResults();

                    return ParseFileResult.SuccessfullyParsedAndDataValid();
                }
            }
            catch (Exception exception)
            {
                return ParseFileResult.SuccessfullyParsedButDataInvalid(exception);
            }
        }

        private ZipArchive GetZipArchiveOrNull(Stream fileStream)
        {
            try
            {
                return new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: true);
            }
            catch (InvalidDataException)
            {
                return null;
            }
        }

        private List<PluginLoader.LoadedPlugin> LoadPlugins()
        {
            lock (SyncObject)
            {
                if (CachedPlugins.Any())
                    return CachedPlugins;

                var config = LoadConfig();

                var pluginPaths = config
                    .Plugins
                    .Where(p => !string.IsNullOrWhiteSpace(p.Path))
                    .Select(p => p.Path)
                    .ToList();

                var allPrefixedSettings = config
                    .Plugins
                    .Where(p => string.IsNullOrWhiteSpace(p.Path))
                    .Select(p => p.Settings)
                    .ToList();

                if (pluginPaths.All(string.IsNullOrWhiteSpace))
                {
                    // There are no paths to other plugins explicitly configured, but there may be some plugin settings
                    pluginPaths = GetAllOtherPluginFolders();
                }

                var loadedPlugins = new PluginLoader
                    {
                        Log = Log,
                        Verbose = config.Verbose,
                    }
                    .LoadPlugins(pluginPaths);

                foreach (var loadedPlugin in loadedPlugins)
                {
                    var pluginConfig = config
                        .Plugins
                        .FirstOrDefault(p =>
                            loadedPlugin.Path.Equals(p.Path, StringComparison.InvariantCultureIgnoreCase)
                            || Path.GetFileName(loadedPlugin.Path).Equals(p.Path, StringComparison.InvariantCultureIgnoreCase));

                    loadedPlugin.PluginPriority = pluginConfig?.PluginPriority ?? int.MaxValue;

                    if (pluginConfig != null && pluginConfig.Settings.Any())
                    {
                        // This plugin has some specific settings, which we should us as-is
                        loadedPlugin.Settings = pluginConfig.Settings;
                        continue;
                    }

                    var keyPrefix = GetPluginFolderName(loadedPlugin) + "-";

                    var prefixedSettings = allPrefixedSettings
                        .FirstOrDefault(s => s.Keys.All(k => k.StartsWith(keyPrefix, StringComparison.InvariantCultureIgnoreCase)));

                    // Strip the prefix from the settings
                    loadedPlugin.Settings = prefixedSettings?.ToDictionary(
                                                kvp => kvp.Key.Substring(keyPrefix.Length),
                                                kvp => kvp.Value)
                                            ?? new Dictionary<string, string>();
                }

                CachedPlugins.Clear();
                CachedPlugins.AddRange(loadedPlugins);

                return CachedPlugins;
            }
        }

        private string GetPluginFolderName(PluginLoader.LoadedPlugin loadedPlugin)
        {
            if (File.Exists(loadedPlugin.Path))
                return Path.GetFileNameWithoutExtension(loadedPlugin.Path);

            var pluginType = loadedPlugin.Plugin.GetType();
            var pluginFolderName = pluginType.FullName;

            if (string.IsNullOrEmpty(pluginFolderName))
                return pluginType.FullName;

            var suffixToStrip = ".plugin";

            if (!pluginFolderName.EndsWith(suffixToStrip, StringComparison.InvariantCultureIgnoreCase))
                return pluginFolderName;

            return pluginFolderName.Substring(0, pluginFolderName.Length - suffixToStrip.Length);
        }

        private List<string> GetAllOtherPluginFolders()
        {
            var pluginDirectory = GetPluginDirectory();

            if (!Directory.Exists(pluginDirectory))
                return GetAdjacentPluginPaths()
                    .ToList();

            var pluginFolder = new DirectoryInfo(pluginDirectory);
            var parentFolder = pluginFolder.Parent;

            // ReSharper disable once PossibleNullReferenceException
            var sourceFolders = parentFolder
                .GetDirectories()
                .Where(di => IsPeerPluginFolder(di, pluginFolder))
                .ToList();

            var pluginCopyRoot = new DirectoryInfo(Path.Combine(pluginFolder.FullName, "PluginCopies"));

            pluginCopyRoot.Create();

            var copiesToDelete = pluginCopyRoot
                .GetDirectories()
                .Where(copy => IsStalePluginCopy(copy, sourceFolders))
                .ToList();

            foreach (var copy in copiesToDelete)
            {
                Log.Info($"Deleting stale plugin copy '{copy.FullName}'");
                copy.Delete(true);
            }

            return sourceFolders
                .Select(source =>
                {
                    var copyPath = Path.Combine(pluginCopyRoot.FullName, source.Name);

                    if (!Directory.Exists(copyPath))
                    {
                        CopyFolder(source, copyPath);
                    }

                    return copyPath;
                })
                .ToList();
        }

        private static bool IsPeerPluginFolder(DirectoryInfo folder, DirectoryInfo pluginFolder)
        {
            return !ExcludedFolderNames.Contains(folder.Name)
                   && folder.FullName != pluginFolder.FullName
                   && folder.GetFiles("*.dll").Any();
        }

        private static readonly HashSet<string> ExcludedFolderNames = new HashSet<string>
        {
            "Library",  // Ignore the framework folder on AQTS app servers
            "Release",  // Ignore the common bin folders when run from the Visual Studio build folder
            "Debug"
        };

        private bool IsStalePluginCopy(DirectoryInfo copy, List<DirectoryInfo> sourceFolders)
        {
            if (sourceFolders.All(source => source.Name != copy.Name))
            {
                // There is no matching folder in the source plugin collection
                return true;
            }

            var sourceFolder = sourceFolders
                .First(source => source.Name == copy.Name);

            var sourceFiles = GetAllFiles(sourceFolder);
            var copyFiles = GetAllFiles(copy);

            if (sourceFiles.Count != copyFiles.Count)
            {
                // The file list is different
                return true;
            }

            return copyFiles.Any(file =>
            {
                if (!sourceFiles.TryGetValue(file.Key, out var sourceFile))
                {
                    // The file doesn't exist in the source
                    return true;
                }

                var copyFile = file.Value;

                if (sourceFile.Length != copyFile.Length)
                {
                    // Source file length has changed
                    return true;
                }

                if (sourceFile.LastWriteTimeUtc > copyFile.LastWriteTimeUtc)
                {
                    // Source file has been updated since the last copy
                    return true;
                }

                // No file change detected
                return false;
            });
        }

        private static Dictionary<string,FileInfo> GetAllFiles(DirectoryInfo folder)
        {
            var files = folder.GetFiles("*", SearchOption.AllDirectories);

            return files
                .ToDictionary(
                    f => f.FullName.Substring(folder.FullName.Length + 1),
                    f => f,
                    StringComparer.InvariantCultureIgnoreCase);
        }

        private void CopyFolder(DirectoryInfo source, string copyPath)
        {
            Log.Info($"Copying plugin from '{source.FullName}' to '{copyPath}'");

            Directory.CreateDirectory(copyPath);

            // Copy all the directories first
            foreach (var dir in source.GetDirectories("*", SearchOption.AllDirectories))
            {
                var copySubfolder = Path.Combine(copyPath, dir.FullName.Substring(source.FullName.Length + 1));
                Log.Debug($"Creating dir '{copySubfolder}'");
                Directory.CreateDirectory(copySubfolder);
            }

            // Now copy the files
            foreach (var file in source.GetFiles("*", SearchOption.AllDirectories))
            {
                var copyFile = Path.Combine(copyPath, file.FullName.Substring(source.FullName.Length + 1));

                Log.Debug($"Copying file '{file.FullName}' to '{copyFile}'");
                File.Copy(file.FullName, copyFile);
            }
        }

        private IEnumerable<string> GetAdjacentPluginPaths()
        {
            if (!File.Exists(PluginLoader.MainPluginPath))
                yield break;

            var mainPluginInfo = new FileInfo(PluginLoader.MainPluginPath);

            // ReSharper disable once PossibleNullReferenceException
            foreach (var otherPluginInfo in mainPluginInfo.Directory.GetFiles("*.plugin"))
            {
                if (otherPluginInfo.FullName == mainPluginInfo.FullName)
                    continue;

                yield return otherPluginInfo.FullName;
            }
        }

        private static readonly object SyncObject = new object();

        private static readonly List<PluginLoader.LoadedPlugin> CachedPlugins = new List<PluginLoader.LoadedPlugin>();

        private Config LoadConfig()
        {
            JsonConfig.Configure();

            if (!ResultsAppender.GetFullPluginConfigurations().TryGetValue(nameof(Config), out var configJsonText) || string.IsNullOrWhiteSpace(configJsonText))
                return GetDefaultConfig();

            try
            {
                return configJsonText.FromJson<Config>();
            }
            catch (Exception e)
            {
                Log.Error($"Can't load configuration: {e.Message}: JSON={configJsonText}");

                return GetDefaultConfig();
            }
        }

        private static Config GetDefaultConfig()
        {
            return new Config();
        }

        private string GetPluginDirectory()
        {
            var assembly = GetType().Assembly;

            // assembly.Location works when the plugin is running on an app server (fully extracted to its own folder)
            // What about when run from a .plugin file (in-memory ZIP?) (For PluginTester.exe: assembly.CodeBase = FULLPATH(PluginTester.exe) && assembly.Location = ""
            var location = assembly.Location;

            return string.IsNullOrWhiteSpace(location)
                ? null
                : Path.GetDirectoryName(location);
        }

        private ParseFileResult ParseArchive(ZipArchive zipArchive, LocationInfo locationInfo, List<PluginLoader.LoadedPlugin> plugins)
        {
            foreach (var entry in zipArchive.Entries)
            {
                var isParsed = false;

                foreach (var plugin in plugins.OrderBy(p => p.PluginPriority))
                {
                    var result = ParseEntry(entry, plugin, locationInfo);

                    if (result.Status == ParseFileStatus.CannotParse)
                        continue;

                    if (result.Status != ParseFileStatus.SuccessfullyParsedAndDataValid)
                    {
                        Log.Error($"Plugin {PluginLoader.GetPluginNameAndVersion(plugin.Plugin)} failed to parse '{entry.FullName}' with {result.Status}('{result.ErrorMessage}')");
                        return result;
                    }

                    isParsed = true;
                    break;
                }

                if (!isParsed)
                    return ParseFileResult.CannotParse();
            }

            // All entries were parsed by one of the plugins
            return ParseFileResult.SuccessfullyParsedAndDataValid();
        }

        private ParseFileResult ParseEntry(ZipArchiveEntry entry, PluginLoader.LoadedPlugin loadedPlugin, LocationInfo locationInfo)
        {
            ResultsAppender.FilterConfigurationSettings(loadedPlugin);

            using (var entryStream = entry.Open())
            using (var reader = new BinaryReader(entryStream))
            using (var memoryStream = new MemoryStream(reader.ReadBytes((int)entry.Length)))
            {
                var proxyLog = ProxyLog.Create(Log, loadedPlugin.Plugin, entry);

                var result = locationInfo == null
                    ? loadedPlugin.Plugin.ParseFile(memoryStream, ResultsAppender, proxyLog)
                    : loadedPlugin.Plugin.ParseFile(memoryStream, locationInfo, ResultsAppender, proxyLog);

                switch (result.Status)
                {
                    case ParseFileStatus.CannotParse:
                        result = ParseFileResult.CannotParse($"{proxyLog.Prefix}: {result.ErrorMessage}");
                        break;

                    case ParseFileStatus.SuccessfullyParsedButDataInvalid:
                        result = ParseFileResult.SuccessfullyParsedButDataInvalid($"{proxyLog.Prefix}: {result.ErrorMessage}");
                        break;
                }

                return result;
            }
        }
    }
}
