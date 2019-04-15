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
                    var pluginFolder = new DirectoryInfo(GetPluginDirectory());
                    var parentFolder = pluginFolder.Parent;

                    // ReSharper disable once PossibleNullReferenceException
                    pluginPaths = parentFolder
                        .GetDirectories()
                        .Where(di => !ExcludedFolderNames.Contains(di.Name) && di.FullName != parentFolder.FullName)
                        .Select(di => di.FullName)
                        .ToList();
                }

                CachedPlugins.Clear();
                CachedPlugins.AddRange(new PluginLoader()
                    .LoadPlugins(pluginPaths));

                return CachedPlugins;
            }
        }

        private static readonly HashSet<string> ExcludedFolderNames = new HashSet<string>
        {
            "Library",  // Ignore the framework folder on AQTS app servers
            "Release",  // Ignore the common bin folders when run from the Visual Studio build folder
            "Debug"
        };

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
                        return ParseFileResult.CannotParse();
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
                return locationInfo == null
                    ? plugin.ParseFile(memoryStream, ResultsAppender, Log)
                    : plugin.ParseFile(memoryStream, locationInfo, ResultsAppender, Log);
            }
        }
    }
}
