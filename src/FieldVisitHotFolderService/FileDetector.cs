using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Aquarius.TimeSeries.Client;
using Aquarius.TimeSeries.Client.ServiceModels.Provisioning;
using Common;
using FieldDataPluginFramework;
using FieldDataPluginFramework.Context;
using log4net;
using ILog = log4net.ILog;

namespace FieldVisitHotFolderService
{
    public class FileDetector
    {
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
        private List<IFieldDataPlugin> Plugins { get; set; }
        private string JsonPluginPath { get; set; }
        private AquariusServerVersion JsonPluginVersion { get; set; }
        private IAquariusClient Client { get; set; }
        private List<LocationInfo> LocationCache { get; set; }
        private int ProcessedFileCount { get; set; }
        public Action CancellationAction { get; set; }

        public void Run()
        {
            WaitForStableServerVersion();

            ConnectAndThrowIfJsonPluginNotInstalled();

            while (!CancellationToken.IsCancellationRequested)
            {
                Validate();
                ProcessNewFiles();
                WaitForNewFiles();
            }
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

            Log.Info($"{Plugins.Count} local plugins ready for parsing field data files.");

            foreach (var plugin in Plugins)
            {
                Log.Info($"{PluginLoader.GetPluginNameAndVersion(plugin)}");
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
            var localPluginLoader = new LocalPluginLoader();

            Plugins = localPluginLoader
                .LoadPlugins();

            JsonPluginPath = localPluginLoader.JsonPluginPath;
            JsonPluginVersion = localPluginLoader.JsonPluginVersion;
        }

        private IAquariusClient CreateConnectedClient()
        {
            Log.Info($"{FileHelper.ExeNameAndVersion} connecting to {Context.Server} as '{Context.Username}' ...");

            var client = AquariusClient.CreateConnectedClient(Context.Server, Context.Username, Context.Password);

            Log.Info($"Connected to {Context.Server} (v{client.ServerVersion})");

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

                if (Context.MergeMode == MergeMode.AllowSameDayVisits && serverVersion.IsLessThan(MinimumVersionSupportingSameDayVisits))
                    throw new ExpectedException($"{Context.Server} (v{serverVersion}) is below the minimum required version of v{MinimumVersionSupportingSameDayVisits} required to support /{nameof(Context.MergeMode)}={MergeMode.AllowSameDayVisits}.");

                return;
            }
        }

        private static readonly AquariusServerVersion MinimumVersion = AquariusServerVersion.Create("19.2");
        private static readonly AquariusServerVersion MinimumVersionSupportingSameDayVisits = AquariusServerVersion.Create("2020.1");

        private void ConnectAndThrowIfJsonPluginNotInstalled()
        {
            if (CancellationToken.IsCancellationRequested)
                return;

            using (Client = CreateConnectedClient())
            {
                LoadLocalPlugins();
                ThrowIfJsonPluginNotInstalled();
            }

            Client = null;
        }

        private void ThrowIfJsonPluginNotInstalled()
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
                InstallJsonPlugin();
                return;
            }

            if (!serverPluginVersion?.IsLessThan(JsonPluginVersion) ?? false)
                // The server JSON plugin is newer, so keep it
                return;

            ReplaceJsonPlugin(serverPlugin);
        }

        private FieldDataPlugin GetServerPlugin(Func<FieldDataPlugin,bool> predicate)
        {
            var plugins = Client.Provisioning.Get(new GetFieldDataPlugins())
                .Results;

            return plugins
                .FirstOrDefault(predicate);
        }

        private void InstallJsonPlugin(int? pluginPriority = null)
        {
            if (string.IsNullOrEmpty(JsonPluginPath))
                throw new ExpectedException($"The JSON field data plugin is not installed on {Context.Server}.\nDownload the latest plugin from https://github.com/AquaticInformatics/aquarius-field-data-framework/releases");

            Log.Info($"Installing JSON field data plugin v{JsonPluginVersion} from '{JsonPluginPath}' ...");

            var serverPlugin = Client.Provisioning.PostFileWithRequest(JsonPluginPath, new PostFieldDataPluginFile
            {
                PluginPriority = pluginPriority ?? 0
            });

            Log.Info($"Successfully installed v{PluginLoader.GetPluginVersion(serverPlugin.AssemblyQualifiedTypeName)} {serverPlugin.PluginFolderName}: {nameof(serverPlugin.PluginPriority)}={serverPlugin.PluginPriority}");
        }

        private void ReplaceJsonPlugin(FieldDataPlugin serverPlugin)
        {
            Log.Info($"Removing v{PluginLoader.GetPluginVersion(serverPlugin.AssemblyQualifiedTypeName)} {serverPlugin.PluginFolderName} ...");

            Client.Provisioning.Delete(new DeleteFieldDataPlugin {UniqueId = serverPlugin.UniqueId});

            InstallJsonPlugin(serverPlugin.PluginPriority);
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
                    ThrowIfJsonPluginNotInstalled();

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
        }

        private List<string> GetNewFiles()
        {
            return Directory.GetFiles(SourceFolder)
                .Where(f => FileMasks.Any(m => m.IsMatch(f)) && !StatusIndicator.FilesToIgnore.Contains(Path.GetFileName(f)))
                .ToList();
        }

        public void ProcessFile(string filename)
        {
            var sourcePath = Path.Combine(SourceFolder, filename);

            if (!File.Exists(sourcePath))
                throw new ExpectedException($"'{sourcePath}' no longer exists");

            new FileProcessor
                {
                    Context = Context,
                    Client = Client,
                    LocationCache = LocationCache,
                    Plugins = Plugins,
                    ProcessingFolder = ProcessingFolder,
                    PartialFolder = PartialFolder,
                    ArchivedFolder = ArchivedFolder,
                    UploadedFolder = UploadedFolder,
                    FailedFolder = FailedFolder,
                    CancellationToken = CancellationToken
                }
                .ProcessFile(sourcePath);
        }

        private void WaitForNewFiles()
        {
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
