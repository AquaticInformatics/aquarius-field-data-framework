using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using Aquarius.TimeSeries.Client;
using Aquarius.TimeSeries.Client.Helpers;
using Aquarius.TimeSeries.Client.ServiceModels.Acquisition;
using Aquarius.TimeSeries.Client.ServiceModels.Publish;
using Common;
using FieldDataPluginFramework;
using FieldDataPluginFramework.Context;
using FieldDataPluginFramework.Results;
using FieldDataPluginFramework.Serialization;
using log4net;
using ServiceStack;
using ServiceStack.Text;
using ILog = log4net.ILog;

namespace FieldVisitHotFolderService
{
    public class FileProcessor
    {
        private static readonly ILog Log4NetLog = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public Context Context { get; set; }
        public string ProcessingFolder { get; set; }
        public string PartialFolder { get; set; }
        public string UploadedFolder { get; set; }
        public string ArchivedFolder { get; set; }
        public string FailedFolder { get; set; }
        public List<IFieldDataPlugin> Plugins { get; set; }
        public IAquariusClient Client { get; set; }
        public List<LocationInfo> LocationCache { get; set; }
        private FileLogger Log { get; } = new FileLogger {Log = Log4NetLog};

        public void ProcessFile(string sourcePath)
        {
            string processingPath;

            try
            {
                processingPath = MoveFile(sourcePath, ProcessingFolder, false);
            }
            catch (IOException exception)
            {
                Log.Warn($"Skipping '{sourcePath}", exception);
                return;
            }

            try
            {
                var appendedResults = ParseLocalFile(processingPath);
                var isPartial = UploadResults(processingPath, appendedResults);

                if (isPartial)
                    MoveFile(processingPath, PartialFolder);
                else
                    MoveFile(processingPath, UploadedFolder);
            }
            catch (Exception exception)
            {
                Log.Error(exception.Message);

                try
                {
                    MoveFile(processingPath, FailedFolder);
                }
                catch (Exception moveException)
                {
                    Log.Warn($"Can't move '{processingPath}' to '{FailedFolder}'", moveException);
                }
            }
        }

        private string MoveFile(string path, string targetFolder, bool writeTargetLog = true)
        {
            var extension = Path.GetExtension(path);
            var baseFilename = Path.GetFileNameWithoutExtension(path);
            string targetPath;

            for (var attempt = 0; ; ++attempt)
            {
                var filename = attempt > 0
                               ? $"{baseFilename} ({attempt}){extension}"
                               : $"{baseFilename}{extension}";

                targetPath = Path.Combine(targetFolder, filename);

                if (!File.Exists(targetPath))
                    break;
            }

            Log.Info($"Moving '{path}' to '{targetPath}'");
            File.Move(path, targetPath);

            if (writeTargetLog)
                File.WriteAllText($"{targetPath}.log", Log.AllText());

            return targetPath;
        }

        private AppendedResults ParseLocalFile(string path)
        {
            var fileBytes = LoadFileBytes(path);

            var appender = new FieldDataResultsAppender
            {
                Client = Client,
                LocationCache = LocationCache
            };

            foreach (var plugin in Plugins)
            {
                var pluginName = plugin.GetType().FullName;

                try
                {
                    var logger = Log4NetLogger.Create(LogManager.GetLogger(plugin.GetType()));

                    using (var stream = new MemoryStream(fileBytes))
                    {
                        var result = plugin.ParseFile(stream, appender, logger);

                        // TODO: Support Zip-with-attachments

                        if (result.Status == ParseFileStatus.CannotParse)
                            continue;

                        if (result.Status != ParseFileStatus.SuccessfullyParsedAndDataValid)
                            throw new ArgumentException(
                                $"Error parsing '{path}' with {pluginName}: {result.ErrorMessage}");

                        if (!appender.AppendedResults.AppendedVisits.Any())
                            throw new ArgumentException($"{pluginName} did not parse any field visits.");

                        Log.Info(
                            $"{pluginName} parsed '{path}' with {appender.AppendedResults.AppendedVisits.Count} visits: {string.Join(", ", appender.AppendedResults.AppendedVisits.Select(v => v.FieldVisitIdentifier))}");
                    }

                    appender.AppendedResults.PluginAssemblyQualifiedTypeName = plugin.GetType().AssemblyQualifiedName;
                    return appender.AppendedResults;
                }
                catch (Exception e)
                {
                    Log.Warn($"{pluginName} skipping '{path}': {e.Message}");
                }
            }

            throw new ArgumentException($"'{path}' was not parsed by any plugin.");
        }

        private byte[] LoadFileBytes(string path)
        {
            if (!File.Exists(path))
                throw new ExpectedException($"Data file '{path}' does not exist.");

            Log.Info($"Loading data file '{path}'");

            return File.ReadAllBytes(path);
        }

        private bool UploadResults(string path, AppendedResults appendedResults)
        {
            var isPartial = false;

            foreach (var visit in appendedResults.AppendedVisits)
            {
                var singleResult = new AppendedResults
                {
                    FrameworkAssemblyQualifiedName = appendedResults.FrameworkAssemblyQualifiedName,
                    PluginAssemblyQualifiedTypeName = appendedResults.PluginAssemblyQualifiedTypeName,
                    AppendedVisits = new List<FieldVisitInfo> { visit }
                };

                if (ShouldSkipConflictingVisits(visit))
                {
                    isPartial = true;
                    continue;
                }

                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(singleResult.ToJson())))
                {
                    var uploadedFilename = Path.GetFileName(path) + ".json";

                    var response = Client.Acquisition.PostFileWithRequest(stream, uploadedFilename, new PostVisitFile
                    {
                        LocationUniqueId = Guid.Parse(visit.LocationInfo.UniqueId)
                    });

                    Log.Info($"Uploaded '{uploadedFilename}' to '{visit.LocationInfo.LocationIdentifier}' using {response.HandledByPlugin.Name} plugin");
                }
            }

            return isPartial;
        }

        private bool ShouldSkipConflictingVisits(FieldVisitInfo visit)
        {
            var existingVisits = Client.Publish.Get(new FieldVisitDescriptionListServiceRequest
            {
                LocationIdentifier = visit.LocationInfo.LocationIdentifier,
                QueryFrom = StartOfDay(visit.StartDate),
                QueryTo = EndOfDay(visit.EndDate)
            })
                .FieldVisitDescriptions;

            if (!existingVisits.Any())
                return false;

            switch (Context.MergeMode)
            {
                case MergeMode.Fail:
                    throw new ExpectedException($"A conflicting visit already exists on {visit.StartDate.Date:yyyy-MM-dd} at location '{visit.LocationInfo.LocationIdentifier}'");
                case MergeMode.Skip:
                    Log.Warn($"Skipping conflicting visit {visit.FieldVisitIdentifier} with {string.Join(", ", existingVisits.Select(v => $"{v.StartTime}/{v.EndTime}"))}");
                    return true;
                case MergeMode.Replace:
                case MergeMode.ArchiveAndReplace:
                    DeleteExistingVisits(existingVisits);
                    return false;
                default:
                    throw new ExpectedException($"{Context.MergeMode} is an unsupported {nameof(MergeMode)} value.");
            }
        }

        private void DeleteExistingVisits(List<FieldVisitDescription> existingVisits)
        {
            foreach (var visit in existingVisits)
            {
                DeleteExistingVisit(visit);
            }
        }

        private void DeleteExistingVisit(FieldVisitDescription visit)
        {
            if (Context.MergeMode == MergeMode.ArchiveAndReplace)
                ArchiveExistingVisit(visit);
        }

        private void ArchiveExistingVisit(FieldVisitDescription visit)
        {
            var archivedVisit = new ArchivedVisit
            {
                Summary = visit,
                Activities = Client.Publish.Get(new FieldVisitDataServiceRequest
                {
                    FieldVisitIdentifier = visit.Identifier,
                    IncludeNodeDetails = true,
                    IncludeInvalidActivities = true,
                    IncludeCrossSectionSurveyProfile = true,
                    IncludeVerticals = true
                })
            };

            var archiveFilenameBase = Path.Combine(ArchivedFolder, $"{visit.Identifier}_{visit.StartTime?.Date:yyyy-MM-dd}_{visit.LocationIdentifier}");
            
            Log.Info($"Archiving existing visit '{archiveFilenameBase}'.json");
            File.WriteAllText(archiveFilenameBase+".json", archivedVisit.ToJson().IndentJson());

            var publishClient = Client.Publish as ServiceClientBase;

            if (publishClient == null) return;

            foreach (var attachment in archivedVisit.Activities.Attachments)
            {
                var attachmentUrl = $"{publishClient.BaseUri}/{attachment.Url}";
                var attachmentFilename = $"{archiveFilenameBase}_{attachment.FileName}";

                Log.Info($"Archiving attachment '{attachmentFilename}' from {attachmentUrl}");
                File.WriteAllBytes(
                    attachmentFilename,
                    attachmentUrl.GetBytesFromUrl(requestFilter: SetAuthenticationHeaders));
            }
        }

        private void SetAuthenticationHeaders(HttpWebRequest request)
        {
            request.Headers[AuthenticationHeaders.AuthenticationHeaderNameKey] = GetSessionToken();
        }

        private string GetSessionToken()
        {
            var connection =
                ConnectionPool.Instance.GetConnection(Context.Server, Context.Username, Context.Password, null);

            return connection.SessionToken;
        }

        private static DateTimeOffset StartOfDay(DateTimeOffset dateTimeOffset)
        {
            return new DateTimeOffset(dateTimeOffset.Date, dateTimeOffset.Offset);
        }

        private static DateTimeOffset EndOfDay(DateTimeOffset dateTimeOffset)
        {
            var start = StartOfDay(dateTimeOffset);

            return new DateTimeOffset(start.Year, start.Month, start.Day, 23, 59, 59, start.Offset);
        }
    }
}
