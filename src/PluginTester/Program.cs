using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using Common;
using FieldDataPluginFramework.Results;
using FieldDataPluginFramework.Serialization;
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

                ConfigureJson();

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

        private static void ConfigureJson()
        {
            JsonConfig.Configure();
        }

        private Context Context { get; } = new Context();

        private void ParseArgs(string[] args)
        {
            var options = new[]
            {
                new CommandLineOption {Key = "Plugin", Setter = value => Context.PluginPath = value, Getter = () => Context.PluginPath, Description = "Path to the plugin assembly to debug"},
                new CommandLineOption {Key = "Data", Setter = value => AddDataPath(Context, value), Getter = () => string.Empty, Description = "Path to the data file to be parsed. Can be set more than once."},
                new CommandLineOption {Key = nameof(Context.RecursiveSearch), Setter = value => Context.RecursiveSearch = bool.Parse(value), Getter = () => $"{Context.RecursiveSearch}", Description = "Search /Data directories recursively. -R shortcut is also supported."},
                new CommandLineOption {Key = "Location", Setter = value => Context.LocationIdentifier = value, Getter = () => Context.LocationIdentifier, Description = "Optional location identifier context"},
                new CommandLineOption {Key = "UtcOffset", Setter = value => Context.LocationUtcOffset = TimeSpan.Parse(value), Getter = () => Context.LocationUtcOffset.ToString(), Description = "UTC offset in .NET TimeSpan format."},
                new CommandLineOption {Key = "Json", Setter = value => Context.JsonPath = value, Getter = () => Context.JsonPath, Description = "Optional path to write the appended results as JSON"},
                new CommandLineOption {Key = "ExpectedError", Setter = value => Context.ExpectedError = value, Getter = () => Context.ExpectedError, Description = "Expected error message"},
                new CommandLineOption {Key = "ExpectedStatus", Setter = value => Context.ExpectedStatus = (ParseFileStatus)Enum.Parse(typeof(ParseFileStatus), value, true), Getter = () => Context.ExpectedStatus.ToString(), Description = $"Expected parse status. One of {string.Join(", ", Enum.GetNames(typeof(ParseFileStatus)))}"},
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

            return false;
        }

        private static readonly HashSet<string> RecursiveShortcuts =
            new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
            {
                "-r",
                "/r"
            };

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
            new Tester {Context = Context}
                .Run();
        }
    }
}
