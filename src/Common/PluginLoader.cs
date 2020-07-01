using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text.RegularExpressions;
using FieldDataPluginFramework;

namespace Common
{
    public class PluginLoader
    {
        public ILog Log { get; set; }

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
            var requestingAssemblyName = args.RequestingAssembly.Location;

            var pluginFolder = PluginFolders
                .FirstOrDefault(p => requestingAssemblyName.StartsWith(p, StringComparison.InvariantCultureIgnoreCase));

            if (string.IsNullOrEmpty(pluginFolder))
            {
                Log.Warn($"No plugin folder matches '{assemblyName}' from requesting assembly '{requestingAssemblyName}'");
                return null;
            }

            var assemblyPath = Path.Combine(pluginFolder, assemblyName);

            if (!File.Exists(assemblyPath))
            {
                Log.Warn($"'{assemblyPath}' does not exist for '{requestingAssemblyName}'");
                return null;
            }

            var loadedAssembly = LoadAssembly(assemblyPath, message => Log.Error($"Can't load plugin dependency from '{assemblyPath}' for '{requestingAssemblyName}': {message}"));

            if (loadedAssembly == null)
            {
                Log.Warn($"Plugin dependency '{assemblyPath}' exists but could not be loaded for '{requestingAssemblyName}'.");
            }
            else
            {
                Log.Debug($"Loaded '{assemblyPath}' for '{requestingAssemblyName}'");
            }

            return loadedAssembly;
        }

        private IFieldDataPlugin LoadPlugin(string path)
        {
            if (File.Exists(path))
                return LoadPluginFromFile(path);

            return LoadPluginFromFolder(path);
        }

        private IFieldDataPlugin LoadPluginFromFile(string path)
        {
            AddPluginFolderPath(Path.GetDirectoryName(path));

            var assembly = LoadAssembly(path, message => Log.Error($"Can't load '{path}': {message}"));

            if (assembly == null)
                throw new ExpectedException($"Can't load plugin assembly.");

            var plugins = FindAllPluginImplementations(assembly).ToList();

            return GetSinglePluginOrThrow(path, plugins);
        }

        private void AddPluginFolderPath(string path)
        {
            if (PluginFolders.Contains(path)) return;

            PluginFolders.Add(path);
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

            AddPluginFolderPath(path);

            var directory = new DirectoryInfo(path);

            var plugins = new List<IFieldDataPlugin>();

            foreach (var file in directory.GetFiles("*.dll"))
            {
                var assembly = LoadAssembly(file.FullName, message => Log.Info($"Skipping '{file.FullName}': {message}"));

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
                var assembly = Assembly.LoadFile(path);

                Log.Debug($"Loaded '{path}'");

                return assembly;
            }
            catch (Exception exception)
            {
                if (exception is ReflectionTypeLoadException loadException)
                {
                    exceptionAction(SummarizeLoaderExceptions(loadException));
                }
                else if (exception is BadImageFormatException || exception is FileLoadException || exception is SecurityException)
                {
                    exceptionAction(exception.Message);
                }
                else
                {
                    exceptionAction($"Unexpected Assembly.LoadFile() exception: {exception.GetType().Name}: {exception.Message}");
                    throw;
                }

                return null;
            }
        }

        private static string SummarizeLoaderExceptions(ReflectionTypeLoadException exception)
        {
            if (exception.LoaderExceptions == null)
                return string.Empty;

            return string.Join("\n", exception.LoaderExceptions.Select(e => e.Message));
        }

        private IEnumerable<IFieldDataPlugin> FindAllPluginImplementations(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes()
                    .Where(type => typeof(IFieldDataPlugin).IsAssignableFrom(type))
                    .Select(type => (IFieldDataPlugin)Activator.CreateInstance(type));
            }
            catch (ReflectionTypeLoadException exception)
            {
                Log.Error($"Can't load plugin from '{assembly.FullName}': {exception.Message}:\n{SummarizeLoaderExceptions(exception)}\n{exception.StackTrace}");
                throw;
            }
        }

        public static string GetPluginNameAndVersion(IFieldDataPlugin plugin)
        {
            var pluginType = plugin.GetType();

            return $"{pluginType.FullName} v{GetTypeVersion(pluginType)}";
        }

        public static string GetPluginVersion(string assemblyQualifiedName)
        {
            return GetAssemblyVersion(assemblyQualifiedName);
        }

        public static string GetPluginFolderName(IFieldDataPlugin plugin)
        {
            var folder = Path.GetDirectoryName(plugin.GetType().Assembly.Location);

            // ReSharper disable once AssignNullToNotNullAttribute
            return new DirectoryInfo(folder)
                .Name;
        }
        private static string GetTypeVersion(Type type)
        {
            var version = GetAssemblyVersion(type.AssemblyQualifiedName);

            return version != DefaultAssemblyVersion
                ? version
                : FileVersionInfo.GetVersionInfo(type.Assembly.Location).FileVersion;
        }

        private const string DefaultAssemblyVersion = "1.0.0.0";

        private static string GetAssemblyVersion(string assemblyQualifiedName)
        {
            var match = AssemblyVersionRegex.Match(assemblyQualifiedName ?? string.Empty);

            return match.Success
                ? match.Groups["Version"].Value
                : DefaultAssemblyVersion;
        }

        private static readonly Regex AssemblyVersionRegex = new Regex(@"\bVersion=(?<Version>\d+(\.\d+)*)");
    }
}
