using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using Common;
using log4net;
using ServiceStack;

namespace PluginPackager
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

                var context = ParseArgs(args);
                new Program(context).Run();

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

        private static Context ParseArgs(string[] args)
        {
            var context = new Context();

            var options = new[]
            {
                new CommandLineOption {Key = nameof(context.AssemblyFolder), Setter = value => context.AssemblyFolder = value, Getter = () => context.AssemblyFolder, Description = "Path to the folder containing the plugin."},
                new CommandLineOption {Key = nameof(context.AssemblyPath), Setter = value => context.AssemblyPath = value, Getter = () => context.AssemblyPath, Description = "Path to the plugin assembly."},
                new CommandLineOption {Key = nameof(context.OutputPath), Setter = value => context.OutputPath = value, Getter = () => context.OutputPath, Description = "Path to packaged output file."},
                new CommandLineOption {Key = nameof(context.DeployedFolderName), Setter = value => context.DeployedFolderName = value, Getter = () => context.DeployedFolderName, Description = "Name of the deployed folder"},
                new CommandLineOption {Key = nameof(context.Description), Setter = value => context.Description = value, Getter = () => context.Description, Description = "Description of the plugin"},
                new CommandLineOption {Key = nameof(context.AssemblyQualifiedTypeName), Setter = value => context.AssemblyQualifiedTypeName = value, Getter = () => context.AssemblyQualifiedTypeName, Description = "Assembly-qualified type name of the plugin"},
                new CommandLineOption {Key = nameof(context.Subfolders), Setter = value => context.Subfolders = bool.Parse(value), Getter = () => context.Subfolders.ToString(), Description = "Include all subfolders"},
                new CommandLineOption {Key = nameof(context.Include), Setter = value => AddToList(value, context.Include), Getter = () => string.Join(", ", context.Include), Description = "Include file or DOS wildcard pattern"},
                new CommandLineOption {Key = nameof(context.Exclude), Setter = value => AddToList(value, context.Exclude), Getter = () => string.Join(", ", context.Exclude), Description = "Exclude file or DOS wildcard pattern"},
            };

            var usageMessage = CommandLineUsage.ComposeUsageText(
                "Package a field data plugin into a deployable .plugin file.", options);

            var optionResolver = new CommandLineOptionResolver();

            optionResolver.Resolve(args, options, usageMessage, arg => ResolvePositionalArgument(context, arg));

            if (string.IsNullOrEmpty(context.AssemblyFolder) && string.IsNullOrEmpty(context.AssemblyPath))
                throw new ExpectedException($"You must specify at least one /{nameof(context.AssemblyPath)} or /{nameof(context.AssemblyFolder)} option.");

            if (string.IsNullOrEmpty(context.OutputPath))
                throw new ExpectedException($"You must specify the /{nameof(context.OutputPath)} option.");

            return context;
        }

        private static bool ResolvePositionalArgument(Context context, string arg)
        {
            if (Directory.Exists(arg))
            {
                context.AssemblyFolder = arg;
                return true;
            }

            if (File.Exists(arg))
            {
                context.AssemblyPath = arg;
                return true;
            }

            return false;
        }

        private static void AddToList(string value, List<string> values)
        {
            if (value.Equals("~"))
            {
                values.Clear();
            }
            else
            {
                values.Add(value);
            }
        }

        private readonly Context _context;

        public Program(Context context)
        {
            _context = context;
        }

        private void Run()
        {
            new Packager
            {
                Context = _context
            }.CreatePackage();
        }
    }
}
