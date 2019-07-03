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

        private List<IFieldDataPlugin> LoadPlugins()
        {
            lock (SyncObject)
            {
                if (CachedPlugins.Any())
                    return CachedPlugins;

                var config = LoadConfig();

                var pluginPaths = config.Plugins ?? new List<string>();

                if (!pluginPaths.Any())
                {
                    pluginPaths = GetAllOtherPluginFolders();
                }

                CachedPlugins.Clear();
                CachedPlugins.AddRange(new PluginLoader
                    {
                        Log = Log
                    }
                    .LoadPlugins(pluginPaths));

                return CachedPlugins;
            }
        }

        private List<string> GetAllOtherPluginFolders()
        {
            var pluginFolder = new DirectoryInfo(GetPluginDirectory());
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

        private static readonly object SyncObject = new object();

        private static readonly List<IFieldDataPlugin> CachedPlugins = new List<IFieldDataPlugin>();

        private Config LoadConfig()
        {
            JsonConfig.Configure();

            var configPath = Path.Combine(
                GetPluginDirectory(),
                $"{nameof(Config)}.json");

            if (!File.Exists(configPath))
                return new Config();

            return File.ReadAllText(configPath).FromJson<Config>();
        }

        private string GetPluginDirectory()
        {
            return Path.GetDirectoryName(GetType().Assembly.Location);
        }

        private ParseFileResult ParseArchive(ZipArchive zipArchive, LocationInfo locationInfo, List<IFieldDataPlugin> plugins)
        {
            foreach (var entry in zipArchive.Entries)
            {
                var isParsed = false;

                foreach (var plugin in plugins)
                {
                    var result = ParseEntry(entry, plugin, locationInfo);

                    if (result.Status == ParseFileStatus.CannotParse)
                        continue;

                    if (result.Status != ParseFileStatus.SuccessfullyParsedAndDataValid)
                    {
                        Log.Error($"Plugin {PluginLoader.GetPluginNameAndVersion(plugin)} failed to parse '{entry.FullName}' with {result.Status}('{result.ErrorMessage}')");
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

        private ParseFileResult ParseEntry(ZipArchiveEntry entry, IFieldDataPlugin plugin, LocationInfo locationInfo)
        {
            using (var entryStream = entry.Open())
            using (var reader = new BinaryReader(entryStream))
            using (var memoryStream = new MemoryStream(reader.ReadBytes((int)entry.Length)))
            {
                var proxyLog = ProxyLog.Create(Log, plugin, entry);

                var result = locationInfo == null
                    ? plugin.ParseFile(memoryStream, ResultsAppender, proxyLog)
                    : plugin.ParseFile(memoryStream, locationInfo, ResultsAppender, proxyLog);

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
