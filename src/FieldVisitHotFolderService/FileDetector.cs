using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Aquarius.TimeSeries.Client;
using Aquarius.TimeSeries.Client.ServiceModels.Provisioning;
using Common;
using FieldDataPluginFramework.Context;
using log4net;
using MigrationProject;
using ILog = log4net.ILog;

namespace FieldVisitHotFolderService
{
    public class FileDetector
    {
        // ReSharper disable once PossibleNullReferenceException
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public Context Context { get; set; }
        public CancellationToken CancellationToken { get; set; }
        public StatusIndicator StatusIndicator { get; set; }

        private string SourceFolder { get; set; }
        private List<Regex> FileMasks { get; set; }
        private string ProcessingFolder { get; set; }
        private string PartialFolder { get; set; }
        private string ArchivedFolder { get; set; }
        private string UploadedFolder { get; set; }
        private string FailedFolder { get; set; }
        private List<PluginLoader.LoadedPlugin> LoadedPlugins { get; set; }
        private AquariusServerVersion JsonPluginVersion { get; set; }
        private IAquariusClient Client { get; set; }
        private List<LocationInfo> LocationCache { get; set; }
        private ReferencePointCache ReferencePointCache { get; set; }
        private MethodLookup MethodLookup { get; set; }
        private ParameterIdLookup ParameterIdLookup { get; set; }

        private int ProcessedFileCount { get; set; }
        public Action CancellationAction { get; set; }
        public string[] StartArgs { get; set; }

        public void Run()
        {
            WaitForStableServerVersion();

            ConnectAndThrowIfJsonPluginNotInstalled();

            if (IsExporting())
            {
                ExportExistingVisits();
                return;
            }

            while (!CancellationToken.IsCancellationRequested)
            {
                Validate();
                ProcessNewFiles();
                WaitForNewFiles();
            }
        }

        private bool IsExporting()
        {
            return !string.IsNullOrEmpty(Context.ExportFolder);
        }

        private void ExportExistingVisits()
        {
            using (Client = CreateConnectedClient())
            {
                new Exporter
                    {
                        Context = Context,
                        Client = Client,
                        ReferencePointCache = ReferencePointCache,
                        ParameterIdLookup = ParameterIdLookup,
                        MethodLookup = MethodLookup,
                        Plugins = LoadedPlugins
                            .Select(lp => lp.Plugin)
                            .ToList(),
                    }
                    .Run();
            }
        }

        private void ReparseArgs()
        {
            Program.GetContext(Context, StartArgs);
        }

        private void Validate()
        {
            if (string.IsNullOrWhiteSpace(Context.HotFolderPath))
                throw new ExpectedException($"You must specify a /{nameof(Context.HotFolderPath)} option.");

            Context.HotFolderPath = ResolvePath(FileHelper.ExeDirectory, Context.HotFolderPath);

            SourceFolder = Context.HotFolderPath;

            ThrowIfFolderIsMissing(SourceFolder);

            StatusIndicator.Activate(SourceFolder);

            ProcessingFolder = ResolveSourceFolderPath(Context.ProcessingFolder);
            PartialFolder = ResolveSourceFolderPath(Context.PartialFolder);
            ArchivedFolder = ResolveSourceFolderPath(Context.ArchivedFolder);
            UploadedFolder = ResolveSourceFolderPath(Context.UploadedFolder);
            FailedFolder = ResolveSourceFolderPath(Context.FailedFolder);

            FileMasks = (Context.FileMask ?? "*.*")
                .Split(FileMaskDelimiters, StringSplitOptions.RemoveEmptyEntries)
                .Where(mask => !string.IsNullOrWhiteSpace(mask))
                .Select(CreateRegexFromDosWildcard)
                .ToList();

            LoadLocalPlugins();

            Log.Info($"{LoadedPlugins.Count} local plugins ready for parsing field data files.");

            foreach (var loadedPlugin in LoadedPlugins)
            {
                Log.Info($"{PluginLoader.GetPluginNameAndVersion(loadedPlugin.Plugin)}");
            }
        }

        private static readonly char[] FileMaskDelimiters = {',', ';'};

        private static Regex CreateRegexFromDosWildcard(string mask)
        {
            return new Regex(
                $@"^{mask.Replace(".", "\\.").Replace("*", ".*")}$",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
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

        private void LoadLocalPlugins()
        {
            FieldDataPluginFramework.Serialization.JsonConfig.Configure();

            var localPluginLoader = new LocalPluginLoader
            {
                Verbose = Context.Verbose
            };

            LoadedPlugins = localPluginLoader
                .LoadPlugins();

            SortPluginsByPriority();

            JsonPluginVersion = localPluginLoader.JsonPluginVersion;
        }

        private void SortPluginsByPriority()
        {
            if (Client == null)
                return;

            var serverPlugins = Client.Provisioning.Get(new GetFieldDataPlugins()).Results;

            LoadedPlugins = LoadedPlugins
                .OrderBy(loadedPlugin =>
                {
                    var pluginFolderName = loadedPlugin.Manifest.PluginFolderName;

                    if (Context.PluginPriority.TryGetValue(pluginFolderName, out var priority))
                        return priority;

                    var serverPlugin = serverPlugins
                        .FirstOrDefault(p =>
                            p.PluginFolderName.Equals(pluginFolderName, StringComparison.InvariantCultureIgnoreCase));

                    return serverPlugin?.PluginPriority ?? int.MaxValue;
                })
                .ThenBy(loadedPlugin => loadedPlugin.Manifest.PluginFolderName)
                .ToList();
        }

        private IAquariusClient CreateConnectedClient()
        {
            Log.Info($"{FileHelper.ExeNameAndVersion} connecting to {Context.Server} as '{Context.Username}' ...");

            var client = AquariusClient.CreateConnectedClient(Context.Server, Context.Username, Context.Password);

            Log.Info($"Connected to {Context.Server} (v{client.ServerVersion})");

            ReferencePointCache = new ReferencePointCache(client);

            ParameterIdLookup = new ParameterIdLookup(client.Provisioning.Get(new GetParameters()));

            MethodLookup = new MethodLookup(client.Provisioning.Get(new GetMonitoringMethods()));

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

        private static readonly AquariusServerVersion MinimumVersion = AquariusServerVersion.Create("20.3");

        private void ConnectAndThrowIfJsonPluginNotInstalled()
        {
            if (CancellationToken.IsCancellationRequested)
                return;

            using (Client = CreateConnectedClient())
            {
                LoadLocalPlugins();
                ThrowIfJsonPluginNotInstalledOrOlder();
            }

            Client = null;
        }

        private void ThrowIfJsonPluginNotInstalledOrOlder()
        {
            var serverPlugin = GetServerPlugin(LocalPluginLoader.IsJsonPlugin);

            var serverPluginVersion = serverPlugin != null
                ? AquariusServerVersion.Create(PluginLoader.GetPluginVersion(serverPlugin.AssemblyQualifiedTypeName))
                : null;

            if (JsonPluginVersion == null && serverPluginVersion != null)
                // There is no local Json plugin to deploy, so assume the server plugin will be good enough
                return;

            if (serverPlugin == null)
            {
	            ThrowJsonPluginNotInstalledMessage();
            }

            if (!serverPluginVersion?.IsLessThan(JsonPluginVersion) ?? false)
            {
                // The server JSON plugin is newer, so keep it
                EnableJsonPlugin(serverPlugin);
                return;
            }

            throw new ExpectedException("The JSON field data plugin on server is older than the local plugin. " +
                                        $"Please update the server JSON field data plugin to version {JsonPluginVersion}");
        }

		private void ThrowJsonPluginNotInstalledMessage()
		{
			throw new ExpectedException($"The JSON field data plugin is not installed on {Context.Server}." +
			                            "Contact your system admin and install the latest JSON field data plugin.");
		}

		private FieldDataPlugin GetServerPlugin(Func<FieldDataPlugin,bool> predicate)
        {
            var plugins = Client.Provisioning.Get(new GetFieldDataPlugins())
                .Results;

            return plugins
                .FirstOrDefault(predicate);
        }

		private void EnableJsonPlugin(FieldDataPlugin serverPlugin)
        {
            if (serverPlugin.IsEnabled)
                return;

            Log.Info($"Enabling v{PluginLoader.GetPluginVersion(serverPlugin.AssemblyQualifiedTypeName)} {serverPlugin.PluginFolderName} ...");

            Client.Provisioning.Put(new PutFieldDataPlugin
            {
                UniqueId = serverPlugin.UniqueId,
                PluginPriority = serverPlugin.PluginPriority,
                IsEnabled = true
            });
        }

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
                    ThrowIfJsonPluginNotInstalledOrOlder();

                    foreach (var file in files)
                    {
                        if (CancellationToken.IsCancellationRequested)
                            return;

                        ProcessFile(file);

                        ++ProcessedFileCount;

                        if (ProcessedFileCount >= Context.MaximumFileCount)
                        {
                            Log.Info($"Stopping processing after {ProcessedFileCount} files.");
                            CancellationAction();
                            return;
                        }
                    }
                }

                Client = null;
            }

            CancelIfImportZipComplete();
        }

        private List<string> GetNewFiles()
        {
            if (IsProjectImportEnabled())
                return GetFilesFromImportProject();

            return Directory.GetFiles(SourceFolder)
                .Where(f => FileMasks.Any(m => m.IsMatch(f)) && !StatusIndicator.FilesToIgnore.Contains(Path.GetFileName(f)))
                .ToList();
        }

        private bool IsProjectImportEnabled()
        {
            return !string.IsNullOrWhiteSpace(Context.ImportZip);
        }

        private void CancelIfImportZipComplete()
        {
            if (!IsProjectImportEnabled() || CancellationToken.IsCancellationRequested)
                return;

            Log.Info($"Stopping processing after importing all files from '{Context.ImportZip}'");
            CancellationAction();
            Archive?.Dispose();
        }

        private Archive Archive { get; set; }
        private ZipEntryParser ZipEntryParser { get; set; }

        private Dictionary<string, ZipArchiveEntry> ImportZipEntries { get; set; } =
            new Dictionary<string, ZipArchiveEntry>();

        private List<string> GetFilesFromImportProject()
        {
            if (Archive != null)
                return new List<string>();

            if (!File.Exists(Context.ImportZip))
                throw new ExpectedException($"File '{Context.ImportZip}' does not exist.");

            Archive = Archive.OpenForRead(Context.ImportZip, options =>
            {
                options.ValidateOnlyFieldVisitFiles = true;
            });

            ZipEntryParser = new ZipEntryParser(Archive.Project);
            ImportZipEntries = Archive
                .FieldVisitEntries
                .ToDictionary(e => e.FullName, e => e);

            return ImportZipEntries
                .Keys
                .OrderBy(name => name)
                .ToList();
        }

        public void ProcessFile(string filename)
        {
            var processor = new FileProcessor
                {
                    Context = Context,
                    Client = Client,
                    LocationCache = LocationCache,
                    ReferencePointCache = ReferencePointCache,
                    ParameterIdLookup = ParameterIdLookup,
                    MethodLookup = MethodLookup,
                    LoadedPlugins = LoadedPlugins,
                    ProcessingFolder = ProcessingFolder,
                    PartialFolder = PartialFolder,
                    ArchivedFolder = ArchivedFolder,
                    UploadedFolder = UploadedFolder,
                    FailedFolder = FailedFolder,
                    CancellationToken = CancellationToken
                };

            if (ImportZipEntries.TryGetValue(filename, out var zipEntry) && ZipEntryParser.IsFieldVisit(filename, out var locationIdentifier))
            {
                processor.ProcessZipEntry(zipEntry, locationIdentifier);
                return;
            }

            var sourcePath = Path.Combine(SourceFolder, filename);

            if (!File.Exists(sourcePath))
                throw new ExpectedException($"'{sourcePath}' no longer exists");

            processor.ProcessFile(sourcePath);
        }

        private void WaitForNewFiles()
        {
            if (CancellationToken.IsCancellationRequested)
                return;

            var remainingFileStatus = Context.MaximumFileCount.HasValue
                ? $"{Context.MaximumFileCount - ProcessedFileCount} of {Context.MaximumFileCount} "
                : string.Empty;

            var maximumWaitStatus = Context.MaximumFileWaitInterval.HasValue
                ? $"up to {Context.MaximumFileWaitInterval} "
                : string.Empty;

            Log.Info($"Waiting {maximumWaitStatus}for {remainingFileStatus}file changes in '{SourceFolder}' with a scan interval of {Context.FileScanInterval} ...");
            var task = WhenFileCreated();

            var stopwatch = Stopwatch.StartNew();

            while(true)
            {
                if (CancellationToken.IsCancellationRequested)
                    return;

                if (GetNewFiles().Any())
                    break;

                if (stopwatch.Elapsed >= Context.MaximumFileWaitInterval)
                {
                    Log.Info($"Stopping processing after {stopwatch.Elapsed} with no detected file activity.");
                    CancellationAction();
                    return;
                }

                task.Wait((int)Context.FileScanInterval.TotalMilliseconds, CancellationToken);
            }

            var timeSpan = Context.FileQuietDelay;
            Log.Info($"Waiting {timeSpan} for file activity to settle at '{SourceFolder}'");
            CancellationToken.WaitHandle.WaitOne(timeSpan);

            ReparseArgs();
        }

        private Task WhenFileCreated()
        {
            var tcs = new TaskCompletionSource<bool>();
            var watcher = new FileSystemWatcher(SourceFolder);

            void CreatedHandler(object s, FileSystemEventArgs e)
            {
                if (StatusIndicator.FilesToIgnore.Contains(e.Name)) return;

                tcs.TrySetResult(true);
                watcher.Created -= CreatedHandler;
                watcher.Dispose();
            }

            void ChangedHandler(object s, FileSystemEventArgs e)
            {
                if (StatusIndicator.FilesToIgnore.Contains(e.Name)) return;

                tcs.TrySetResult(true);
                watcher.Changed -= ChangedHandler;
                watcher.Dispose();
            }

            void RenamedHandler(object s, RenamedEventArgs e)
            {
                if (StatusIndicator.FilesToIgnore.Contains(e.Name)) return;

                tcs.TrySetResult(true);
                watcher.Renamed -= RenamedHandler;
                watcher.Dispose();
            }

            watcher.Created += CreatedHandler;
            watcher.Changed += ChangedHandler;
            watcher.Renamed += RenamedHandler;

            watcher.EnableRaisingEvents = true;

            return tcs.Task;
        }
    }
}
