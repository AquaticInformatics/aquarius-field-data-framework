using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Aquarius.TimeSeries.Client;
using Aquarius.TimeSeries.Client.ServiceModels.Provisioning;
using log4net;

namespace FieldVisitHotFolderService
{
    public class FileProcessor
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

            ThrowIfJsonPluginMissing();

            while (!CancellationToken.IsCancellationRequested)
            {
                ProcessNewFiles();
                WaitForNewFiles();
            }
        }

        private string SourceFolder { get; set; }
        private string ProcessingFolder { get; set; }
        private string UploadedFolder { get; set; }
        private string FailedFolder { get; set; }
        private List<Regex> FileMasks { get; set; }

        private void Validate()
        {
            if (string.IsNullOrWhiteSpace(Context.HotFolderPath))
                throw new ExpectedException($"You must specify a /{nameof(Context.HotFolderPath)} option.");

            Context.HotFolderPath = ResolvePath(FileHelper.ExeDirectory, Context.HotFolderPath);

            SourceFolder = Context.HotFolderPath;

            ThrowIfFolderIsMissing(SourceFolder);

            if (string.IsNullOrWhiteSpace(Context.PluginFolder))
                throw new ExpectedException($"You must specify a /{nameof(Context.PluginFolder)} option.");

            Context.PluginFolder = ResolvePath(FileHelper.ExeDirectory, Context.PluginFolder);
            ThrowIfFolderIsMissing(Context.PluginFolder);

            ProcessingFolder = CreateFolderPath(Context.ProcessingFolder);
            UploadedFolder = CreateFolderPath(Context.UploadedFolder);
            FailedFolder = CreateFolderPath(Context.FailedFolder);

            FileMasks = (Context.FileMask ?? "*.*")
                .Split(FileMaskDelimiters, StringSplitOptions.RemoveEmptyEntries)
                .Where(mask => !string.IsNullOrWhiteSpace(mask))
                .Select(CreateRegexFromDosWildcard)
                .ToList();
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

        private void ThrowIfJsonPluginMissing()
        {
            using (var client = CreateConnectedClient())
            {
                var plugins = client.Provisioning.Get(new GetFieldDataPlugins())
                    .Results;

                var jsonPlugin = plugins
                    .FirstOrDefault(p => p.AssemblyQualifiedTypeName.StartsWith("JsonFieldData.Plugin"));

                if (jsonPlugin == null)
                    throw new ExpectedException($"The JSON field data plugin is not installed on {Context.Server}.\nDownload the latest plugin from https://github.com/AquaticInformatics/json-field-data-plugin/releases");
            }
        }

        private void ProcessNewFiles()
        {
            for (var files = GetNewFiles(); files.Any(); files = GetNewFiles())
            {
                Log.Info($"Processing {files.Count} files");

                WaitForStableServerVersion();

                if (CancellationToken.IsCancellationRequested)
                    return;

                LoadLocalPlugins();

                using (Client = CreateConnectedClient())
                {
                    foreach (var file in files)
                    {
                        if (CancellationToken.IsCancellationRequested)
                            return;

                        ProcessFile(file);
                    }
                }
            }
        }

        private List<string> GetNewFiles()
        {
            return Directory.GetFiles(SourceFolder)
                .Where(f => FileMasks.Any(m => m.IsMatch(f)))
                .ToList();
        }

        private void LoadLocalPlugins()
        {
            // TODO: Load *.plugin from the folder, extract them into relative subfolders, find the entry assembly
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
                ParseAndUploadFile(processingPath);
                MoveFile(processingPath, UploadedFolder);
            }
            catch (Exception exception)
            {
                Log.Error($"Can't upload '{processingPath}'", exception);

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

        private void ParseAndUploadFile(string path)
        {
            // TODO: Parse the file using the local plugins
            // TODO: Upload all the visits to AQTS
            Log.Info($"Uploaded '{path}'");
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
