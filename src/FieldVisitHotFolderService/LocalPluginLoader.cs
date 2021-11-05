using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Aquarius.TimeSeries.Client;
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

        public bool Verbose { get; set; }
        public string JsonPluginPath { get; set; }
        public AquariusServerVersion JsonPluginVersion { get; set; }

        public static bool IsJsonPlugin(FieldDataPlugin plugin)
        {
            return plugin.AssemblyQualifiedTypeName.StartsWith("JsonFieldData.Plugin");
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

            return new PluginLoader
                {
                    Log = Log4NetLogger.Create(Log),
                    Verbose = Verbose
                }
                .LoadPlugins(pluginFiles);
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
