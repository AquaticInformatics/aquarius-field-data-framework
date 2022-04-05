using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security;
using FieldDataPluginFramework;
using ServiceStack;

namespace Common
{
    public class PluginLoader
    {
        public ILog Log { get; set; }

        public bool Verbose { get; set; }

        private List<string> PluginFolders { get; } = new List<string>();

        public class ArchiveContext
        {
            public ArchiveContext(string path, ZipArchive archive, Assembly assembly)
            {
                Path = path;
                Archive = archive;
                LoadedAssemblies.Add(assembly);
            }

            public string Path { get; }
            public ZipArchive Archive { get; }
            public HashSet<Assembly> LoadedAssemblies { get; } = new HashSet<Assembly>();
        }

        private List<ArchiveContext> PluginArchives { get; } = new List<ArchiveContext>();

        public class LoadedPlugin
        {
            public LoadedPlugin(IFieldDataPlugin plugin, PluginManifest manifest, string path)
            {
                Plugin = plugin;
                Manifest = manifest;
                Path = path;
            }

            public IFieldDataPlugin Plugin { get; }
            public PluginManifest Manifest { get; }
            public string Path { get; }
        }

        public List<LoadedPlugin> LoadPlugins(List<string> paths)
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolvePluginAssembliesFromSameFolder;

            return paths
                .Select(LoadPlugin)
                .ToList();
        }

        private void LogVerbose(string message)
        {
            if (Verbose)
                Log.Info(message);
            else
                Log.Debug(message);
        }

        private Assembly ResolvePluginAssembliesFromSameFolder(object sender, ResolveEventArgs args)
        {
            var assemblyName = new AssemblyName(args.Name).Name + ".dll";
            var requestingAssembly = args.RequestingAssembly;

            if (requestingAssembly == null)
                return null;

            if (TryResolveAssemblyFromArchive(requestingAssembly, assemblyName, out var resolvedAssembly))
                return resolvedAssembly;

            var requestingAssemblyName = requestingAssembly.Location;

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

            var loadedAssembly = LoadAssemblyFromFile(assemblyPath, message => Log.Error($"Can't load plugin dependency from '{assemblyPath}' for '{requestingAssemblyName}': {message}"));

            if (loadedAssembly == null)
            {
                Log.Warn($"Plugin dependency '{assemblyPath}' exists but could not be loaded for '{requestingAssemblyName}'.");
            }
            else
            {
                LogVerbose($"Loaded '{assemblyPath}' for '{requestingAssemblyName}'");
            }

            return loadedAssembly;
        }

        private bool TryResolveAssemblyFromArchive(Assembly requestingAssembly, string assemblyName, out Assembly assembly)
        {
            assembly = default;

            var archiveContext = PluginArchives
                .FirstOrDefault(context => context.LoadedAssemblies.Contains(requestingAssembly));

            if (archiveContext == null)
                return false;

            var targetAssemblyEntry = archiveContext
                    .Archive
                    .Entries
                    .FirstOrDefault(e => e.FullName.Equals(assemblyName, StringComparison.InvariantCultureIgnoreCase));

            if (targetAssemblyEntry == null)
                return false;

            var targetAssemblyBytes = LoadAssemblyBytes(targetAssemblyEntry);

            assembly = LoadAssemblyFromBytes(
                targetAssemblyBytes,
                message => Log.Error($"Can't load assembly '{assemblyName}' from '{archiveContext.Path}': {message}"));

            if (assembly == null)
                return true;

            LogVerbose($"Loaded '{assemblyName}' for '{requestingAssembly.FullName}' from {archiveContext.Path}");

            archiveContext.LoadedAssemblies.Add(assembly);

            return true;
        }

        private LoadedPlugin LoadPlugin(string path)
        {
            if (IsZipPlugin(path))
                return LoadPluginFromZip(path);

            var plugin = File.Exists(path)
                ? LoadPluginFromFile(path)
                : LoadPluginFromFolder(path);

            var manifest = new PluginManifest
            {
                AssemblyQualifiedTypeName = plugin.GetType().AssemblyQualifiedName,
                PluginFolderName = File.Exists(path)
                    ? new FileInfo(path).Directory?.Name
                    : new DirectoryInfo(path).Name
            };

            return new LoadedPlugin(plugin, manifest, path);
        }

        private IFieldDataPlugin LoadPluginFromFile(string path)
        {
            AddPluginFolderPath(Path.GetDirectoryName(path));

            var assembly = LoadAssemblyFromFile(path, message => Log.Error($"Can't load '{path}': {message}"));

            if (assembly == null)
                throw new ExpectedException($"Can't load plugin assembly.");

            var plugins = FindAllPluginImplementations(assembly).ToList();

            return GetSinglePluginOrThrow(path, plugins);
        }

        private LoadedPlugin LoadPluginFromZip(string path)
        {
            var archive = LoadPluginArchive(path);

            var manifestEntry = archive.GetEntry(PluginManifest.EntryName);

            if (manifestEntry == null)
                throw new ExpectedException($"'{path}' is not valid *.plugin bundle. No {PluginManifest.EntryName} found.");

            var manifest = LoadManifest(manifestEntry);

            var assemblyName = $"{AssemblyQualifiedNameParser.Parse(manifest.AssemblyQualifiedTypeName).AssemblyName}.dll";

            var mainAssemblyEntry = archive
                .Entries
                .FirstOrDefault(e => e.Name.Equals(assemblyName, StringComparison.InvariantCultureIgnoreCase));

            if (mainAssemblyEntry == null)
                throw new ExpectedException($"Can't find '{assemblyName}' inside '{path}'");

            var assemblyBytes = LoadAssemblyBytes(mainAssemblyEntry);

            var assembly = LoadAssemblyFromBytes(assemblyBytes, message => Log.Error($"Can't load '{assemblyName}' from '{path}': {message}"));

            if (assembly == null)
                throw new ExpectedException($"Can't load plugin assembly from '{path}'.");

            LogVerbose($"Loaded '{assemblyName}' from '{path}'");

            var archiveContext = new ArchiveContext(path, archive, assembly);

            try
            {
                PluginArchives.Add(archiveContext);

                var plugins = FindAllPluginImplementations(assembly).ToList();

                return new LoadedPlugin(GetSinglePluginOrThrow(path, plugins), manifest, path);
            }
            catch (Exception)
            {
                PluginArchives.Remove(archiveContext);
                throw;
            }
        }

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

        private PluginManifest LoadManifest(ZipArchiveEntry manifestEntry)
        {
            using (var stream = manifestEntry.Open())
            using (var reader = new StreamReader(stream))
            {
                var jsonText = reader.ReadToEnd();
                var manifest = jsonText.FromJson<PluginManifest>();

                if (string.IsNullOrWhiteSpace(manifest.PluginFolderName))
                    throw new ExpectedException($"Invalid plugin manifest. {nameof(manifest.PluginFolderName)} must be set.");

                if (string.IsNullOrWhiteSpace(manifest.AssemblyQualifiedTypeName))
                    throw new ExpectedException($"Invalid plugin manifest. {nameof(manifest.AssemblyQualifiedTypeName)} must be set.");

                return manifest;
            }
        }

        private byte[] LoadAssemblyBytes(ZipArchiveEntry entry)
        {
            using (var stream = entry.Open())
            using(var reader = new BinaryReader(stream))
            {
                return reader.ReadBytes((int)entry.Length);
            }
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
                var assembly = LoadAssemblyFromFile(file.FullName, message => Log.Info($"Skipping '{file.FullName}': {message}"));

                if (assembly == null)
                    continue;

                plugins.AddRange(FindAllPluginImplementations(assembly));
            }

            return GetSinglePluginOrThrow(path, plugins);
        }

        private Assembly LoadAssemblyFromBytes(byte[] assemblyBytes, Action<string> exceptionAction)
        {
            return LoadAssembly(() => Assembly.Load(assemblyBytes), exceptionAction);
        }

        private Assembly LoadAssemblyFromFile(string path, Action<string> exceptionAction)
        {
            return LoadAssembly(() =>
            {
                var assembly = Assembly.LoadFile(path);

                LogVerbose($"Loaded '{path}'");

                return assembly;

            }, exceptionAction);
        }

        private Assembly LoadAssembly(Func<Assembly> loadFunc, Action<string> exceptionAction)
        {
            try
            {
                return loadFunc();
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

        private static string GetTypeVersion(Type type)
        {
            var version = GetAssemblyVersion(type.AssemblyQualifiedName);

            if (version != DefaultAssemblyVersion)
                return version;

            var assemblyPath = type.Assembly.Location;

            return File.Exists(assemblyPath)
                ? FileVersionInfo.GetVersionInfo(assemblyPath).FileVersion
                : version;
        }

        private const string DefaultAssemblyVersion = "1.0.0.0";

        private static string GetAssemblyVersion(string assemblyQualifiedName)
        {
            return AssemblyQualifiedNameParser.TryParse(assemblyQualifiedName, out var result)
                ? result.Version
                : DefaultAssemblyVersion;
        }

        private static bool IsZipPlugin(string path)
        {
            if (!File.Exists(path) || !PluginExtensions.Contains(Path.GetExtension(path)))
                return false;

            var bytes = File.ReadAllBytes(path);

            return bytes.Length > 3
                   && bytes[0] == 0x50
                   && bytes[1] == 0x4b
                   && bytes[2] == 0x03
                   && bytes[3] == 0x04;
        }

        private static readonly HashSet<string> PluginExtensions =
            new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
            {
                ".plugin"
            };
    }
}
