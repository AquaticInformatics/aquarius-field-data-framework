using System.IO.Compression;
using Common;
using FieldDataPluginFramework;

namespace MultiFile
{
    class ProxyLog : ILog
    {
        public static ILog Create(ILog log, IFieldDataPlugin plugin, ZipArchiveEntry entry)
        {
            return new ProxyLog(log, plugin, entry);
        }

        private ILog Log { get; }
        private string Prefix { get; }

        private ProxyLog(ILog log, IFieldDataPlugin plugin, ZipArchiveEntry entry)
        {
            Log = log;

            Prefix = $"{PluginLoader.GetPluginNameAndVersion(plugin)} - '{entry.FullName}'";
        }

        public void Info(string message)
        {
            Log.Info($"{Prefix}: {message}");
        }

        public void Error(string message)
        {
            Log.Error($"{Prefix}: {message}");
        }
    }
}
