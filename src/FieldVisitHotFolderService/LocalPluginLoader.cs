using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Aquarius.TimeSeries.Client.ServiceModels.Provisioning;
using Common;
using FieldDataPluginFramework;
using log4net;
using ServiceStack;
using ILog = log4net.ILog;

namespace FieldVisitHotFolderService
{
    public class LocalPluginLoader
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private DirectoryInfo Root { get; } = new DirectoryInfo(Path.Combine(FileHelper.ExeDirectory, "LocalPlugins"));

        public List<IFieldDataPlugin> LoadPlugins()
        {
            var pluginBundles = Root
                .GetFiles("*.plugin");

            var installedPluginFolders = pluginBundles
                .Select(InstallPlugin)
                .ToList();

            if (!installedPluginFolders.Any())
                throw new ExpectedException($"You need to have at least one local *.plugin file at '{Root.FullName}'");

            var stalePluginFolders = Root
                .GetDirectories()
                .Where(d => !installedPluginFolders.Contains(d.FullName))
                .ToList();

            foreach (var stalePluginFolder in stalePluginFolders)
            {
                Log.Info($"Deleting stale local plugin folder '{stalePluginFolder.FullName}'");
                stalePluginFolder.Delete(true);
            }

            return new PluginLoader
                {
                    Log = Log4NetLogger.Create(Log)
                }
                .LoadPlugins(installedPluginFolders);
        }

        private const string ManifestFile = "manifest.json";
        private const string FrameworkAssemblyFilename = "FieldDataPluginFramework.dll";

        private string InstallPlugin(FileInfo archiveInfo)
        {
            using (var archive = LoadPluginArchive(archiveInfo.FullName))
            {
                var manifestEntry = archive.Entries.FirstOrDefault(e =>
                    e.FullName.Equals(ManifestFile, StringComparison.InvariantCultureIgnoreCase));

                if (manifestEntry == null)
                    throw new Exception($"Invalid plugin bundle. No manifest found.");

                var plugin = LoadPluginFromManifest(manifestEntry);

                var otherEntries = archive
                    .Entries
                    .Where(e => !ExcludedFromExtraction.Contains(e.FullName))
                    .ToList();

                if (!otherEntries.Any())
                    throw new Exception($"Invalid plugin bundle. No file entries found to install.");

                var pluginFolder = new DirectoryInfo(Path.Combine(Root.FullName, plugin.PluginFolderName));

                if (!pluginFolder.Exists || pluginFolder.LastWriteTimeUtc < archiveInfo.LastWriteTimeUtc)
                {
                    ExtractLocalPlugin(pluginFolder, otherEntries);
                }

                return pluginFolder.FullName;
            }
        }

        private static readonly HashSet<string> ExcludedFromExtraction = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        {
            ManifestFile,
            FrameworkAssemblyFilename,
        };

        private ZipArchive LoadPluginArchive(string path)
        {
            try
            {
                return ZipFile.OpenRead(path);
            }
            catch (Exception)
            {
                throw new ExpectedException($"'{path}' is not a valid *.plugin bundle.");
            }
        }

        private FieldDataPlugin LoadPluginFromManifest(ZipArchiveEntry manifestEntry)
        {
            using (var stream = manifestEntry.Open())
            using (var reader = new StreamReader(stream))
            {
                var jsonText = reader.ReadToEnd();
                var plugin = jsonText.FromJson<FieldDataPlugin>();

                if (string.IsNullOrWhiteSpace(plugin.PluginFolderName))
                    throw new Exception($"Invalid plugin manifest. {nameof(plugin.PluginFolderName)} must be set.");

                if (string.IsNullOrWhiteSpace(plugin.AssemblyQualifiedTypeName))
                    throw new Exception($"Invalid plugin manifest. {nameof(plugin.AssemblyQualifiedTypeName)} must be set.");

                return plugin;
            }
        }

        private void ExtractLocalPlugin(DirectoryInfo pluginFolder, List<ZipArchiveEntry> entriesToExtract)
        {
            Log.Info($"Extracting local plugin '{pluginFolder.FullName}'");

            if (pluginFolder.Exists)
            {
                Log.Info($"Deleting existing '{pluginFolder.FullName}'");
                pluginFolder.Delete(true);
            }

            pluginFolder.Create();

            foreach (var entry in entriesToExtract)
            {
                var extractedPath = Path.Combine(pluginFolder.FullName, entry.FullName);
                var extractedDir = Path.GetDirectoryName(extractedPath);

                if (!Directory.Exists(extractedDir))
                {
                    // ReSharper disable once AssignNullToNotNullAttribute
                    Directory.CreateDirectory(extractedDir);
                }

                Log.Info($"Extracting {entry.FullName} ...");
                using (var inStream = entry.Open())
                using (var outStream = File.Create(extractedPath))
                {
                    inStream.CopyTo(outStream);
                }
            }
        }
    }
}
