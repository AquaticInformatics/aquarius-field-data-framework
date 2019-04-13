using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using FieldDataPluginFramework;
using log4net;
using ILog = log4net.ILog;

namespace Common
{
    public class PluginLoader
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private List<string> PluginFolders { get; } = new List<string>();

        public List<IFieldDataPlugin> LoadPlugins(List<string> paths)
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolvePluginAssembliesFromSameFolder;

            return paths
                .Select(LoadPlugin)
                .ToList();
        }

        private Assembly ResolvePluginAssembliesFromSameFolder(object sender, ResolveEventArgs args)
        {
            var assemblyName = new AssemblyName(args.Name).Name + ".dll";

            var pluginFolder = PluginFolders
                .FirstOrDefault(p => args.RequestingAssembly.Location.StartsWith(p, StringComparison.InvariantCultureIgnoreCase));

            if (string.IsNullOrEmpty(pluginFolder))
                return null;

            var assemblyPath = Path.Combine(pluginFolder, assemblyName);

            if (!File.Exists(assemblyPath))
                return null;

            return Assembly.LoadFrom(assemblyPath);
        }

        private IFieldDataPlugin LoadPlugin(string path)
        {
            if (File.Exists(path))
                return LoadPluginFromFile(path);

            return LoadPluginFromFolder(path);
        }

        private IFieldDataPlugin LoadPluginFromFile(string path)
        {
            PluginFolders.Add(Path.GetDirectoryName(path));

            var assembly = LoadAssembly(path, message => Log.Error($"Can't load '{path}': {message}"));

            if (assembly == null)
                throw new ExpectedException($"Can't load plugin assembly.");

            var plugins = FindAllPluginImplementations(assembly).ToList();

            return GetSinglePluginOrThrow(path, plugins);
        }

        private IFieldDataPlugin GetSinglePluginOrThrow(string path, List<IFieldDataPlugin> plugins)
        {
            if (!plugins.Any())
                throw new ExpectedException($"No {nameof(IFieldDataPlugin)} implementations found in '{path}'");

            if (plugins.Count > 1)
                throw new ExpectedException($"'{path}' contains {plugins.Count} {nameof(IFieldDataPlugin)} implementations.");

            return plugins.Single();
        }

        private IFieldDataPlugin LoadPluginFromFolder(string path)
        {
            if (!Directory.Exists(path))
                throw new ExpectedException($"'{path}' is not a valid directory.");

            PluginFolders.Add(path);

            var directory = new DirectoryInfo(path);

            var plugins = new List<IFieldDataPlugin>();

            foreach (var file in directory.GetFiles("*.dll"))
            {
                var assembly = LoadAssembly(file.FullName, message => Log.Warn($"Skipping '{file.FullName}': {message}"));

                if (assembly == null)
                    continue;

                plugins.AddRange(FindAllPluginImplementations(assembly));
            }

            return GetSinglePluginOrThrow(path, plugins);
        }

        private Assembly LoadAssembly(string path, Action<string> exceptionAction)
        {
            try
            {
                return Assembly.LoadFile(path);
            }
            catch (Exception exception)
            {
                if (exception is ReflectionTypeLoadException loadException)
                {
                    exceptionAction(string.Join("\n", loadException.LoaderExceptions.Select(e => e.Message)));
                }
                else if (exception is BadImageFormatException || exception is FileLoadException || exception is SecurityException)
                {
                    exceptionAction(exception.Message);
                }
                else
                {
                    throw;
                }

                return null;
            }
        }

        private static IEnumerable<IFieldDataPlugin> FindAllPluginImplementations(Assembly assembly)
        {
            return assembly.GetTypes()
                .Where(type => typeof(IFieldDataPlugin).IsAssignableFrom(type))
                .Select(type => (IFieldDataPlugin)Activator.CreateInstance(type));
        }
    }
}
