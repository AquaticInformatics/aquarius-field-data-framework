﻿using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
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
        public Dictionary<string, Dictionary<string, string>> MethodLookup { get; set; }
        public Dictionary<string, string> ParameterIdLookup { get; set; }

        private ArchivedVisitMapper Mapper { get; set; }
        private int VisitCount { get; set; }

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

            Log.Info($"Exported {"visit".ToQuantity(VisitCount)} in {stopwatch.Elapsed.Humanize(2)}");
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
            var visitDescriptions = Client.Publish.Get(new FieldVisitDescriptionListServiceRequest
                {
                    LocationIdentifier = locationIdentifier,
                    QueryFrom = Context.ExportAfter,
                    QueryTo = Context.ExportBefore
                })
                .FieldVisitDescriptions;

            Log.Info($"Exporting {"visit".ToQuantity(visitDescriptions.Count)} from '{locationIdentifier}' ...");

            var locationPath = Path.Combine(Context.ExportFolder, locationIdentifier);
            Directory.CreateDirectory(locationPath);

            foreach (var visitDescription in visitDescriptions)
            {
                ExportVisit(locationPath, visitDescription);
            }
        }

        private void ExportVisit(string locationPath, FieldVisitDescription fieldVisitDescription)
        {
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

            var visitPath = Path.Combine(locationPath, $"{fieldVisitDescription.LocationIdentifier}@{fieldVisitDescription.StartTime:yyyy-MM-DD_HH_MM}.json");

            Log.Info($"Saving '{visitPath}' ...");

            File.WriteAllText(visitPath, Transform(archivedVisit).ToJson().IndentJson());

            ++VisitCount;
        }

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