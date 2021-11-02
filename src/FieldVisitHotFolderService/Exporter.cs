using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Aquarius.TimeSeries.Client;
using Aquarius.TimeSeries.Client.ServiceModels.Publish;
using Common;
using FieldDataPluginFramework;
using FieldDataPluginFramework.Context;
using FieldDataPluginFramework.Serialization;
using Humanizer;
using log4net;
using ServiceStack;
using ServiceStack.Text;
using Attachment = Aquarius.TimeSeries.Client.ServiceModels.Publish.Attachment;
using ILog = log4net.ILog;

namespace FieldVisitHotFolderService
{
    public class Exporter
    {
        private static readonly ILog Log4NetLog = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public Context Context { get; set; }
        private FileLogger Log { get; } = new FileLogger(Log4NetLog);
        public List<IFieldDataPlugin> Plugins { get; set; }
        public IAquariusClient Client { get; set; }
        private List<LocationInfo> LocationCache { get; } = new List<LocationInfo>();
        public ReferencePointCache ReferencePointCache { get; set; }
        public MethodLookup MethodLookup { get; set; }
        public ParameterIdLookup ParameterIdLookup { get; set; }

        private ArchivedVisitMapper Mapper { get; set; }
        private int VisitCount { get; set; }
        private int ErrorCount { get; set; }
        private int MissingAttachmentCount { get; set; }
        private int SkipCount { get; set; }

        public void Run()
        {
            Validate();

            var locationIdentifiers = GetLocationsToExport()
                .OrderBy(s => s)
                .ToList();

            var stopwatch = Stopwatch.StartNew();

            var summary = new StringBuilder("Exporting");

            if (!Context.ExportBefore.HasValue && !Context.ExportAfter.HasValue)
            {
                summary.Append(" all visits");
            }
            else
            {
                if (Context.ExportBefore.HasValue)
                    summary.Append($" before {Context.ExportBefore:O}");

                if (Context.ExportAfter.HasValue)
                {
                    if (Context.ExportBefore.HasValue)
                        summary.Append(" and");

                    summary.Append($" after {Context.ExportAfter:O}");
                }
            }

            if (!Context.ExportLocations.Any())
            {
                summary.Append($" from {"location".ToQuantity(locationIdentifiers.Count)} ...");
            }

            Log.Info(summary.ToString());

            foreach (var locationIdentifier in locationIdentifiers)
            {
                ExportVisitsFromLocation(locationIdentifier);
            }

            if (MissingAttachmentCount > 0)
                Log.Warn($"{"missing attachments".ToQuantity(MissingAttachmentCount)} were detected. Check your app server's BLOB storage configuration.");

            Log.Info($"Exported {"visit".ToQuantity(VisitCount)}, skipping {"visit".ToQuantity(SkipCount)}, with {"error".ToQuantity(ErrorCount)} detected in {stopwatch.Elapsed.Humanize(2)}");
        }

        private void Validate()
        {
            if (!Directory.Exists(Context.ExportFolder))
                throw new ExpectedException($"Export folder '{Context.ExportFolder}' does not exist.");

            var appender = new FieldDataResultsAppender
            {
                Client = Client,
                LocationCache = LocationCache,
                LocationAliases = Context.LocationAliases,
                Log = Log
            };

            Mapper = new ArchivedVisitMapper
            {
                Appender = appender,
                Plugins = Plugins,
                ReferencePointCache = ReferencePointCache,
                ParameterIdLookup = ParameterIdLookup,
                MethodLookup = MethodLookup
            };
        }

        private IEnumerable<string> GetLocationsToExport()
        {
            return Context.ExportLocations.Any()
                ? Context.ExportLocations
                : Client.Publish.Get(new LocationDescriptionListServiceRequest())
                    .LocationDescriptions
                    .Select(ld => ld.Identifier);
        }

        private void ExportVisitsFromLocation(string locationIdentifier)
        {
            var visitDescriptions = GetVisitsToExport(locationIdentifier);

            Log.Info($"Exporting {"visit".ToQuantity(visitDescriptions.Count)} from '{locationIdentifier}' ...");

            var locationPath = Path.Combine(Context.ExportFolder, FileProcessor.SanitizeFilename(locationIdentifier));
            Directory.CreateDirectory(locationPath);

            foreach (var visitDescription in visitDescriptions)
            {
                ExportVisit(locationPath, visitDescription);
            }
        }

        private List<FieldVisitDescription> GetVisitsToExport(string locationIdentifier)
        {
            try
            {
                return Client.Publish.Get(new FieldVisitDescriptionListServiceRequest
                    {
                        LocationIdentifier = locationIdentifier,
                        QueryFrom = Context.ExportAfter,
                        QueryTo = Context.ExportBefore
                    })
                    .FieldVisitDescriptions
                    .OrderBy(v => v.StartTime)
                    .ToList();
            }
            catch (WebServiceException exception)
            {
                if (exception.ErrorCode != "PermissionException")
                    throw;

                Log.Warn($"Skipping export of location '{locationIdentifier}': {exception.ErrorCode} {exception.ErrorMessage}");

                ++ErrorCount;

                return new List<FieldVisitDescription>();
            }
        }

        private void ExportVisit(string locationPath, FieldVisitDescription fieldVisitDescription)
        {
            var visitPath = Path.Combine(locationPath, FileProcessor.SanitizeFilename($"{fieldVisitDescription.LocationIdentifier}@{fieldVisitDescription.StartTime:yyyy-MM-dd_HH_MM}.json"));
            var zipPath = Path.ChangeExtension(visitPath, ".zip");

            var targetPath = File.Exists(zipPath)
                ? zipPath
                : visitPath;

            if (!Context.ExportOverwrite && File.Exists(targetPath))
            {
                Log.Info($"Skipping existing '{targetPath}'");
                ++SkipCount;
                return;
            }

            var archivedVisit = new ArchivedVisit
            {
                Summary = fieldVisitDescription,
                Activities = Client.Publish.Get(new FieldVisitDataServiceRequest
                {
                    FieldVisitIdentifier = fieldVisitDescription.Identifier,
                    IncludeNodeDetails = true,
                    IncludeInvalidActivities = true,
                    IncludeCrossSectionSurveyProfile = true,
                    IncludeVerticals = true
                })
            };

            try
            {
                ExportVisit(visitPath, zipPath, archivedVisit);

                ++VisitCount;
            }
            catch (Exception exception)
            {
                ++ErrorCount;

                var errorPath = Path.ChangeExtension(visitPath, ".error.json");

                File.WriteAllText(errorPath, archivedVisit.ToJson().IndentJson());

                Log.Error(exception is ExpectedException
                    ? $"'{visitPath}': {exception.Message}"
                    : $"'{visitPath}': {exception.Message}\n{exception.StackTrace}");
            }
        }

        private void ExportVisit(string visitPath, string zipPath, ArchivedVisit archivedVisit)
        {
            File.Delete(visitPath);
            File.Delete(zipPath);

            if (!archivedVisit.Activities.Attachments.Any())
            {
                Log.Info($"Saving '{visitPath}' ...");

                File.WriteAllText(visitPath, TransformToJson(archivedVisit));
                return;
            }

            Log.Info($"Saving '{zipPath}' ...");

            var deleteZipOnCleanup = true;

            try
            {
                ExportVisitAndAttachments(visitPath, zipPath, archivedVisit);

                deleteZipOnCleanup = false;
            }
            finally
            {
                if (deleteZipOnCleanup)
                    File.Delete(zipPath);
            }
        }

        private void ExportVisitAndAttachments(string visitPath, string zipPath, ArchivedVisit archivedVisit)
        {
            using (var stream = File.OpenWrite(zipPath))
            using (var zipArchive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var rootJsonEntry = zipArchive.CreateEntry(Path.GetFileName(visitPath), CompressionLevel.Fastest);

                using (var writer = new StreamWriter(rootJsonEntry.Open()))
                {
                    writer.Write(TransformToJson(archivedVisit));
                }

                var attachmentCount = 0;
                foreach (var attachment in archivedVisit.Activities.Attachments)
                {
                    ++attachmentCount;
                    var attachmentEntry =
                        zipArchive.CreateEntry($"Attachment{attachmentCount}/{Path.GetFileName(attachment.FileName)}");

                    var contentBytes = DownloadAttachmentContent(attachmentEntry.FullName, attachment);

                    using (var writer = new BinaryWriter(attachmentEntry.Open()))
                    {
                        writer.Write(contentBytes);
                    }
                }
            }
        }

        private byte[] DownloadAttachmentContent(string downloadPath, Attachment attachment)
        {
            Log.Info($"Downloading {downloadPath} ...");

            try
            {
                using (var httpResponse = Client.Publish.Get<HttpWebResponse>(attachment.Url))
                {
                    return httpResponse.GetResponseStream().ReadFully();
                }
            }
            catch (WebServiceException exception)
            {
                if (!exception.IsNotFound())
                    throw;

                ++MissingAttachmentCount;

                throw new ExpectedException($"{exception.StatusCode}: {exception.StatusDescription}: {exception.ErrorMessage}");
            }
        }

        private string TransformToJson(ArchivedVisit archivedVisit)
        {
            var json = Transform(archivedVisit).ToJson();

            if (Context.ExportUtcOverride.HasValue)
            {
                var adjustedUtcOffset = FormatUtcOffset(Context.ExportUtcOverride.Value);

                json = DateTimeOffsetRegex.Replace(json, match => $"\"{match.Groups["datetime"].Value}{adjustedUtcOffset}\"");
            }

            return json.IndentJson();
        }

        private static string FormatUtcOffset(TimeSpan timeSpan)
        {
            return timeSpan >= TimeSpan.Zero
                ? $"+{timeSpan:hh\\:mm}"
                : $"-{timeSpan:hh\\:mm}";
        }

        // "2016-02-05T10:30:00.0000000-08:00"
        private static Regex DateTimeOffsetRegex = new Regex(@"""(?<datetime>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?)(?<utcOffset>[-+]\d{2}:\d{2})""");

        private AppendedResults Transform(ArchivedVisit archivedVisit)
        {
            return new AppendedResults
            {
                FrameworkAssemblyQualifiedName = typeof(IFieldDataPlugin).AssemblyQualifiedName,
                PluginAssemblyQualifiedTypeName = Mapper.GetJsonPluginAQFN(),
                AppendedVisits = new List<FieldVisitInfo>
                {
                    Mapper.Map(archivedVisit)
                }
            };
        }
    }
}
