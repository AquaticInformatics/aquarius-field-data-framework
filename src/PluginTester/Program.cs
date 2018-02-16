using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using FieldDataPluginFramework;
using FieldDataPluginFramework.Results;
using log4net;
using ServiceStack;
using ServiceStack.Text;
using ILog = FieldDataPluginFramework.ILog;

namespace PluginTester
{
    public class Program
    {
        private static log4net.ILog _log;

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
            var resourceName = $"{GetProgramName()}.{path}";

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
                new Option {Key = "Data", Setter = value => Context.DataPath = value, Getter = () => Context.DataPath, Description = "Path to the data file to be parsed"},
                new Option {Key = "Location", Setter = value => Context.LocationIdentifier = value, Getter = () => Context.LocationIdentifier, Description = "Optional location identifier context"},
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

            if (string.IsNullOrEmpty(Context.DataPath))
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

        private void Run()
        {
            using (var stream = LoadDataStream())
            {
                var locationInfo = !string.IsNullOrEmpty(Context.LocationIdentifier)
                    ? FieldDataResultsAppender.CreateLocationInfo(Context.LocationIdentifier)
                    : null;

                var plugin = LoadPlugin();
                var logger = CreateLogger();
                var appender = new FieldDataResultsAppender {LocationInfo = locationInfo};

                try
                {
                    appender.AppendedResults.PluginAssemblyQualifiedTypeName = plugin.GetType().AssemblyQualifiedName;

                    var result = string.IsNullOrEmpty(Context.LocationIdentifier)
                        ? plugin.ParseFile(stream, appender, logger)
                        : plugin.ParseFile(stream, locationInfo, appender, logger);


                    SaveAppendedResults(appender.AppendedResults);

                    SummarizeResults(result, appender.AppendedResults);
                }
                catch (ExpectedException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _log.Error("Plugin has thrown an error", exception);

                    throw new ExpectedException($"Unhandled plugin exception: {exception.Message}");
                }
            }
        }

        private void SaveAppendedResults(AppendedResults appendedResults)
        {
            if (string.IsNullOrEmpty(Context.JsonPath))
                return;

            _log.Info($"Saving {appendedResults.AppendedVisits.Count} visits data to '{Context.JsonPath}'");

            File.WriteAllText(Context.JsonPath, appendedResults.ToJson().IndentJson());
        }

        private void SummarizeResults(ParseFileResult result, AppendedResults appendedResults)
        {
            if (result.Status == Context.ExpectedStatus)
            {
                SummarizeExpectedResults(result, appendedResults);
            }
            else
            {
                SummarizeFailedResults(result, appendedResults);
            }
        }

        private void SummarizeExpectedResults(ParseFileResult result, AppendedResults appendedResults)
        {
            if (result.Parsed)
            {
                if (!appendedResults.AppendedVisits.Any())
                {
                    throw new ExpectedException("File was parsed but no visits were appended.");
                }

                _log.Info($"Successfully parsed {appendedResults.AppendedVisits.Count} visits.");
            }
            else
            {
                var actualError = result.ErrorMessage ?? string.Empty;
                var expectedError = Context.ExpectedError ?? string.Empty;

                if (!actualError.Equals(expectedError))
                    throw new ExpectedException(
                        $"Expected an error message of '{expectedError}' but received '{actualError}' instead.");

                _log.Info($"ParsedResult.Status == {Context.ExpectedStatus} with Error='{Context.ExpectedError}' as expected.");
            }
        }

        private void SummarizeFailedResults(ParseFileResult result, AppendedResults appendedResults)
        {
            if (result.Parsed)
                throw new ExpectedException(
                    $"File was parsed successfully with {appendedResults.AppendedVisits.Count} visits appended.");

            if (result.Status != ParseFileStatus.CannotParse)
                throw new ExpectedException(
                    $"Result: Parsed={result.Parsed} Status={result.Status} ErrorMessage={result.ErrorMessage}");

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                throw new ExpectedException($"Can't parse '{Context.DataPath}'. {result.ErrorMessage}");
            }

            throw new ExpectedException($"File '{Context.DataPath}' is not parsed by the plugin.");
        }

        private Stream LoadDataStream()
        {
            if (!File.Exists(Context.DataPath))
                throw new ExpectedException($"Data file '{Context.DataPath}' does not exist.");

            _log.Info($"Loading data file '{Context.DataPath}'");

            using (var stream = new FileStream(Context.DataPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new BinaryReader(stream))
            {
                return new MemoryStream(reader.ReadBytes((int)stream.Length));
            }
        }

        private IFieldDataPlugin LoadPlugin()
        {
            var pluginPath = Path.GetFullPath(Context.PluginPath);

            if (!File.Exists(pluginPath))
                throw new ExpectedException($"Plugin file '{pluginPath}' does not exist.");

            // ReSharper disable once PossibleNullReferenceException
            var assembliesInPluginFolder = new FileInfo(pluginPath).Directory.GetFiles("*.dll");

            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                var dll = assembliesInPluginFolder.FirstOrDefault(fi =>
                    args.Name.StartsWith(Path.GetFileNameWithoutExtension(fi.Name) + ", ",
                        StringComparison.InvariantCultureIgnoreCase));

                return dll == null ? null : Assembly.LoadFrom(dll.FullName);
            };

            var assembly = Assembly.LoadFile(pluginPath);

            var pluginTypes = (
                    from type in assembly.GetTypes()
                    where typeof(IFieldDataPlugin).IsAssignableFrom(type)
                    select type
                ).ToList();

            if (pluginTypes.Count == 0)
                throw new ExpectedException($"No IFieldDataPlugin plugin implementations found in '{pluginPath}'.");

            if (pluginTypes.Count > 1)
                throw new ExpectedException($"{pluginTypes.Count} IFieldDataPlugin plugin implementations found in '{pluginPath}'.");

            var pluginType = pluginTypes.Single();

            return Activator.CreateInstance(pluginType) as IFieldDataPlugin;
        }

        private ILog CreateLogger()
        {
            return Log4NetLogger.Create(LogManager.GetLogger(Path.GetFileNameWithoutExtension(Context.PluginPath)));
        }
    }
}
