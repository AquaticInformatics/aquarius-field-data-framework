using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using FieldDataPluginFramework.Results;
using log4net;
using ServiceStack;
using ServiceStack.Text;

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
            JsConfig.ExcludeTypeInfo = true;
            JsConfig.DateHandler = DateHandler.ISO8601DateTime;
            JsConfig.IncludeNullValues = true;
            JsConfig.IncludeNullValuesInDictionaries = true;

            JsConfig<DateTimeOffset>.SerializeFn = offset => offset.ToString("O");
            JsConfig<DateTimeOffset?>.SerializeFn = offset => offset?.ToString("O") ?? string.Empty;
        }

        private static string GetProgramName()
        {
            return Path.GetFileNameWithoutExtension(Assembly.GetEntryAssembly().Location);
        }

        private Context Context { get; set; } = new Context();

        private void ParseArgs(string[] args)
        {
            var resolvedArgs = args
                .SelectMany(ResolveOptionsFromFile)
                .ToArray();

            var options = new[]
            {
                new Option {Key = "Plugin", Setter = value => Context.PluginPath = value, Getter = () => Context.PluginPath, Description = "Path to the plugin assembly to debug"},
                new Option {Key = "Data", Setter = value => AddDataPath(Context, value), Getter = () => string.Empty, Description = "Path to the data file to be parsed. Can be set more than once."},
                new Option {Key = "Location", Setter = value => Context.LocationIdentifier = value, Getter = () => Context.LocationIdentifier, Description = "Optional location identifier context"},
                new Option {Key = "UtcOffset", Setter = value => Context.LocationUtcOffset = TimeSpan.Parse(value), Getter = () => Context.LocationUtcOffset.ToString(), Description = "UTC offset in .NET TimeSpan format."},
                new Option {Key = "Json", Setter = value => Context.JsonPath = value, Getter = () => Context.JsonPath, Description = "Optional path to write the appended results as JSON"},
                new Option {Key = "ExpectedError", Setter = value => Context.ExpectedError = value, Getter = () => Context.ExpectedError, Description = "Expected error message"},
                new Option {Key = "ExpectedStatus", Setter = value => Context.ExpectedStatus = (ParseFileStatus)Enum.Parse(typeof(ParseFileStatus), value, true), Getter = () => Context.ExpectedStatus.ToString(), Description = $"Expected parse status. One of {string.Join(", ", Enum.GetNames(typeof(ParseFileStatus)))}"},
            };

            var usageMessage = $"Parse a file using a field data plugin, logging the results."
                               + $"\n"
                               + $"\nusage: {GetProgramName()} [-option=value] ..."
                               + $"\n"
                               + $"\nSupported -option=value settings (/option=value works too):\n\n  -{string.Join("\n  -", options.Select(o => o.UsageText()))}"
                               + $"\n"
                               + $"\nUse the @optionsFile syntax to read more options from a file."
                               + $"\n"
                               + $"\n  Each line in the file is treated as a command line option."
                               + $"\n  Blank lines and leading/trailing whitespace is ignored."
                               + $"\n  Comment lines begin with a # or // marker."
                               ;

            foreach (var arg in resolvedArgs)
            {
                var match = ArgRegex.Match(arg);

                if (!match.Success)
                {
                    throw new ExpectedException($"Unknown argument: {arg}\n\n{usageMessage}");
                }

                var key = match.Groups["key"].Value.ToLower();
                var value = match.Groups["value"].Value;

                var option =
                    options.FirstOrDefault(o => o.Key.Equals(key, StringComparison.InvariantCultureIgnoreCase));

                if (option == null)
                {
                    throw new ExpectedException($"Unknown -option=value: {arg}\n\n{usageMessage}");
                }

                option.Setter(value);
            }

            if (string.IsNullOrEmpty(Context.PluginPath))
                throw new ExpectedException("No plugin assembly specified.");

            if (!Context.DataPaths.Any())
                throw new ExpectedException("No data file specified.");
        }

        private static IEnumerable<string> ResolveOptionsFromFile(string arg)
        {
            if (!arg.StartsWith("@"))
                return new[] { arg };

            var path = arg.Substring(1);

            if (!File.Exists(path))
                throw new ExpectedException($"Options file '{path}' does not exist.");

            return File.ReadAllLines(path)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Where(s => !s.StartsWith("#") && !s.StartsWith("//"));
        }

        private static readonly Regex ArgRegex = new Regex(@"^([/-])(?<key>[^=]+)=(?<value>.*)$", RegexOptions.Compiled);

        private static void AddDataPath(Context context, string dataPath)
        {
            foreach (var path in ExpandDataPath(dataPath))
            {
                context.DataPaths.Add(path);
            }
        }

        private static IEnumerable<string> ExpandDataPath(string path)
        {
            var dir = Path.GetDirectoryName(path);
            var filename = Path.GetFileName(path);

            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(filename))
            {
                yield return path;
            }
            else
            {
                foreach (var expandedPath in Directory.GetFiles(dir, filename))
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
