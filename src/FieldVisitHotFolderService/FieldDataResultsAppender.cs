using System;
using System.Collections.Generic;
using System.Linq;
using Aquarius.TimeSeries.Client;
using Aquarius.TimeSeries.Client.ServiceModels.Provisioning;
using Aquarius.TimeSeries.Client.ServiceModels.Publish;
using FieldDataPluginFramework;
using FieldDataPluginFramework.Context;
using FieldDataPluginFramework.DataModel;
using FieldDataPluginFramework.DataModel.ControlConditions;
using FieldDataPluginFramework.DataModel.CrossSection;
using FieldDataPluginFramework.DataModel.LevelSurveys;
using FieldDataPluginFramework.Results;
using FieldDataPluginFramework.Serialization;
using ServiceStack;
using DischargeActivity = FieldDataPluginFramework.DataModel.DischargeActivities.DischargeActivity;
using Reading = FieldDataPluginFramework.DataModel.Readings.Reading;

namespace FieldVisitHotFolderService
{
    public class FieldDataResultsAppender : IFieldDataResultsAppender
    {
        public IAquariusClient Client { get; set; }
        public List<LocationInfo> LocationCache { get; set; }

        public AppendedResults AppendedResults { get; } = new AppendedResults
        {
            FrameworkAssemblyQualifiedName = typeof(IFieldDataPlugin).AssemblyQualifiedName
        };

        public LocationInfo GetLocationByIdentifier(string locationIdentifier)
        {
            var locationInfo = LocationCache.SingleOrDefault(l => l.LocationIdentifier == locationIdentifier);

            if (locationInfo != null)
                return locationInfo;

            var locationDescriptions = Client.Publish.Get(new LocationDescriptionListServiceRequest
                    {LocationIdentifier = locationIdentifier})
                .LocationDescriptions;

            if (locationDescriptions.Count != 1)
            {
                throw new ArgumentException($"Location {locationIdentifier} does not exist");
            }

            var location = Client.Provisioning.Get(new GetLocation {LocationUniqueId = locationDescriptions[0].UniqueId});

            return AddLocationInfo(location);
        }

        private LocationInfo AddLocationInfo(Location location)
        {
            const long dummyId = 0;
            var locationInfo =
                InternalConstructor<LocationInfo>.Invoke(
                    location.LocationName,
                    location.Identifier,
                    dummyId,
                    location.UniqueId,
                    location.UtcOffset.ToTimeSpan().TotalHours);

            LocationCache.Add(locationInfo);

            return locationInfo;
        }

        public LocationInfo GetLocationByUniqueId(string uniqueIdText)
        {
            if (!Guid.TryParse(uniqueIdText, out var uniqueId))
                throw new ArgumentException($"Can't parse '{uniqueIdText}' as a unique ID");

            var locationInfo = LocationCache.SingleOrDefault(l => Guid.Parse(l.UniqueId) == uniqueId);

            if (locationInfo != null)
                return locationInfo;

            try
            {
                var location = Client.Provisioning.Get(new GetLocation {LocationUniqueId = uniqueId});

                return AddLocationInfo(location);
            }
            catch (WebServiceException)
            {
                throw new ArgumentException($"LocationUniqueId {uniqueId:N} does not exist");
            }
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

        public void AddLevelSurvey(FieldVisitInfo fieldVisit, LevelSurvey levelSurvey)
        {
            fieldVisit.LevelSurveys.Add(levelSurvey);
        }
    }
}
