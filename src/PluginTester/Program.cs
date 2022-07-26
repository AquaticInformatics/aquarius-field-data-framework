using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text.RegularExpressions;
using System.Xml;
using Common;
using log4net;
using ServiceStack;

namespace PluginTester
{
    public class Program
    {
        private static ILog _log;

        public static void Main(string[] args)
        {
            Environment.ExitCode = 1;

            try
            {
                ConfigureLogging();

                var program = new Program();
                program.ParseArgs(args);
                program.Run();

                Environment.ExitCode = 0;
            }
            catch (ExpectedException exception)
            {
                _log.Error(exception.Message);
            }
            catch (Exception exception)
            {
                _log.Error("Unhandled exception", exception);
            }
        }

        private static void ConfigureLogging()
        {
            using (var stream = new MemoryStream(LoadEmbeddedResource("log4net.config")))
            using (var reader = new StreamReader(stream))
            {
                var xml = new XmlDocument();
                xml.LoadXml(reader.ReadToEnd());

                log4net.Config.XmlConfigurator.Configure(xml.DocumentElement);

                _log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
            }
        }

        private static byte[] LoadEmbeddedResource(string path)
        {
            // ReSharper disable once PossibleNullReferenceException
            var resourceName = $"{MethodBase.GetCurrentMethod().DeclaringType.Namespace}.{path}";

            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new ExpectedException($"Can't load '{resourceName}' as embedded resource.");

                return stream.ReadFully();
            }
        }

        private Context Context { get; } = new Context();

        private void ParseArgs(string[] args)
        {
            var options = new[]
            {
                new CommandLineOption { Description = "Specify the plugin to be tested" },
                new CommandLineOption
                {
                    Key = "Plugin",
                    Setter = value => Context.PluginPath = value,
                    Getter = () => Context.PluginPath,
                    Description = "Path to the plugin assembly. Can be a folder, a DLL, or a packaged *.plugin file."
                },
                new CommandLineOption
                {
                    Key = nameof(Context.Verbose),
                    Setter = value => Context.Verbose = bool.Parse(value),
                    Getter = () => $"{Context.Verbose}",
                    Description = "Enables verbose logging of assembly loading logic."
                },
                new CommandLineOption
                {
                    Key = nameof(Context.FrameworkAssemblyPath),
                    Setter = value => Context.FrameworkAssemblyPath = value,
                    Getter = () => Context.FrameworkAssemblyPath,
                    Description = "Optional path to the FieldDataPluginFramework.dll assembly. [default: Test using the latest framework version]"
                },
                new CommandLineOption(), new CommandLineOption { Description = "Test data settings" },
                new CommandLineOption
                {
                    Key = "Data",
                    Setter = value => AddDataPath(Context, value),
                    Getter = () => string.Empty,
                    Description = "Path to the data file to be parsed. Can be set more than once."
                },
                new CommandLineOption
                {
                    Key = nameof(Context.RecursiveSearch),
                    Setter = value => Context.RecursiveSearch = bool.Parse(value),
                    Getter = () => $"{Context.RecursiveSearch}",
                    Description = "Search /Data directories recursively. -R shortcut is also supported."
                },
                new CommandLineOption
                {
                    Key = "Setting",
                    Setter = value => AddSetting(Context, value),
                    Getter = () => string.Empty,
                    Description = "Supply plugin settings as 'key=text' or 'key=@pathToTextFile' values."
                },
                new CommandLineOption(), new CommandLineOption { Description = "Plugin context settings" },
                new CommandLineOption
                {
                    Key = "Location",
                    Setter = value => Context.LocationIdentifier = value,
                    Getter = () => Context.LocationIdentifier,
                    Description = "Optional location identifier context"
                },
                new CommandLineOption
                {
                    Key = "UtcOffset",
                    Setter = value => Context.LocationUtcOffset = TimeSpan.Parse(value),
                    Getter = () => Context.LocationUtcOffset.ToString(),
                    Description = "UTC offset in .NET TimeSpan format."
                },
                new CommandLineOption(), new CommandLineOption { Description = "Output settings" },
                new CommandLineOption
                {
                    Key = "Json",
                    Setter = value => Context.JsonPath = value,
                    Getter = () => Context.JsonPath,
                    Description = "Optional path (file or folder) to write the appended results as JSON."
                },
                new CommandLineOption(), new CommandLineOption { Description = "Expected response settings" },
                new CommandLineOption
                {
                    Key = nameof(Context.ExpectedError),
                    Setter = value => Context.ExpectedError = value,
                    Getter = () => Context.ExpectedError,
                    Description = "Expected error message"
                },
                new CommandLineOption
                {
                    Key = nameof(Context.ExpectedStatus),
                    Setter = value => Context.ExpectedStatus = (StatusType)Enum.Parse(typeof(StatusType), value, true),
                    Getter = () => Context.ExpectedStatus.ToString(),
                    Description = $"Expected parse status. One of {string.Join(", ", Enum.GetNames(typeof(StatusType)))}"
                },
            };

            var usageMessage = CommandLineUsage.ComposeUsageText(
                "Parse a file using a field data plugin, logging the results.", options);

            var optionResolver = new CommandLineOptionResolver();

            optionResolver.Resolve(args, options, usageMessage, arg => PositionalArgumentResolver(Context, arg));

            if (string.IsNullOrEmpty(Context.PluginPath))
                throw new ExpectedException("No plugin assembly specified.");

            if (!Context.DataPaths.Any())
                throw new ExpectedException("No data file specified.");
        }

        private bool PositionalArgumentResolver(Context context, string arg)
        {
            if (RecursiveShortcuts.Contains(arg))
            {
                Context.RecursiveSearch = true;
                return true;
            }

            var match = SettingRegex.Match(arg);

            if (match.Success)
            {
                AddSetting(context, arg);
                return true;
            }

            return false;
        }

        private static readonly HashSet<string> RecursiveShortcuts =
            new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
            {
                "-r",
                "/r"
            };

        private static void AddSetting(Context context, string value)
        {
            var match = SettingRegex.Match(value);

            if (!match.Success)
                throw new ExpectedException($"'{value}' does not match a key=text or key=@pathToTextFile setting.");

            var key = match.Groups["key"].Value;
            var text = match.Groups["text"].Value;
            var pathToText = match.Groups["pathToTextFile"].Value;

            context.Settings[key] = !string.IsNullOrWhiteSpace(pathToText)
                ? File.ReadAllText(pathToText)
                : text;
        }

        private static readonly Regex SettingRegex = new Regex(@"^\s*(?<key>[^=]+)\s*=\s*(@(?<pathToTextFile>.+)|(?<text>.+))$");

        private static void AddDataPath(Context context, string dataPath)
        {
            foreach (var path in ExpandDataPath(context, dataPath))
            {
                context.DataPaths.Add(path);
            }
        }

        private static IEnumerable<string> ExpandDataPath(Context context, string path)
        {
            var dir = Path.GetDirectoryName(path);
            var filename = Path.GetFileName(path);

            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(filename))
            {
                yield return path;
            }
            else
            {
                var searchDepth = context.RecursiveSearch
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;

                foreach (var expandedPath in Directory.GetFiles(dir, filename, searchDepth))
                {
                    yield return expandedPath;
                }
            }
        }

        private void Run()
        {
            LoadFrameworkAssembly();

            new Tester {Context = Context}
                .Run();
        }

        private void LoadFrameworkAssembly()
        {
            if (string.IsNullOrWhiteSpace(Context.FrameworkAssemblyPath))
                return;

            if (!File.Exists(Context.FrameworkAssemblyPath))
                throw new ExpectedException($"Can't find framework assembly at '{Context.FrameworkAssemblyPath}'.");

            var assembly = LoadFrameworkAssembly(Context.FrameworkAssemblyPath);

            const string targetName = "FieldDataPluginFramework.IFieldDataPlugin";

            var interfaceDefinitionType = assembly.GetTypes()
                .FirstOrDefault(type => type.FullName == targetName);

            if (interfaceDefinitionType == null)
                throw new ExpectedException($"Can't find {targetName} in '{Context.FrameworkAssemblyPath}'");

            _log.Info($"Loaded external framework assembly '{assembly.FullName}' from '{Context.FrameworkAssemblyPath}'.");
        }

        private Assembly LoadFrameworkAssembly(string path)
        {
            try
            {
                return Assembly.LoadFile(path);
            }
            catch (Exception exception)
            {
                switch (exception)
                {
                    case ReflectionTypeLoadException loadException:
                        throw new ExpectedException($"Can't load '{path}': {SummarizeLoaderExceptions(loadException)}");

                    case BadImageFormatException _:
                    case FileLoadException _:
                    case SecurityException _:
                        throw new ExpectedException($"Can't load '{path}': {exception.Message}");

                    default:
                        _log.Error($"Unexpected Assembly.LoadFile('{path}') exception: {exception.GetType().Name}: {exception.Message}");
                        throw;
                }
            }
        }

        private static string SummarizeLoaderExceptions(ReflectionTypeLoadException exception)
        {
            if (exception.LoaderExceptions == null)
                return string.Empty;

            return string.Join("\n", exception.LoaderExceptions.Select(e => e.Message));
        }
    }
}
