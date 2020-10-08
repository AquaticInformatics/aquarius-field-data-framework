using System;
using System.Collections.Generic;
using System.Linq;
using FieldDataPluginFramework;
using FieldDataPluginFramework.Context;
using FieldDataPluginFramework.DataModel;
using FieldDataPluginFramework.DataModel.Calibrations;
using FieldDataPluginFramework.DataModel.ControlConditions;
using FieldDataPluginFramework.DataModel.CrossSection;
using FieldDataPluginFramework.DataModel.DischargeActivities;
using FieldDataPluginFramework.DataModel.GageZeroFlow;
using FieldDataPluginFramework.DataModel.Inspections;
using FieldDataPluginFramework.DataModel.LevelSurveys;
using FieldDataPluginFramework.DataModel.Readings;
using FieldDataPluginFramework.Results;
using FieldDataPluginFramework.Serialization;

namespace PluginTester
{
    public class FieldDataResultsAppender : IFieldDataResultsAppender
    {
        public LocationInfo CreateDummyLocationInfoByIdentifier(string locationIdentifier)
        {
            return CreateDummyLocationInfo(locationIdentifier, $"DummyNameFor-{locationIdentifier}", Guid.Empty);
        }

        private LocationInfo CreateDummyLocationInfoByUniqueId(Guid uniqueId)
        {
            return CreateDummyLocationInfo($"DummyIdentifierFor-{uniqueId:N}", $"DummyNameFor-{uniqueId:N}", uniqueId);
        }

        private LocationInfo CreateDummyLocationInfo(string identifier, string name, Guid uniqueId)
        {
            const long dummyLocationId = 0;

            var locationInfo = InternalConstructor<LocationInfo>.Invoke(
                name,
                identifier,
                dummyLocationId,
                uniqueId,
                UtcOffset.TotalHours);

            if (KnownLocations.Any(l => l.LocationIdentifier == identifier))
                throw new ArgumentException($"Can't add duplicate location for Identifier='{identifier}'");

            KnownLocations.Add(locationInfo);

            return locationInfo;
        }

        private static readonly List<LocationInfo> KnownLocations = new List<LocationInfo>();

        public LocationInfo ForcedLocationInfo { get; set; }
        public TimeSpan UtcOffset { get; set; }
        public Dictionary<string,string> Settings { get; set; }

        public AppendedResults AppendedResults { get; } = new AppendedResults
        {
            FrameworkAssemblyQualifiedName = typeof(IFieldDataPlugin).AssemblyQualifiedName
        };

        public LocationInfo GetLocationByIdentifier(string locationIdentifier)
        {
            if (ForcedLocationInfo != null)
            {
                if (ForcedLocationInfo.LocationIdentifier == locationIdentifier)
                    return ForcedLocationInfo;

                throw new ArgumentException($"Location {locationIdentifier} does not exist");
            }

            var locationInfo = KnownLocations.SingleOrDefault(l => l.LocationIdentifier == locationIdentifier);

            return locationInfo ?? CreateDummyLocationInfoByIdentifier(locationIdentifier);
        }

        public LocationInfo GetLocationByUniqueId(string uniqueIdText)
        {
            if (!Guid.TryParse(uniqueIdText, out var uniqueId))
                throw new ArgumentException($"Can't parse '{uniqueIdText}' as a unique ID");

            var locationInfo = KnownLocations.SingleOrDefault(l => Guid.Parse(l.UniqueId) == uniqueId);

            return locationInfo ?? CreateDummyLocationInfoByUniqueId(uniqueId);
        }

        public Dictionary<string, string> GetPluginConfigurations()
        {
            return Settings;
        }

        public FieldVisitInfo AddFieldVisit(LocationInfo location, FieldVisitDetails fieldVisitDetails)
        {
            var fieldVisitInfo = InternalConstructor<FieldVisitInfo>.Invoke(location, fieldVisitDetails);

            AppendedResults.AppendedVisits.Add(fieldVisitInfo);

            return fieldVisitInfo;
        }

        public void AddDischargeActivity(FieldVisitInfo fieldVisit, DischargeActivity dischargeActivity)
        {
            fieldVisit.DischargeActivities.Add(dischargeActivity);
        }

        public void AddControlCondition(FieldVisitInfo fieldVisit, ControlCondition controlCondition)
        {
            fieldVisit.ControlConditions.Add(controlCondition);
        }

        public void AddCrossSectionSurvey(FieldVisitInfo fieldVisit, CrossSectionSurvey crossSectionSurvey)
        {
            fieldVisit.CrossSectionSurveys.Add(crossSectionSurvey);
        }

        public void AddReading(FieldVisitInfo fieldVisit, Reading reading)
        {
            fieldVisit.Readings.Add(reading);
        }

        public void AddCalibration(FieldVisitInfo fieldVisit, Calibration calibration)
        {
            fieldVisit.Calibrations.Add(calibration);
        }

        public void AddInspection(FieldVisitInfo fieldVisit, Inspection inspection)
        {
            fieldVisit.Inspections.Add(inspection);
        }

        public void AddGageZeroFlowActivity(FieldVisitInfo fieldVisit, GageZeroFlowActivity gageZeroFlowActivity)
        {
            fieldVisit.GageZeroFlowActivities.Add(gageZeroFlowActivity);
        }

        public void AddLevelSurvey(FieldVisitInfo fieldVisit, LevelSurvey levelSurvey)
        {
            fieldVisit.LevelSurveys.Add(levelSurvey);
        }
    }
}
