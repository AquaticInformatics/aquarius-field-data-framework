using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Common;
using FieldDataPluginFramework;
using FieldDataPluginFramework.Results;
using FieldDataPluginFramework.Serialization;
using Humanizer;
using log4net;
using ServiceStack;
using ServiceStack.Text;
using IFrameworkLogger = FieldDataPluginFramework.ILog;
using ILog = log4net.ILog;

namespace PluginTester
{
    public class Tester
    {
        // ReSharper disable once PossibleNullReferenceException
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public Context Context { get; set; }

        private IFieldDataPlugin Plugin { get; set; }
        private IFrameworkLogger Logger { get; set; }
        private int FailedCount { get; set; }

        public void Run()
        {
            ConfigureJson();

            Logger = CreateLogger();
            Plugin = LoadPlugin();

            var frameworkInterfaceType = typeof(IFieldDataPlugin);

            var frameworkContext = File.Exists(frameworkInterfaceType.Assembly.Location)
                ? frameworkInterfaceType.Assembly.Location
                : $"embedded resource";

            Log.Info($"'{FileHelper.ExeNameAndVersion}' loaded '{Plugin.GetType().AssemblyQualifiedName}' from '{Context.PluginPath}' via '{frameworkInterfaceType.AssemblyQualifiedName}' from '{frameworkContext}'.");

            foreach (var path in Context.DataPaths)
            {
                ParseOneFile(path);
            }

            if (FailedCount > 0)
            {
                throw new ExpectedException($"{FailedCount} files of {Context.DataPaths.Count} failed to parse as expected, check the log for details.");
            }

            if (Context.DataPaths.Count > 1)
            {
                Log.Info($"{Context.DataPaths.Count} files parsed as expected.");
            }
        }

        private void ConfigureJson()
        {
            JsonConfig.Configure();
        }

        private void ParseOneFile(string path)
        {
            var dataBytes = LoadDataStream(path);

            var appender = new FieldDataResultsAppender
            {
                UtcOffset = Context.LocationUtcOffset,
                Settings = Context.Settings,
            };

            var locationInfo = !string.IsNullOrEmpty(Context.LocationIdentifier)
                ? appender.CreateDummyLocationInfoByIdentifier(Context.LocationIdentifier)
                : null;

            appender.ForcedLocationInfo = locationInfo;
            appender.AppendedResults.PluginAssemblyQualifiedTypeName = Plugin.GetType().AssemblyQualifiedName;

            try
            {
                var result = new ZipLoader
                    {
                        Plugin = Plugin,
                        Logger = Logger,
                        Appender = appender,
                        LocationInfo = locationInfo
                    }
                    .ParseFile(dataBytes);

                SaveAppendedResults(path, appender.AppendedResults);

                SummarizeResults(path, result, appender.AppendedResults);
            }
            catch (ExpectedException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Log.Error("Plugin has thrown an error", exception);

                throw new ExpectedException($"Unhandled plugin exception: {exception.Message}");
            }
        }

        private void SaveAppendedResults(string sourcePath, AppendedResults appendedResults)
        {
            if (string.IsNullOrEmpty(Context.JsonPath))
                return;

            var savePath = Directory.Exists(Context.JsonPath)
                ? Path.Combine(Context.JsonPath, $"{Path.GetFileName(sourcePath)}.json")
                : Context.JsonPath;

            Log.Info($"Saving {appendedResults.AppendedVisits.Count} visits data to '{savePath}'");

            File.WriteAllText(savePath, appendedResults.ToJson().IndentJson());
        }

        private void SummarizeResults(string path, ParseFileResultWithAttachments resultWithAttachments, AppendedResults appendedResults)
        {
            try
            {
                // Use a string comparison of the enum status, since they are not the same enum (which allows different frameworks to be used)
                if ($"{resultWithAttachments.Result?.Status}" == $"{Context.ExpectedStatus}")
                {
                    SummarizeExpectedResults(path, resultWithAttachments, appendedResults);
                }
                else
                {
                    SummarizeFailedResults(path, resultWithAttachments, appendedResults);
                }

            }
            catch (ExpectedException e)
            {
                if (Context.DataPaths.Count > 1)
                {
                    Log.Error(e.Message);
                    ++FailedCount;
                }
                else
                {
                    throw;
                }
            }
        }

        private void SummarizeExpectedResults(string path, ParseFileResultWithAttachments resultWithAttachments, AppendedResults appendedResults)
        {
            var result = resultWithAttachments.Result;

            var attachmentSummary = GetAttachmentSummary(resultWithAttachments.Attachments);

            if (result.Parsed)
            {
                if (!appendedResults.AppendedVisits.Any())
                {
                    throw new ExpectedException($"'{path}' was parsed{attachmentSummary} but no visits were appended.");
                }

                Log.Info($"Successfully parsed {appendedResults.AppendedVisits.Count} visits{attachmentSummary} from '{path}'.");
            }
            else
            {
                var actualError = result.ErrorMessage ?? string.Empty;
                var expectedError = Context.ExpectedError ?? string.Empty;

                if (!actualError.Equals(expectedError))
                    throw new ExpectedException(
                        $"Expected an error message of '{expectedError}' but received '{actualError}' instead while parsing '{path}'{attachmentSummary}.");

                Log.Info($"ParsedResult.Status == {Context.ExpectedStatus} with Error='{Context.ExpectedError}' as expected when parsing '{path}'{attachmentSummary}.");
            }
        }

        private void SummarizeFailedResults(string path, ParseFileResultWithAttachments resultWithAttachments, AppendedResults appendedResults)
        {
            var result = resultWithAttachments.Result;

            var attachmentSummary = GetAttachmentSummary(resultWithAttachments.Attachments);

            if (result.Parsed)
                throw new ExpectedException(
                    $"'{path}' was parsed successfully with {appendedResults.AppendedVisits.Count} visits{attachmentSummary} appended when {Context.ExpectedStatus} was expected.");

            if (result.Status != ParseFileStatus.CannotParse)
                throw new ExpectedException(
                    $"Result: '{path}' Parsed={result.Parsed} Status={result.Status} ErrorMessage={result.ErrorMessage}{attachmentSummary}");

            var errorMessage = result.ErrorMessage;

            if (!string.IsNullOrEmpty(errorMessage))
            {
                throw new ExpectedException($"Can't parse '{path}'. {errorMessage}{attachmentSummary}");
            }

            throw new ExpectedException($"File '{path}'{attachmentSummary} is not parsed by the plugin.");
        }

        private static string GetAttachmentSummary(List<Attachment> attachments)
        {
            if (attachments == null || !attachments.Any())
                return string.Empty;

            var totalByteSize = attachments
                .Sum(a => a.ByteSize);

            return $" and {totalByteSize.Bytes().Humanize("#.#")} in {attachments.Count} attachments: {string.Join(", ", attachments.Select(a => $"'{Path.GetFileName(a.Path)}' ({a.ByteSize.Bytes().Humanize("#.#")})"))}";
        }

        private byte[] LoadDataStream(string path)
        {
            if (!File.Exists(path))
                throw new ExpectedException($"Data file '{path}' does not exist.");

            Log.Info($"Loading data file '{path}'");

            return File.ReadAllBytes(path);
        }

        private IFieldDataPlugin LoadPlugin()
        {
            var pluginPath = Path.GetFullPath(Context.PluginPath);

            if (!File.Exists(pluginPath) && !Directory.Exists(pluginPath))
                throw new ExpectedException($"Plugin file '{pluginPath}' does not exist.");

            var plugins = new PluginLoader
                {
                    Log = Logger,
                    Verbose = Context.Verbose
                }
                .LoadPlugins(new List<string> { pluginPath });

            if (!plugins.Any())
                throw new ExpectedException($"No {nameof(IFieldDataPlugin)} plugin implementations found in '{pluginPath}'.");

            if (plugins.Count > 1)
                throw new ExpectedException($"{plugins.Count} {nameof(IFieldDataPlugin)} plugin implementations found in '{pluginPath}'.");

            var plugin = plugins.Single().Plugin;

            PluginLoader.SetMainPlugin(plugin, pluginPath);

            return plugin;
        }

        private IFrameworkLogger CreateLogger()
        {
            return Log4NetLogger.Create(LogManager.GetLogger(Path.GetFileNameWithoutExtension(Context.PluginPath)));
        }
    }
}
