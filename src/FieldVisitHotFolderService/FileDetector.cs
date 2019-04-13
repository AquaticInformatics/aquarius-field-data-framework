using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Aquarius.TimeSeries.Client;
using Aquarius.TimeSeries.Client.ServiceModels.Acquisition;
using Aquarius.TimeSeries.Client.ServiceModels.Provisioning;
using Aquarius.TimeSeries.Client.ServiceModels.Publish;
using Common;
using FieldDataPluginFramework;
using FieldDataPluginFramework.Context;
using FieldDataPluginFramework.Results;
using FieldDataPluginFramework.Serialization;
using log4net;
using ServiceStack;
using FieldDataPlugin = Aquarius.TimeSeries.Client.ServiceModels.Provisioning.FieldDataPlugin;
using ILog = log4net.ILog;

namespace FieldVisitHotFolderService
{
    public class FileDetector
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public Context Context { get; set; }
        public CancellationToken CancellationToken { get; set; }

        public void Run()
        {
            Validate();

            WaitForStableServerVersion();

            if (CancellationToken.IsCancellationRequested)
                return;

            ThrowIfJsonPluginNotInstalled();

            while (!CancellationToken.IsCancellationRequested)
            {
                ProcessNewFiles();
                WaitForNewFiles();
            }
        }

        private string SourceFolder { get; set; }
        private string ProcessingFolder { get; set; }
        private string PartialFolder { get; set; }
        private string UploadedFolder { get; set; }
        private string FailedFolder { get; set; }
        private List<Regex> FileMasks { get; set; }
        private List<IFieldDataPlugin> Plugins { get; set; }

        private void Validate()
        {
            if (string.IsNullOrWhiteSpace(Context.HotFolderPath))
                throw new ExpectedException($"You must specify a /{nameof(Context.HotFolderPath)} option.");

            Context.HotFolderPath = ResolvePath(FileHelper.ExeDirectory, Context.HotFolderPath);

            SourceFolder = Context.HotFolderPath;

            ThrowIfFolderIsMissing(SourceFolder);

            if (!Context.Plugins.Any())
                throw new ExpectedException($"You must specify a /Plugin option.");

            ProcessingFolder = CreateFolderPath(Context.ProcessingFolder);
            PartialFolder = CreateFolderPath(Context.PartialFolder);
            UploadedFolder = CreateFolderPath(Context.UploadedFolder);
            FailedFolder = CreateFolderPath(Context.FailedFolder);

            FileMasks = (Context.FileMask ?? "*.*")
                .Split(FileMaskDelimiters, StringSplitOptions.RemoveEmptyEntries)
                .Where(mask => !string.IsNullOrWhiteSpace(mask))
                .Select(CreateRegexFromDosWildcard)
                .ToList();

            Plugins = new PluginLoader()
                .LoadPlugins(Context.Plugins);

            Log.Info($"{Plugins.Count} local plugins ready for parsing field data files.");

            foreach (var plugin in Plugins)
            {
                Log.Info($"{plugin.GetType().AssemblyQualifiedName}");
            }
        }

        private static readonly char[] FileMaskDelimiters = {','};

        private static Regex CreateRegexFromDosWildcard(string mask)
        {
            return new Regex(
                $@"^{mask.Replace(".", "\\.").Replace("*", ".*")}$",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        }

        private string CreateFolderPath(string relativeOrAbsolutePath)
        {
            var path = ResolveSourceFolderPath(relativeOrAbsolutePath);

            if (!Directory.Exists(path))
            {
                Log.Info($"Creating '{path}'");
                Directory.CreateDirectory(path);
            }

            return path;
        }

        private string ResolveSourceFolderPath(string relativeOrAbsolutePath)
        {
            return ResolvePath(SourceFolder, relativeOrAbsolutePath);
        }

        private static string ResolvePath(string sourcePath, string relativeOrAbsolutePath)
        {
            return Path.IsPathRooted(relativeOrAbsolutePath)
                ? relativeOrAbsolutePath
                : Path.Combine(sourcePath, relativeOrAbsolutePath);
        }

        private void ThrowIfFolderIsMissing(string path)
        {
            if (!Directory.Exists(path))
                throw new ExpectedException($"'{path}' is not an existing folder.");
        }

        private IAquariusClient CreateConnectedClient()
        {
            Log.Info($"{FileHelper.ExeNameAndVersion} connecting to {Context.Server} as '{Context.Username}'");

            var client = AquariusClient.CreateConnectedClient(Context.Server, Context.Username, Context.Password);

            return client;
        }

        private void WaitForStableServerVersion()
        {
            var systemDetector = AquariusSystemDetector.Instance;
            systemDetector.Reset();

            for (var connectionAttempt = 1; ; ++connectionAttempt)
            {
                var serverType = systemDetector.GetAquariusServerType(Context.Server);

                if (serverType == AquariusServerType.Unknown)
                {
                    Log.Warn($"{Context.Server} is offline. Waiting {Context.ConnectionRetryDelay} before attempting next connection.");
                    CancellationToken.WaitHandle.WaitOne(Context.ConnectionRetryDelay);

                    if (CancellationToken.IsCancellationRequested)
                        return;

                    if (Context.MaximumConnectionAttempts > 0 && connectionAttempt >= Context.MaximumConnectionAttempts)
                        throw new ExpectedException($"Can't connect to {Context.Server} after {Context.MaximumConnectionAttempts} attempts.");

                    Log.Info($"Re-connecting with {Context.Server}");
                    continue;
                }

                var serverVersion = systemDetector.GetAquariusServerVersion(Context.Server);

                if (serverVersion.IsLessThan(MinimumVersion))
                    throw new ExpectedException($"{Context.Server} (v{serverVersion}) is below the minimum required version of v{MinimumVersion}");

                return;
            }
        }

        private static readonly AquariusServerVersion MinimumVersion = AquariusServerVersion.Create("18.4");

        private IAquariusClient Client { get; set; }

        private void ThrowIfJsonPluginNotInstalled()
        {
            using (Client = CreateConnectedClient())
            {
                GetInstalledJsonPlugin();
            }

            Client = null;
        }

        private FieldDataPlugin GetInstalledJsonPlugin()
        {
            var plugins = Client.Provisioning.Get(new GetFieldDataPlugins())
                .Results;

            var jsonPlugin = plugins
                .FirstOrDefault(p => p.AssemblyQualifiedTypeName.StartsWith("JsonFieldData.Plugin"));

            if (jsonPlugin == null)
                throw new ExpectedException($"The JSON field data plugin is not installed on {Context.Server}.\nDownload the latest plugin from https://github.com/AquaticInformatics/json-field-data-plugin/releases");

            return jsonPlugin;
        }

        private List<LocationInfo> LocationCache { get; set; }

        private void ProcessNewFiles()
        {
            for (var files = GetNewFiles(); files.Any(); files = GetNewFiles())
            {
                Log.Info($"Processing {files.Count} files");

                WaitForStableServerVersion();

                if (CancellationToken.IsCancellationRequested)
                    return;

                LocationCache = new List<LocationInfo>();

                using (Client = CreateConnectedClient())
                {
                    foreach (var file in files)
                    {
                        if (CancellationToken.IsCancellationRequested)
                            return;

                        ProcessFile(file);
                    }
                }

                Client = null;
            }
        }

        private List<string> GetNewFiles()
        {
            return Directory.GetFiles(SourceFolder)
                .Where(f => FileMasks.Any(m => m.IsMatch(f)))
                .ToList();
        }

        private void ProcessFile(string filename)
        {
            var sourcePath = Path.Combine(SourceFolder, filename);

            if (!File.Exists(sourcePath))
                throw new ExpectedException($"'{sourcePath}' no longer exists");

            string processingPath;

            try
            {
                processingPath = MoveFile(sourcePath, ProcessingFolder);
            }
            catch (IOException exception)
            {
                Log.Warn($"Skipping '{sourcePath}", exception);
                return;
            }

            try
            {
                var appendedResults = ParseLocalFile(processingPath);
                var isPartial = UploadResults(processingPath, appendedResults);

                if (isPartial)
                    MoveFile(processingPath, PartialFolder);
                else
                    MoveFile(processingPath, UploadedFolder);
            }
            catch (Exception exception)
            {
                Log.Error(exception.Message);

                try
                {
                    MoveFile(processingPath, FailedFolder);
                }
                catch (Exception moveException)
                {
                    Log.Warn($"Can't move '{processingPath}' to '{FailedFolder}'", moveException);
                }
            }
        }

        private string MoveFile(string path, string targetFolder)
        {
            var extension = Path.GetExtension(path);
            var baseFilename = Path.GetFileNameWithoutExtension(path);
            string targetPath;

            for(var attempt = 0;;++attempt)
            {
                var filename = attempt > 0
                               ? $"{baseFilename} ({attempt}){extension}"
                               : $"{baseFilename}{extension}";

                targetPath = Path.Combine(targetFolder, filename);

                if (!File.Exists(targetPath))
                    break;
            }

            Log.Info($"Moving '{path}' to '{targetPath}'");
            File.Move(path, targetPath);

            return targetPath;
        }

        private AppendedResults ParseLocalFile(string path)
        {
            using (var stream = LoadDataStream(path))
            {
                var appender = new FieldDataResultsAppender
                {
                    Client = Client,
                    LocationCache = LocationCache
                };

                foreach (var plugin in Plugins)
                {
                    var pluginName = plugin.GetType().FullName;

                    try
                    {
                        var logger = Log4NetLogger.Create(LogManager.GetLogger(plugin.GetType()));

                        var result = plugin.ParseFile(CloneMemoryStream(stream), appender, logger);

                        // TODO: Support Zip-with-attachments

                        if (result.Status == ParseFileStatus.CannotParse)
                            continue;

                        if (result.Status != ParseFileStatus.SuccessfullyParsedAndDataValid)
                            throw new ArgumentException($"Error parsing '{path}' with {pluginName}: {result.ErrorMessage}");

                        if (!appender.AppendedResults.AppendedVisits.Any())
                            throw new ArgumentException($"{pluginName} did not parse any field visits.");

                        Log.Info($"{pluginName} parsed '{path}' with {appender.AppendedResults.AppendedVisits.Count} visits: {string.Join(", ", appender.AppendedResults.AppendedVisits.Select(v => v.FieldVisitIdentifier))}");

                        appender.AppendedResults.PluginAssemblyQualifiedTypeName = plugin.GetType().AssemblyQualifiedName;
                        return appender.AppendedResults;
                    }
                    catch (Exception e)
                    {
                        Log.Warn($"{pluginName} skipping '{path}': {e.Message}");
                    }
                }
            }

            throw new ArgumentException($"'{path}' was not parsed by any plugin.");
        }

        private MemoryStream LoadDataStream(string path)
        {
            if (!File.Exists(path))
                throw new ExpectedException($"Data file '{path}' does not exist.");

            Log.Info($"Loading data file '{path}'");

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new BinaryReader(stream, Encoding.Default, true))
            {
                return new MemoryStream(reader.ReadBytes((int)stream.Length));
            }
        }

        private MemoryStream CloneMemoryStream(MemoryStream source)
        {
            return new MemoryStream(source.ToArray());
        }

        private bool UploadResults(string path, AppendedResults appendedResults)
        {
            var isPartial = false;

            foreach (var visit in appendedResults.AppendedVisits)
            {
                var singleResult = new AppendedResults
                {
                    FrameworkAssemblyQualifiedName = appendedResults.FrameworkAssemblyQualifiedName,
                    PluginAssemblyQualifiedTypeName = appendedResults.PluginAssemblyQualifiedTypeName,
                    AppendedVisits = new List<FieldVisitInfo> {visit}
                };

                if (DoConflictingVisitsExist(visit))
                {
                    isPartial = true;
                    continue;
                }

                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(singleResult.ToJson())))
                {
                    var uploadedFilename = Path.GetFileName(path) + ".json";

                    var response = Client.Acquisition.PostFileWithRequest(stream, uploadedFilename, new PostVisitFile
                    {
                        LocationUniqueId = Guid.Parse(visit.LocationInfo.UniqueId)
                    });

                    Log.Info($"Uploaded '{uploadedFilename}' to '{visit.LocationInfo.LocationIdentifier}' using {response.HandledByPlugin.Name} plugin");
                }
            }

            return isPartial;
        }

        private bool DoConflictingVisitsExist(FieldVisitInfo visit)
        {
            var existingVisits = Client.Publish.Get(new FieldVisitDescriptionListServiceRequest
                {
                    LocationIdentifier = visit.LocationInfo.LocationIdentifier,
                    QueryFrom = StartOfDay(visit.StartDate),
                    QueryTo = EndOfDay(visit.EndDate)
                })
                .FieldVisitDescriptions;

            if (existingVisits.Any())
            {
                Log.Warn($"Skipping conflicting visit {visit.FieldVisitIdentifier} with {string.Join(", ", existingVisits.Select(v => $"{v.StartTime}/{v.EndTime}"))}");
                return true;
            }

            return false;
        }

        private static DateTimeOffset StartOfDay(DateTimeOffset dateTimeOffset)
        {
            return new DateTimeOffset(dateTimeOffset.Date, dateTimeOffset.Offset);
        }

        private static DateTimeOffset EndOfDay(DateTimeOffset dateTimeOffset)
        {
            var start = StartOfDay(dateTimeOffset);

            return new DateTimeOffset(start.Year, start.Month, start.Day, 23,59, 59, start.Offset);
        }

        private void WaitForNewFiles()
        {
            if (CancellationToken.IsCancellationRequested)
                return;

            var timeSpan = Context.FileQuietDelay;
            Log.Info($"Waiting {timeSpan} for new files at '{SourceFolder}'");
            CancellationToken.WaitHandle.WaitOne(timeSpan);
        }
    }
}
