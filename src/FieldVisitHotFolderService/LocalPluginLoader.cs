using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Aquarius.TimeSeries.Client;
using Aquarius.TimeSeries.Client.ServiceModels.Provisioning;
using Common;
using log4net;
using ILog = log4net.ILog;

namespace FieldVisitHotFolderService
{
    public class LocalPluginLoader
    {
        // ReSharper disable once PossibleNullReferenceException
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private DirectoryInfo Root { get; } = new DirectoryInfo(Path.Combine(FileHelper.ExeDirectory, "LocalPlugins"));

        public bool Verbose { get; set; }
        public string JsonPluginPath { get; set; }
        public AquariusServerVersion JsonPluginVersion { get; set; }

        public static bool IsJsonPlugin(FieldDataPlugin plugin)
        {
            return IsJsonPlugin(plugin.AssemblyQualifiedTypeName);
        }

        private static bool IsJsonPlugin(PluginManifest manifest)
        {
            return IsJsonPlugin(manifest.AssemblyQualifiedTypeName);
        }

        private static bool IsJsonPlugin(string assemblyQualifiedTypeName)
        {
            return assemblyQualifiedTypeName.StartsWith("JsonFieldData.Plugin");
        }

        public List<PluginLoader.LoadedPlugin> LoadPlugins()
        {
            RemoveExtractedPluginFolders();

            var pluginFiles = Root
                .GetFiles("*.plugin")
                .Select(fi => fi.FullName)
                .ToList();

            if (!pluginFiles.Any())
                throw new ExpectedException($"You need to have at least one local *.plugin file at '{Root.FullName}'");

            var loadedPlugins = new PluginLoader
                {
                    Log = Log4NetLogger.Create(Log),
                    Verbose = Verbose
                }
                .LoadPlugins(pluginFiles);

            var jsonPlugin = loadedPlugins
                .FirstOrDefault(lp => IsJsonPlugin(lp.Manifest));

            if (jsonPlugin != null)
            {
                var result = AssemblyQualifiedNameParser.Parse(jsonPlugin.Manifest.AssemblyQualifiedTypeName);

                JsonPluginVersion = AquariusServerVersion.Create(result.Version);
                JsonPluginPath = jsonPlugin.Path;
            }

            return loadedPlugins;
        }

        private void RemoveExtractedPluginFolders()
        {
            foreach (var subFolder in Root.GetDirectories())
            {
                Log.Info($"Removing stale subfolder '{subFolder.FullName}' ...");
                SafelyDeleteDirectory(subFolder.FullName);
            }
        }

        private static void SafelyDeleteDirectory(string path)
        {
            Directory.Delete(path, true);

            // Thanks be to https://stackoverflow.com/questions/4216396/system-io-directorynotfoundexception-after-deleting-an-empty-folder-and-recreati
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}
