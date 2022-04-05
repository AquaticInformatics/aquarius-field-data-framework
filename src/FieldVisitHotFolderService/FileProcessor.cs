using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Aquarius.TimeSeries.Client;
using Aquarius.TimeSeries.Client.Helpers;
using Aquarius.TimeSeries.Client.ServiceModels.Acquisition;
using Aquarius.TimeSeries.Client.ServiceModels.Provisioning;
using Aquarius.TimeSeries.Client.ServiceModels.Publish;
using Common;
using FieldDataPluginFramework;
using FieldDataPluginFramework.Context;
using FieldDataPluginFramework.Results;
using FieldDataPluginFramework.Serialization;
using Humanizer;
using log4net;
using NodaTime;
using ServiceStack;
using ServiceStack.Text;
using Attachment = Common.Attachment;
using ILog = log4net.ILog;

namespace FieldVisitHotFolderService
{
    public class FileProcessor
    {
        // ReSharper disable once PossibleNullReferenceException
        private static readonly ILog Log4NetLog = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public Context Context { get; set; }
        public string ProcessingFolder { get; set; }
        public string PartialFolder { get; set; }
        public string UploadedFolder { get; set; }
        public string ArchivedFolder { get; set; }
        public string FailedFolder { get; set; }
        public List<PluginLoader.LoadedPlugin> LoadedPlugins { get; set; }
        public IAquariusClient Client { get; set; }
        public List<LocationInfo> LocationCache { get; set; }
        public ReferencePointCache ReferencePointCache { get; set; }
        public MethodLookup MethodLookup { get; set; }
        public ParameterIdLookup ParameterIdLookup { get; set; }
        public CancellationToken CancellationToken { get; set; }
        private FileLogger Log { get; } = new FileLogger(Log4NetLog);

        public void ProcessZipEntry(ZipArchiveEntry entry)
        {
            try
            {
                var locationIdentifier = MigrationProjectHelper.GetLocationIdentifier(entry);

                var fileBytes = LoadFileBytes(entry);
                var uploadContext = ParseLocalFile(entry.FullName, fileBytes, locationIdentifier);
                UploadResultsConcurrently(uploadContext);
            }
            catch (Exception exception)
            {
                LogProcessingException(exception);
            }
        }

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
                var fileBytes = LoadFileBytes(processingPath);
                var uploadContext = ParseLocalFile(processingPath, fileBytes);
                var results = UploadResultsConcurrently(uploadContext);

                if (results.IsFailure)
                    MoveFiles(FailedFolder, results.PathsToMove);
                else if (results.IsPartial)
                    MoveFiles(PartialFolder, results.PathsToMove);
                else
                    MoveFiles(UploadedFolder, results.PathsToMove);
            }
            catch (Exception exception)
            {
                LogProcessingException(exception);

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

        private void LogProcessingException(Exception exception)
        {
            var message = exception.Message;

            if (exception is AggregateException aggregateException)
            {
                message = aggregateException.InnerExceptions.Count == 1
                    ? aggregateException.InnerExceptions.First().Message
                    : $"{aggregateException.InnerExceptions.Count} concurrent errors: {string.Join("\n", aggregateException.InnerExceptions.Take(5).Select(e => e.Message))}";
            }

            Log.Error(message);
        }

        private void MoveFiles(string targetFolder, List<string> paths)
        {
            foreach (var path in paths.Take(paths.Count - 1))
            {
                MoveFile(path, targetFolder, false);
            }

            MoveFile(paths.Last(), targetFolder);
        }

        private string MoveFile(string path, string targetFolder, bool writeTargetLog = true)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("A path is is required", nameof(path));

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

            CreateTargetFolder(targetFolder);

            File.Move(path, targetPath);

            if (writeTargetLog)
                File.WriteAllText($"{targetPath}.log", Log.AllText());

            return targetPath;
        }

        private void CreateTargetFolder(string targetFolder)
        {
            if (!Directory.Exists(targetFolder))
            {
                Log.Info($"Creating '{targetFolder}'");
                Directory.CreateDirectory(targetFolder);
            }
        }

        private UploadContext ParseLocalFile(string path, byte[] fileBytes, string locationIdentifier = null)
        {
            var appender = new FieldDataResultsAppender
            {
                Client = Client,
                LocationCache = LocationCache,
                LocationAliases = Context.LocationAliases,
                Log = Log
            };

            foreach (var loadedPlugin in LoadedPlugins)
            {
                appender.SettingsFunc = () => GetPluginSettings(loadedPlugin);

                var pluginName = PluginLoader.GetPluginNameAndVersion(loadedPlugin.Plugin);

                try
                {
                    var resultWithAttachments = new ZipLoader
                        {
                            Plugin = loadedPlugin.Plugin,
                            Appender = appender,
                            Logger = Log,
                            LocationInfo = string.IsNullOrEmpty(locationIdentifier)
                                ? null
                                : appender.GetLocationByIdentifier(locationIdentifier)
                        }
                        .ParseFile(fileBytes);

                    if (resultWithAttachments.Result.Status == ParseFileStatus.CannotParse)
                        continue;

                    if (resultWithAttachments.Result.Status != ParseFileStatus.SuccessfullyParsedAndDataValid)
                        throw new ArgumentException(
                            $"Error parsing '{path}' with {pluginName}: {resultWithAttachments.Result.ErrorMessage}");

                    if (!appender.AppendedResults.AppendedVisits.Any())
                        throw new ArgumentException($"{pluginName} did not parse any field visits.");

                    var attachmentCount = resultWithAttachments.Attachments?.Count ?? 0;

                    if (appender.AppendedResults.AppendedVisits.Count > 1 && attachmentCount > 0)
                        throw new ArgumentException($"Only single-visit data files can be uploaded with attachments.");

                    Log.Info(
                        $"{pluginName} parsed '{path}' with {appender.AppendedResults.AppendedVisits.Count} visits: {string.Join(", ", appender.AppendedResults.AppendedVisits.Take(10).Select(v => v.FieldVisitIdentifier))}");

                    appender.AppendedResults.PluginAssemblyQualifiedTypeName = loadedPlugin.GetType().AssemblyQualifiedName;

                    return new UploadContext
                    {
                        Path = path,
                        AppendedResults = appender.AppendedResults,
                        Attachments = resultWithAttachments.Attachments
                    };
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

        private byte[] LoadFileBytes(ZipArchiveEntry entry)
        {
            if (entry.Length > int.MaxValue)
                throw new ExpectedException($"Zip entry '{entry.FullName}' is too big ({entry.Length.Bytes().Humanize("#.#")}) to uncompress.");

            using (var stream = entry.Open())
            using (var reader = new BinaryReader(stream))
            {
                Log.Info($"Loading zip entry '{entry.FullName}'");

                return reader.ReadBytes((int)entry.Length);
            }
        }

        private Dictionary<string, string> GetPluginSettings(PluginLoader.LoadedPlugin loadedPlugin)
        {
            var pluginFolderName = loadedPlugin.Manifest.PluginFolderName;

            if (Context.PluginSettings.TryGetValue(pluginFolderName, out var settings))
                return settings;

            var targetSettingGroup = $"FieldDataPluginConfig-{pluginFolderName}";

            // We ask for all settings and filter the results in-memory to avoid a 404 GroupNameNotFound exception when no configuration exist
            return Client.Provisioning.Get(new GetSettings())
                .Results
                .Where(setting => setting.Group.Equals(targetSettingGroup, StringComparison.InvariantCultureIgnoreCase))
                .ToDictionary(
                    setting => setting.Key,
                    setting => setting.Value,
                    StringComparer.InvariantCultureIgnoreCase);
        }

        public class UploadedResults
        {
            public bool IsPartial { get; set; }
            public bool IsFailure { get; set; }
            public List<string> PathsToMove { get; } = new List<string>();
        }

        public class UploadContext
        {
            public string Path { get; set; }
            public string UploadedFilename { get; set; }
            public AppendedResults AppendedResults { get; set; }
            public List<Attachment> Attachments { get; set; }

            public bool HasAttachments => Attachments?.Any() ?? false;
        }

        private UploadedResults UploadResultsConcurrently(UploadContext uploadContext)
        {
            var uploadedResults = new UploadedResults
            {
                IsPartial = false,
                IsFailure = false,
                PathsToMove = {uploadContext.Path}
            };

            if (!uploadContext.AppendedResults.AppendedVisits.Any())
            {
                Log.Warn($"No visits parsed from '{uploadContext.Path}'");

                return uploadedResults;
            }

            var unknownVisits = uploadContext.AppendedResults
                .AppendedVisits
                .Where(IsUnknownLocation)
                .ToList();

            var largeDurationVisits = uploadContext.AppendedResults
                .AppendedVisits
                .Where(visit => !unknownVisits.Contains(visit) && IsVisitDurationExceeded(visit))
                .ToList();

            var visitsToAppend = uploadContext.AppendedResults
                .AppendedVisits
                .Where(visit => !unknownVisits.Contains(visit) && !largeDurationVisits.Contains(visit))
                .ToList();

            if (unknownVisits.Any())
            {
                Log.Warn($"Skipping {unknownVisits.Count} visits for unknown locations");
            }

            if (largeDurationVisits.Any())
            {
                Log.Warn($"Skipping {largeDurationVisits.Count} visits exceeding /{nameof(Context.MaximumVisitDuration)}={Context.MaximumVisitDuration:g}");

                uploadedResults.PathsToMove.Add(SaveLargeVisits(uploadContext.AppendedResults, largeDurationVisits, uploadContext.Path));
            }

            if (!visitsToAppend.Any())
            {
                Log.Error($"None of the {uploadContext.AppendedResults.AppendedVisits.Count} visits can be imported.");
            }

            var isIncomplete = unknownVisits.Any() || largeDurationVisits.Any();
            var isFailure = isIncomplete && !visitsToAppend.Any();
            var isPartial = !isFailure && isIncomplete;

            uploadContext.UploadedFilename = Path.GetFileName(uploadContext.Path) + ".json";

            if (visitsToAppend.Any())
            {
                Log.Info($"Appending {visitsToAppend.Count} visits using {Context.MaximumConcurrentRequests} concurrent requests.");

                var duplicateVisits = new List<FieldVisitInfo>();

                UploadVisitsConcurrently(uploadContext, visitsToAppend, duplicateVisits, ref isPartial, ref isFailure);

                if (duplicateVisits.Any())
                {
                    for (var retryAttempts = 0; !CancellationToken.IsCancellationRequested
                                                && duplicateVisits.Any()
                                                && retryAttempts < Context.MaximumDuplicateRetry; ++retryAttempts)
                    {
                        Log.Info($"Retrying {duplicateVisits.Count} duplicate visits.");

                        visitsToAppend.Clear();
                        visitsToAppend.AddRange(duplicateVisits);
                        duplicateVisits.Clear();

                        UploadVisitsConcurrently(uploadContext, visitsToAppend, duplicateVisits, ref isPartial, ref isFailure);
                    }

                    if (duplicateVisits.Any())
                    {
                        Log.Error($"Could not resolve {duplicateVisits.Count} duplicate visits.");
                        isFailure = true;
                    }
                }
            }

            uploadedResults.IsPartial = isPartial;
            uploadedResults.IsFailure = isFailure;

            return uploadedResults;
        }

        private string SaveLargeVisits(AppendedResults appendedResults, List<FieldVisitInfo> largeVisits, string path)
        {
            var largePath = Path.Combine(
                // ReSharper disable once AssignNullToNotNullAttribute
                Path.GetDirectoryName(path),
                $"{Path.GetFileNameWithoutExtension(path)}.LargeDuration.json");

            var largeResults = new AppendedResults
            {
                PluginAssemblyQualifiedTypeName = appendedResults.PluginAssemblyQualifiedTypeName,
                FrameworkAssemblyQualifiedName = appendedResults.FrameworkAssemblyQualifiedName,
                AppendedVisits = largeVisits
            };

            Log.Info($"Saving {largeResults.AppendedVisits.Count} visits data to '{largePath}'");

            File.WriteAllText(largePath, largeResults.ToJson().IndentJson());

            return largePath;
        }

        private void UploadVisitsConcurrently(
            UploadContext uploadContext,
            List<FieldVisitInfo> visitsToAppend,
            List<FieldVisitInfo> duplicateVisits,
            ref bool isPartial,
            ref bool isFailure)
        {
            var semaphore = new SemaphoreSlim(Context.MaximumConcurrentRequests);

            var localIsPartial = false;
            var localIsFailure = false;

            Task.WhenAll(visitsToAppend.Select(async visit =>
            {
                using (await LimitedConcurrencyContext.EnterContextAsync(semaphore))
                {
                    await Task.Run(() =>
                            UploadVisit(
                                visit,
                                uploadContext,
                                () => localIsPartial = true,
                                () => localIsFailure = true,
                                () => duplicateVisits.Add(visit))
                        , CancellationToken);
                }
            })).Wait(CancellationToken);

            isPartial |= localIsPartial;
            isFailure |= localIsFailure;
        }

        private bool IsUnknownLocation(FieldVisitInfo visit)
        {
            return !Guid.TryParse(visit.LocationInfo.UniqueId, out var uniqueId) || uniqueId == Guid.Empty;
        }

        private bool IsVisitDurationExceeded(FieldVisitInfo visit)
        {
            return visit.EndDate - visit.StartDate > Context.MaximumVisitDuration;
        }

        private void UploadVisit(FieldVisitInfo visit,
            UploadContext uploadContext,
            Action partialAction,
            Action failureAction,
            Action duplicateAction = null)
        {
            if (ShouldSkipConflictingVisits(visit))
            {
                partialAction();
                return;
            }

            if (Context.DryRun)
            {
                Log.Warn($"Dry-run: Would upload '{visit.FieldVisitIdentifier}'");
                return;
            }

            var singleResult = new AppendedResults
            {
                FrameworkAssemblyQualifiedName = uploadContext.AppendedResults.FrameworkAssemblyQualifiedName,
                PluginAssemblyQualifiedTypeName = uploadContext.AppendedResults.PluginAssemblyQualifiedTypeName,
                AppendedVisits = new List<FieldVisitInfo> { visit }
            };

            try
            {
                using (var stream = new MemoryStream(GetUploadFileBytes(singleResult, uploadContext)))
                {
                    var uploadedFilename = ComposeUploadedFilename(uploadContext, visit);

                    var response = Client.Acquisition.PostFileWithRequest(stream, uploadedFilename,
                        new PostVisitFileToLocation
                        {
                            LocationUniqueId = Guid.Parse(visit.LocationInfo.UniqueId)
                        });

                    Log.Info($"Uploaded '{uploadedFilename}' {visit.FieldVisitIdentifier} to '{visit.LocationInfo.LocationIdentifier}' using {response.HandledByPlugin.Name} plugin");
                }
            }
            catch (WebServiceException exception)
            {
                if (IsDuplicateAttachmentException(exception))
                {
                    Log.Info($"{uploadContext.UploadedFilename}: Skipping already uploaded content.");
                    return;
                }

                if (duplicateAction != null && IsDuplicateFailure(exception))
                {
                    Log.Warn($"{uploadContext.UploadedFilename}: Saving {visit.FieldVisitIdentifier} for later retry: {exception.ErrorCode} {exception.ErrorMessage}");

                    duplicateAction();
                    return;
                }

                Log.Error($"{uploadContext.UploadedFilename}: {visit.FieldVisitIdentifier}: {exception.ErrorCode} {exception.ErrorMessage}");
                failureAction();
            }
        }

        private static string ComposeUploadedFilename(UploadContext uploadContext, FieldVisitInfo visit)
        {
            var uploadedFilename = uploadContext.HasAttachments
                ? ComposeSafeAttachmentUploadFilename(uploadContext)
                : uploadContext.AppendedResults.AppendedVisits.Count <= 1
                    ? uploadContext.UploadedFilename
                    : $"{Path.GetFileName(uploadContext.Path)}.{visit.LocationInfo.LocationIdentifier}-{visit.StartDate.Date:yyyy-MM-dd}.json";

            return SanitizeFilename(uploadedFilename);
        }

        private static string ComposeSafeAttachmentUploadFilename(UploadContext uploadContext)
        {
            // AQTS really needs the uploaded filename to end with ".zip" if it has attachments
            return Path.GetFileNameWithoutExtension(uploadContext.Path) + ".zip";
        }

        public static string SanitizeFilename(string filename)
        {
            return InvalidFileNameCharsRegex.Replace(filename, "_");
        }

        private static readonly Regex InvalidFileNameCharsRegex = new Regex($"[{string.Join("", Path.GetInvalidFileNameChars())}]");

        private byte[] GetUploadFileBytes(AppendedResults singleResult, UploadContext uploadContext)
        {
            var jsonBytes = Encoding.UTF8.GetBytes(singleResult.ToJson());

            if (!uploadContext.HasAttachments)
                return jsonBytes;

            using (var stream = new MemoryStream())
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, true))
                {
                    AddArchiveEntry(archive, new Attachment
                    {
                        Content = jsonBytes,
                        Path = uploadContext.UploadedFilename,
                        LastWriteTime = DateTimeOffset.UtcNow,
                        ByteSize = jsonBytes.Length
                    });

                    foreach (var attachment in uploadContext.Attachments)
                    {
                        AddArchiveEntry(archive, attachment);
                    }
                }

                stream.Position = 0;

                var zipBytes = stream.GetBuffer();

                return zipBytes;
            }
        }

        private void AddArchiveEntry(ZipArchive archive, Attachment attachment)
        {
            var entry = archive.CreateEntry(attachment.Path, CompressionLevel.Fastest);

            using (var writerStream = entry.Open())
            using (var binaryWriter = new BinaryWriter(writerStream))
            {
                binaryWriter.Write(attachment.Content);
            }

            entry.LastWriteTime = attachment.LastWriteTime;
        }

        private static bool IsDuplicateFailure(WebServiceException exception)
        {
            return (exception.ErrorCode?.Equals("FieldDataFileImportFailureException", StringComparison.InvariantCultureIgnoreCase) ?? false)
                   && exception.ErrorMessage?.IndexOf("Saving parsed data would result in duplicates", StringComparison.InvariantCultureIgnoreCase) >= 0;
        }

        private static bool IsDuplicateAttachmentException(WebServiceException exception)
        {
            return exception.ErrorCode?.Equals("DuplicateImportedAttachmentException", StringComparison.InvariantCultureIgnoreCase) ?? false;
        }

        private bool ShouldSkipConflictingVisits(FieldVisitInfo visit)
        {
            var startDate = Context.OverlapIncludesWholeDay ? StartOfDay(visit.StartDate) : visit.StartDate;
            var endDate = Context.OverlapIncludesWholeDay ? EndOfDay(visit.EndDate) : visit.EndDate;

            var conflictingPeriod = CreateVisitInterval(startDate, endDate);

            var existingVisits = GetExistingVisits(visit.LocationInfo.LocationIdentifier, conflictingPeriod);

            var conflictingVisits = existingVisits
                .Where(v =>
                {
                    if (!v.StartTime.HasValue || !v.EndTime.HasValue)
                        return false;

                    var existingPeriod = CreateVisitInterval(v.StartTime.Value, v.EndTime.Value);

                    return conflictingPeriod.Intersects(existingPeriod);
                })
                .ToList();

            if (!conflictingVisits.Any())
                return false;

            switch (Context.MergeMode)
            {
                case MergeMode.Fail:
                    throw new ExpectedException($"A conflicting visit already exists on {visit.StartDate.Date:yyyy-MM-dd} at location '{visit.LocationInfo.LocationIdentifier}'");
                case MergeMode.Skip:
                    Log.Warn($"Skipping conflicting visit {visit.FieldVisitIdentifier} with {string.Join(", ", conflictingVisits.Select(v => $"{v.StartTime}/{v.EndTime}"))}");
                    return true;
                case MergeMode.Replace:
                case MergeMode.ArchiveAndReplace:
                    DeleteExistingVisits(conflictingVisits);
                    return false;
                case MergeMode.AllowSameDayVisits:
                    return false;
                default:
                    throw new ExpectedException($"{Context.MergeMode} is an unsupported {nameof(MergeMode)} value.");
            }
        }

        private static Interval CreateVisitInterval(DateTimeOffset start, DateTimeOffset end)
        {
            return new Interval(Instant.FromDateTimeOffset(start), Instant.FromDateTimeOffset(end.AddTicks(1)));
        }

        private List<FieldVisitDescription> GetExistingVisits(string locationIdentifier, Interval overlapInterval)
        {
            // TODO: Remove the extra minute of start/end slop once AQ-24998 is fixed
            return Client.Publish.Get(new FieldVisitDescriptionListServiceRequest
                {
                    LocationIdentifier = locationIdentifier,
                    QueryFrom = overlapInterval.Start.ToDateTimeOffset().AddMinutes(-1),
                    QueryTo = overlapInterval.End.ToDateTimeOffset().AddMinutes(1)
                })
                .FieldVisitDescriptions;
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

            if (Context.DryRun)
            {
                Log.Warn($"Dry-run: Would delete existing visit on {visit.StartTime:yyyy-MM-dd} at location '{visit.LocationIdentifier}'");
                return;
            }

            Log.Info($"Deleting existing visit on {visit.StartTime:yyyy-MM-dd} at location '{visit.LocationIdentifier}'");

            new VisitDeleter(Client)
                .DeleteVisit(visit);
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

            CreateTargetFolder(ArchivedFolder);

            var archiveFilenameBase = Path.Combine(ArchivedFolder, $"{visit.Identifier}_{visit.StartTime?.Date:yyyy-MM-dd}_{visit.LocationIdentifier}");
            
            Log.Info($"Archiving existing visit '{archiveFilenameBase}'.json");
            File.WriteAllText(archiveFilenameBase+".json", Transform(archivedVisit).ToJson().IndentJson());

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

        private AppendedResults Transform(ArchivedVisit archivedVisit)
        {
            var appender = new FieldDataResultsAppender
            {
                Client = Client,
                LocationCache = LocationCache,
                LocationAliases = Context.LocationAliases,
                Log = Log
            };

            var mapper = new ArchivedVisitMapper
            {
                Appender = appender,
                ReferencePointCache = ReferencePointCache,
                ParameterIdLookup = ParameterIdLookup,
                MethodLookup = MethodLookup
            };

            var visit = mapper.Map(archivedVisit);

            return new AppendedResults
            {
                FrameworkAssemblyQualifiedName = typeof(IFieldDataPlugin).AssemblyQualifiedName,
                PluginAssemblyQualifiedTypeName = mapper.GetJsonPluginAQFN(),
                AppendedVisits = new List<FieldVisitInfo>
                {
                    visit
                }
            };
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
