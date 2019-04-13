using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text.RegularExpressions;
using Aquarius.TimeSeries.Client.ServiceModels.Provisioning;
using Common;
using FieldDataPluginFramework;
using log4net;
using ServiceStack;
using ServiceStack.Text;
using ILog = log4net.ILog;

namespace PluginPackager
{
    public class Packager
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public Context Context { get; set; }
        private IFieldDataPlugin Plugin { get; set; }

        public void CreatePackage()
        {
            ResolveDefaults();

            CreateOutputPackage();
        }

        private void ResolveDefaults()
        {
            ResolveAssemblyFolder();
            ResolveAssemblyPath();
            ResolveAssemblyQualifiedTypeName();
            ResolveDeployedFolderName();
            ResolveDescription();
        }

        private void ResolveAssemblyFolder()
        {
            if (!string.IsNullOrEmpty(Context.AssemblyFolder))
            {
                if (!Directory.Exists(Context.AssemblyFolder))
                    throw new ExpectedException($"'{Context.AssemblyFolder}' is not a valid directory.");

                return;
            }

            if (!File.Exists(Context.AssemblyPath))
                throw new ExpectedException($"'{Context.AssemblyPath}' is not a valid file.");

            var file = new FileInfo(Context.AssemblyPath);

            Context.AssemblyFolder = file.DirectoryName;

            if (string.IsNullOrEmpty(Context.AssemblyFolder) || !Directory.Exists(Context.AssemblyFolder))
                throw new Exception($"Can't infer existing folder from '{Context.AssemblyPath}'");
        }

        private void ResolveAssemblyPath()
        {
            AllowReflectionLoadsFromAssemblyFolder();

            if (!string.IsNullOrEmpty(Context.AssemblyPath))
            {
                var assembly = LoadAssembly(Context.AssemblyPath, message => Log.Error($"Can't load '{Context.AssemblyPath}': {message}"));

                if (assembly == null)
                    throw new ExpectedException($"Can't load plugin assembly.");

                Plugin = FindAllPluginImplementations(assembly).Single();
                return;
            }

            Plugin = GetSinglePluginOrThrow();

            Context.AssemblyPath = Plugin.GetType().GetAssemblyPath();
        }

        private void AllowReflectionLoadsFromAssemblyFolder()
        {
            AppDomain.CurrentDomain.AssemblyResolve += LoadFromPluginFolder;
        }

        private Assembly LoadFromPluginFolder(object sender, ResolveEventArgs args)
        {
            var assemblyPath = Path.Combine(Context.AssemblyFolder, new AssemblyName(args.Name).Name + ".dll");

            if (!File.Exists(assemblyPath)) return null;

            return Assembly.LoadFrom(assemblyPath);
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

        private IFieldDataPlugin GetSinglePluginOrThrow()
        {
            var directory = new DirectoryInfo(Context.AssemblyFolder);

            var plugins = new List<IFieldDataPlugin>();

            foreach (var file in directory.GetFiles("*.dll"))
            {
                var assembly = LoadAssembly(file.FullName, message => Log.Warn($"Skipping '{file.FullName}': {message}"));

                if (assembly == null)
                    continue;

                plugins.AddRange(FindAllPluginImplementations(assembly));
            }

            if (plugins.Count > 1)
                throw new ExpectedException(
                    $"'{Context.AssemblyFolder}' contains multiple IFieldDataPlugin implementations. You'll need to explicitly specify an /{nameof(Context.AssemblyPath)} option.");

            if (plugins.Count != 1)
                throw new ExpectedException($"Can't find any IFieldDataPlugin implementations in '{Context.AssemblyFolder}'.");

            return plugins.Single();
        }

        private static IEnumerable<IFieldDataPlugin> FindAllPluginImplementations(Assembly assembly)
        {
            return assembly.GetTypes()
                .Where(type => typeof(IFieldDataPlugin).IsAssignableFrom(type))
                .Select(type => (IFieldDataPlugin)Activator.CreateInstance(type));
        }

        private void ResolveAssemblyQualifiedTypeName()
        {
            if (!string.IsNullOrWhiteSpace(Context.AssemblyQualifiedTypeName))
                return;

            Context.AssemblyQualifiedTypeName = Plugin.GetType().AssemblyQualifiedName;
        }

        private void ResolveDeployedFolderName()
        {
            if (!string.IsNullOrWhiteSpace(Context.DeployedFolderName))
                return;

            var filename = Path.GetFileNameWithoutExtension(Context.AssemblyPath);

            if (string.IsNullOrEmpty(filename))
                throw new Exception($"Can't parse filename from '{Context.AssemblyPath}'");

            var tidiedName = new Regex(@"(plugin|plug-in)", RegexOptions.IgnoreCase).Replace(filename, string.Empty);

            Context.DeployedFolderName = tidiedName.Split(new[] {'.'}, StringSplitOptions.RemoveEmptyEntries).Last();
        }

        private void ResolveDescription()
        {
            if (!string.IsNullOrWhiteSpace(Context.Description))
                return;

            Context.Description = $"The {Context.DeployedFolderName} plugin";
        }

        private void CreateOutputPackage()
        {
            var directory = Path.GetDirectoryName(Context.OutputPath);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Log.Info($"Creating '{Context.OutputPath}' ...");

            File.Delete(Context.OutputPath);

            using (var zipArchive = ZipFile.Open(Context.OutputPath, ZipArchiveMode.Create))
            {
                Log.Info("Adding manifest.json ...");
                var manifestEntry = zipArchive.CreateEntry("manifest.json");
                using (var writer = new StreamWriter(manifestEntry.Open()))
                {
                    var manifest = CreateManifest();

                    writer.Write(manifest.ToJson().IndentJson());
                }

                var assemblyFolder = new DirectoryInfo(Context.AssemblyFolder);
                var includeRegexes = Context.Include.Select(CreateRegexFromDosPattern).ToList();
                var excludeRegexes = Context.Exclude.Select(CreateRegexFromDosPattern).ToList();

                foreach (var file in assemblyFolder.GetFiles("*", SearchOption.AllDirectories))
                {
                    var filename = file.Name;
                    var relativePath = file.FullName.Substring(assemblyFolder.FullName.Length + 1);

                    if (excludeRegexes.Any(r => r.IsMatch(filename)))
                    {
                        Log.Info($"Excluding '{relativePath}' ...");
                        continue;
                    }

                    if (includeRegexes.Any() && !includeRegexes.Any(r => r.IsMatch(filename)))
                    {
                        Log.Info($"Skipping '{relativePath}' ...");
                        continue;
                    }

                    Log.Info($"Adding '{relativePath}' ...");
                    zipArchive.CreateEntryFromFile(file.FullName, relativePath);
                }
            }

            Log.Info($"Successfully created '{Context.OutputPath}'.");
        }

        private FieldDataPlugin CreateManifest()
        {
            return new FieldDataPlugin
            {
                AssemblyQualifiedTypeName = Context.AssemblyQualifiedTypeName,
                Description = Context.Description,
                PluginFolderName = Context.DeployedFolderName
            };
        }

        private static Regex CreateRegexFromDosPattern(string pattern)
        {
            if (pattern.EndsWith(".*"))
                pattern = pattern.Substring(0, pattern.Length - 2);

            pattern = pattern
                .Replace(".", "\\.")
                .Replace("*", ".*");

            return new Regex(pattern, RegexOptions.IgnoreCase);
        }
    }
}
