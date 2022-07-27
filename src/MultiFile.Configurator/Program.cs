using System;
using System.IO;
using System.Reflection;
using System.Xml;
using Common;
using log4net;
using ServiceStack;

namespace MultiFile.Configurator
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
            catch (WebServiceException exception)
            {
                _log.Error($"API: ({exception.StatusCode}) {string.Join(" ", exception.StatusDescription, exception.ErrorCode)}: {string.Join(" ", exception.Message, exception.ErrorMessage)}");
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

                _log = LogManager.GetLogger(GetMainProgramType());
            }
        }

        private static byte[] LoadEmbeddedResource(string path)
        {
            var resourceName = $"{GetMainProgramType().Namespace}.{path}";

            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new ExpectedException($"Can't load '{resourceName}' as embedded resource.");

                return stream.ReadFully();
            }
        }

        private static Type GetMainProgramType()
        {
            // ReSharper disable once PossibleNullReferenceException
            return MethodBase.GetCurrentMethod().DeclaringType;
        }

        private Context Context { get; } = new Context();

        private void ParseArgs(string[] args)
        {
            var options = new[]
            {
                new CommandLineOption { Description = "AQTS app server credentials" },
                new CommandLineOption
                {
                    Key = nameof(Context.Server),
                    Setter = value => Context.Server = value,
                    Getter = () => Context.Server,
                    Description = "AQTS server"
                },
                new CommandLineOption
                {
                    Key = nameof(Context.Username),
                    Setter = value => Context.Username = value,
                    Getter = () => Context.Username,
                    Description = "AQTS username"
                },
                new CommandLineOption
                {
                    Key = nameof(Context.Password),
                    Setter = value => Context.Password = value,
                    Getter = () => Context.Password,
                    Description = "AQTS password"
                },
                new CommandLineOption(), new CommandLineOption { Description = "Generator settings" },
                new CommandLineOption
                {
                    Key = nameof(Context.IncludeDisabledPluginSettings),
                    Setter = value => Context.IncludeDisabledPluginSettings = bool.Parse(value),
                    Getter = () => $"{Context.IncludeDisabledPluginSettings}",
                    Description = "If true, include the settings for currently disabled plugins in the generated configuration."
                },
                new CommandLineOption
                {
                    Key = nameof(Context.SaveOnServer),
                    Setter = value => Context.SaveOnServer = bool.Parse(value),
                    Getter = () => $"{Context.SaveOnServer}",
                    Description = "If true, save the generated configuration as a MultiFile plugin setting on the server."
                },
                new CommandLineOption
                {
                    Key = nameof(Context.GenerateForExternalUse),
                    Setter = value => Context.GenerateForExternalUse = bool.Parse(value),
                    Getter = () => $"{Context.GenerateForExternalUse}",
                    Description = "If true, generate a config for external use, in the PluginTester or FieldVisitHotFolderService."
                },
                new CommandLineOption
                {
                    Key = nameof(Context.JsonPath),
                    Setter = value => Context.JsonPath = value,
                    Getter = () => Context.JsonPath,
                    Description = "If set, save the generated configuration to this file."
                },
            };

            var usageMessage = CommandLineUsage.ComposeUsageText(
                "Create a MultiFile configuration setting from an AQTS app server's current configuration.", options);

            var optionResolver = new CommandLineOptionResolver();

            optionResolver.Resolve(args, options, usageMessage);
        }

        private void Run()
        {
            new Generator
                {
                    Context = Context
                }
                .Run();
        }
    }
}
