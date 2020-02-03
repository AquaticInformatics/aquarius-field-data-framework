using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Common;
using FieldDataPluginFramework;
using FieldDataPluginFramework.Context;
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
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public Context Context { get; set; }

        private IFieldDataPlugin Plugin { get; set; }
        private IFrameworkLogger Logger { get; set; }
        private int FailedCount { get; set; }

        public void Run()
        {
            Plugin = LoadPlugin();
            Logger = CreateLogger();

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

        private void ParseOneFile(string path)
        {
            var dataBytes = LoadDataStream(path);

            var appender = new FieldDataResultsAppender
            {
                UtcOffset = Context.LocationUtcOffset
            };

            var locationInfo = !string.IsNullOrEmpty(Context.LocationIdentifier)
                ? appender.CreateDummyLocationInfoByIdentifier(Context.LocationIdentifier)
                : null;

            appender.ForcedLocationInfo = locationInfo;
            appender.AppendedResults.PluginAssemblyQualifiedTypeName = Plugin.GetType().AssemblyQualifiedName;

            try
            {
                var result = ParseFile(dataBytes, appender, locationInfo);

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

        private ResultOrException ParseFile(byte[] dataBytes, FieldDataResultsAppender appender, LocationInfo locationInfo)
        {
            // Many plugins, like FlowTracker2, accept a ZIP archive that also matches the Zip-with-attachments pattern.
            // So always try the plugin first with the actual data
            if (TryParseFile(dataBytes, appender, locationInfo, out var directAttempt)
                && IsSuccessfulParse(directAttempt.Result))
                return new ResultOrException
                {
                    Result = directAttempt.Result
                };

            ResultOrException zipAttempt = null;

            if (ZipLoader.TryParse(dataBytes, out var zipWithAttachments)
                && TryParseFile(zipWithAttachments.PluginDataBytes, appender, locationInfo, out zipAttempt)
                && IsSuccessfulParse(zipAttempt.Result))
                return new ResultOrException
                {
                    Result = zipAttempt.Result,
                    Attachments = zipWithAttachments.Attachments
                };

            if (zipWithAttachments != null)
            {
                if (zipAttempt == null)
                    throw new ArgumentNullException(nameof(zipAttempt));

                // Something failed inside the Zip parser
                return new ResultOrException
                {
                    Result = zipAttempt.Result,
                    Attachments = zipWithAttachments.Attachments,
                    Exception = zipAttempt.Exception
                };
            }

            return new ResultOrException
            {
                Result = directAttempt.Result,
                Exception = directAttempt.Exception
            };
        }

        private static bool IsSuccessfulParse(ParseFileResult result)
        {
            return result.Status != ParseFileStatus.CannotParse;
        }

        private class ResultOrException
        {
            public ParseFileResult Result { get; set; }
            public List<Attachment> Attachments { get; set; }
            public Exception Exception { get; set; }
        }

        private bool TryParseFile(byte[] dataBytes, FieldDataResultsAppender appender, LocationInfo locationInfo, out ResultOrException resultOrException)
        {
            resultOrException = new ResultOrException();

            try
            {
                using (var stream = new MemoryStream(dataBytes))
                {
                    resultOrException.Result = locationInfo != null
                        ? Plugin.ParseFile(stream, locationInfo, appender, Logger)
                        : Plugin.ParseFile(stream, appender, Logger);
                }

                return true;
            }
            catch (Exception exception)
            {
                resultOrException.Exception = exception;

                return false;
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

        private void SummarizeResults(string path, ResultOrException resultOrException, AppendedResults appendedResults)
        {
            try
            {
                if (resultOrException.Result?.Status == Context.ExpectedStatus)
                {
                    SummarizeExpectedResults(path, resultOrException, appendedResults);
                }
                else
                {
                    SummarizeFailedResults(path, resultOrException, appendedResults);
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

        private void SummarizeExpectedResults(string path, ResultOrException resultOrException, AppendedResults appendedResults)
        {
            var result = resultOrException.Result;

            var attachmentSummary = GetAttachmentSummary(resultOrException.Attachments);

            if (resultOrException.Exception == null && result.Parsed)
            {
                if (!appendedResults.AppendedVisits.Any())
                {
                    throw new ExpectedException($"'{path}' was parsed{attachmentSummary} but no visits were appended.");
                }

                Log.Info($"Successfully parsed {appendedResults.AppendedVisits.Count} visits{attachmentSummary} from '{path}'.");
            }
            else
            {
                var actualError = resultOrException.Exception?.Message ?? result.ErrorMessage ?? string.Empty;
                var expectedError = Context.ExpectedError ?? string.Empty;

                if (!actualError.Equals(expectedError))
                    throw new ExpectedException(
                        $"Expected an error message of '{expectedError}' but received '{actualError}' instead while parsing '{path}'{attachmentSummary}.");

                Log.Info($"ParsedResult.Status == {Context.ExpectedStatus} with Error='{Context.ExpectedError}' as expected when parsing '{path}'{attachmentSummary}.");
            }
        }

        private void SummarizeFailedResults(string path, ResultOrException resultOrException, AppendedResults appendedResults)
        {
            if (resultOrException.Exception != null)
                throw new ExpectedException($"Can't parse '{path}'. {resultOrException.Exception.Message}");

            var result = resultOrException.Result;

            var attachmentSummary = GetAttachmentSummary(resultOrException.Attachments);

            if (result.Parsed)
                throw new ExpectedException(
                    $"'{path}' was parsed successfully with {appendedResults.AppendedVisits.Count} visits{attachmentSummary} appended when {Context.ExpectedStatus} was expected.");

            if (result.Status != ParseFileStatus.CannotParse)
                throw new ExpectedException(
                    $"Result: '{path}' Parsed={result.Parsed} Status={result.Status} ErrorMessage={result.ErrorMessage}{attachmentSummary}");

            var errorMessage = resultOrException.Exception?.Message ?? result.ErrorMessage;

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

        private IFrameworkLogger CreateLogger()
        {
            return Log4NetLogger.Create(LogManager.GetLogger(Path.GetFileNameWithoutExtension(Context.PluginPath)));
        }
    }
}
