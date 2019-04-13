using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.ServiceProcess;
using System.Xml;
using Common;
using log4net;

namespace FieldVisitHotFolderService
{
    public class Program
    {
        // ReSharper disable once InconsistentNaming
        private static ILog Log;

        static void Main(string[] args)
        {
            try
            {
                Environment.ExitCode = 1;

                ConfigureLogging();

                var context = GetContext(new Context(), args);
                new Program(context)
                    .Run();

                Log.Info("Successful exit.");
                Environment.ExitCode = 0;
            }
            catch (ExpectedException ex)
            {
                Log.Error(ex.Message);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
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

                Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
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

                using (var reader = new BinaryReader(stream))
                {
                    return reader.ReadBytes((int) stream.Length);
                }
            }
        }

        public static Context GetContext(Context context, string[] args)
        {
            var options = new[]
            {
                new CommandLineOption {Description = "AQTS connection settings"}, 
                new CommandLineOption
                {
                    Key = nameof(context.Server),
                    Setter = value => context.Server = value,
                    Getter = () => context.Server,
                    Description = "The AQTS app server."
                },
                new CommandLineOption
                {
                    Key = nameof(context.Username),
                    Setter = value => context.Username = value,
                    Getter = () => context.Username,
                    Description = "AQTS username."
                },
                new CommandLineOption
                {
                    Key = nameof(context.Password),
                    Setter = value => context.Password = value,
                    Getter = () => context.Password,
                    Description = "AQTS credentials."
                },
                new CommandLineOption
                {
                    Key = nameof(context.MaximumConnectionAttempts),
                    Setter = value => context.MaximumConnectionAttempts = int.Parse(value),
                    Getter = () => context.MaximumConnectionAttempts.ToString(),
                    Description = "The maximum number of connection attempts before exiting."
                },
                new CommandLineOption
                {
                    Key = nameof(context.ConnectionRetryDelay),
                    Setter = value => context.ConnectionRetryDelay = TimeSpan.Parse(value),
                    Getter = () => context.ConnectionRetryDelay.ToString(),
                    Description = "The TimeSpan to wait in between AQTS connection attempts."
                },

                new CommandLineOption(), new CommandLineOption{Description = "Local plugin settings"},
                new CommandLineOption
                {
                    Key = "Plugin",
                    Setter = value => context.Plugins.Add(value),
                    Getter = () => string.Empty,
                    Description = "A plugin assembly to use for parsing field visits locally. Can be set multiple times."
                },

                new CommandLineOption(), new CommandLineOption{Description = "File monitoring settings"}, 
                new CommandLineOption
                {
                    Key = nameof(context.HotFolderPath),
                    Setter = value => context.HotFolderPath = value,
                    Getter = () => context.HotFolderPath,
                    Description = "The root path to monitor for field visit files."
                },
                new CommandLineOption
                {
                    Key = nameof(context.FileMask),
                    Setter = value => context.FileMask = value,
                    Getter = () => context.FileMask,
                    Description = "A comma-separated list of file patterns to monitor. [defaults to '*.*' if omitted]"
                },
                new CommandLineOption
                {
                    Key = nameof(context.FileQuietDelay),
                    Setter = value => context.FileQuietDelay = TimeSpan.Parse(value),
                    Getter = () => context.FileQuietDelay.ToString(),
                    Description = "Timespan of no file activity before processing begins."
                },
                new CommandLineOption
                {
                    Key = nameof(context.ProcessingFolder),
                    Setter = value => context.ProcessingFolder = value,
                    Getter = () => context.ProcessingFolder,
                    Description = "Move files to this folder during processing."
                },
                new CommandLineOption
                {
                    Key = nameof(context.UploadedFolder),
                    Setter = value => context.UploadedFolder = value,
                    Getter = () => context.UploadedFolder,
                    Description = "Move files to this folder after successful uploads."
                },
                new CommandLineOption
                {
                    Key = nameof(context.FailedFolder),
                    Setter = value => context.FailedFolder = value,
                    Getter = () => context.FailedFolder,
                    Description = "Move files to this folder if an upload error occurs."
                },
            };

            var usageMessage = CommandLineUsage.ComposeUsageText(
                "Purpose: Monitors a folder for field visit files and appends them to an AQTS app server", options);

            var optionResolver = new CommandLineOptionResolver();

            optionResolver.Resolve(InjectOptionsFileByDefault(args), options, usageMessage, arg => ResolvePositionalArgument(context, arg));

            ValidateContext(context);

            return context;
        }

        private static string[] InjectOptionsFileByDefault(string[] args)
        {
            if (args.Any())
                return args;

            var defaultOptionsFile = Path.Combine(FileHelper.ExeDirectory, "Options.txt");

            if (File.Exists(defaultOptionsFile))
            {
                Log.Info($"Using '@{defaultOptionsFile}' configuration by default.");

                return new[] {"@" + defaultOptionsFile};
            }

            return args;
        }

        private static bool ResolvePositionalArgument(Context context, string arg)
        {
            if (Directory.Exists(arg))
            {
                context.HotFolderPath = arg;
                return true;
            }

            return false;
        }

        private static void ValidateContext(Context context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (string.IsNullOrWhiteSpace(context.Server))
                throw new ExpectedException($"You must specify a /{nameof(context.Server)} option.");
        }

        private readonly Context _context;

        public Program(Context context)
        {
            _context = context;
        }

        private void Run()
        {
            var service = new Service {Context = _context};

            if (Environment.UserInteractive)
            {
                Log.Info("Press Ctrl-C to terminate...");

                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    service.Stop();
                };

                service.RunUntilStopped();
            }
            else
            {
                ServiceBase.Run(service);
            }
        }
    }
}
