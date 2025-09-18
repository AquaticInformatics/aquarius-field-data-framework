using Common;
using FieldDataPluginFramework.Context;
using FieldDataPluginFramework.DataModel;
using FieldDataPluginFramework.DataModel.Calibrations;
using FieldDataPluginFramework.DataModel.ChannelMeasurements;
using FieldDataPluginFramework.DataModel.ControlConditions;
using FieldDataPluginFramework.DataModel.CrossSection;
using FieldDataPluginFramework.DataModel.DischargeActivities;
using FieldDataPluginFramework.DataModel.GageZeroFlow;
using FieldDataPluginFramework.DataModel.HydraulicTest;
using FieldDataPluginFramework.DataModel.Inspections;
using FieldDataPluginFramework.DataModel.LevelSurveys;
using FieldDataPluginFramework.DataModel.Readings;
using FieldDataPluginFramework.DataModel.WellIntegrity;
using FieldDataPluginFramework.Results;
using FieldDataPluginFramework.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using FieldDataPluginFramework.DataModel.PickLists;

namespace MultiFile
{
    // The delayed appender class exists to solely to delay the creation of visits until merges have been resolved.
    // This will allow a visit to be created, then expanded (a widened Start or End timestamp) to include more activities.
    public class DelayedAppender : IFieldDataResultsAppender
    {
        private IFieldDataResultsAppender ActualAppender { get; }

        public DelayedAppender(IFieldDataResultsAppender actualAppender)
        {
            ActualAppender = actualAppender;
        }

        public void AppendAllResults()
        {
            foreach (var delayedFieldVisit in DelayedFieldVisits)
            {
                AppendDelayedVisit(delayedFieldVisit);
            }
        }

        private void AppendDelayedVisit(FieldVisitInfo delayedVisit)
        {
            var visit = ActualAppender.AddFieldVisit(delayedVisit.LocationInfo, delayedVisit.FieldVisitDetails);

            foreach (var dischargeActivity in delayedVisit.DischargeActivities)
            {
                ActualAppender.AddDischargeActivity(visit, dischargeActivity);
            }

            foreach (var reading in delayedVisit.Readings)
            {
                ActualAppender.AddReading(visit, reading);
            }

            foreach (var calibration in delayedVisit.Calibrations)
            {
                ActualAppender.AddCalibration(visit, calibration);
            }

            foreach (var inspection in delayedVisit.Inspections)
            {
                ActualAppender.AddInspection(visit, inspection);
            }

            foreach (var crossSectionSurvey in delayedVisit.CrossSectionSurveys)
            {
                ActualAppender.AddCrossSectionSurvey(visit, crossSectionSurvey);
            }

            foreach (var levelSurvey in delayedVisit.LevelSurveys)
            {
                ActualAppender.AddLevelSurvey(visit, levelSurvey);
            }

            foreach (var controlCondition in delayedVisit.ControlConditions)
            {
                ActualAppender.AddControlCondition(visit, controlCondition);
            }

            foreach (var gageZeroFlowActivity in delayedVisit.GageZeroFlowActivities)
            {
                ActualAppender.AddGageZeroFlowActivity(visit, gageZeroFlowActivity);
            }

            foreach (var wellIntegrityActivity in delayedVisit.WellIntegrity)
            {
                ActualAppender.AddWellIntegrity(visit, wellIntegrityActivity);
            }

            foreach (var hydraulicTestActivity in delayedVisit.HydraulicTests)
            {
                ActualAppender.AddHydraulicTest(visit, hydraulicTestActivity);
            }

            foreach (var extendedAttribute in delayedVisit.ExtendedAttributes)
            {
                ActualAppender.AddExtendedAttribute(visit, extendedAttribute);
            }
        }

        public LocationInfo GetLocationByIdentifier(string locationIdentifier)
        {
            return ActualAppender.GetLocationByIdentifier(locationIdentifier);
        }

        public LocationInfo GetLocationByUniqueId(string uniqueId)
        {
            return ActualAppender.GetLocationByUniqueId(uniqueId);
        }

        public Dictionary<string, string> GetPluginConfigurations()
        {
            return FilteredConfigurations ?? GetFullPluginConfigurations();
        }

        public Dictionary<string, string> GetFullPluginConfigurations()
        {
            return ActualAppender.GetPluginConfigurations();
        }

        private Dictionary<string, string> FilteredConfigurations { get; set; }

        public void FilterConfigurationSettings(PluginLoader.LoadedPlugin plugin)
        {
            FilteredConfigurations = plugin.Settings;
        }

        private List<FieldVisitInfo> DelayedFieldVisits { get; } = new List<FieldVisitInfo>();

        public FieldVisitInfo AddFieldVisit(LocationInfo location, FieldVisitDetails fieldVisitDetails)
        {
            var existingVisit = DelayedFieldVisits
                .FirstOrDefault(visit => visit.LocationInfo.LocationIdentifier == location.LocationIdentifier &&
                    DoPeriodsOverlap(visit.FieldVisitDetails.FieldVisitPeriod, fieldVisitDetails.FieldVisitPeriod));

            if (existingVisit != null)
            {
                MergeCompletedActivities(existingVisit.FieldVisitDetails, fieldVisitDetails);

                return existingVisit;
            }

            var fieldVisitInfo = InternalConstructor<FieldVisitInfo>.Invoke(location, fieldVisitDetails);

            DelayedFieldVisits.Add(fieldVisitInfo);

            return fieldVisitInfo;
        }

        private void MergeCompletedActivities(FieldVisitDetails existingDetails, FieldVisitDetails contributingDetails)
        {
            if (existingDetails.CompletedVisitActivities == null && contributingDetails.CompletedVisitActivities != null)
            {
                existingDetails.CompletedVisitActivities = contributingDetails.CompletedVisitActivities;
                return;
            }

            var existing = existingDetails.CompletedVisitActivities;
            var contributing = contributingDetails.CompletedVisitActivities;

            if (existing == null || contributing == null)
                return;

            existing.GroundWaterLevels |= contributing.GroundWaterLevels;
            existing.ConductedLevelSurvey |= contributing.ConductedLevelSurvey;
            existing.RecorderDataCollected |= contributing.RecorderDataCollected;
            existing.SafetyInspectionPerformed |= contributing.SafetyInspectionPerformed;
            existing.OtherSample |= contributing.OtherSample;
            existing.SedimentSample |= contributing.SedimentSample;
            existing.WaterQualitySample |= contributing.WaterQualitySample;
            existing.WaterQualityCrossSection |= contributing.WaterQualityCrossSection;
        }

        private static bool DoPeriodsOverlap(DateTimeInterval earlierPeriod, DateTimeInterval laterPeriod)
        {
            if (laterPeriod.Start < earlierPeriod.Start)
            {
                // Ensure earlierPeriod precedes laterPeriod, for simpler comparision
                var tempPeriod = earlierPeriod;
                earlierPeriod = laterPeriod;
                laterPeriod = tempPeriod;
            }

            var earlierEnd = EndOfDay(earlierPeriod.End);

            var laterStart = StartOfDay(laterPeriod.Start);

            if (earlierEnd < laterStart)
                return false;

            if (earlierEnd > laterStart)
                return true;

            return false;
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

        public void AddDischargeActivity(FieldVisitInfo fieldVisit, DischargeActivity dischargeActivity)
        {
            fieldVisit.DischargeActivities.Add(dischargeActivity);

            ExtendVisitPeriod(fieldVisit,
                new[] { dischargeActivity.MeasurementPeriod.Start, dischargeActivity.MeasurementPeriod.End }
                    .Concat(dischargeActivity.GageHeightMeasurements.Select(ghm => ghm.MeasurementTime ?? DateTimeOffset.MinValue))
                    .Concat(dischargeActivity.ChannelMeasurements.SelectMany(ChannelMeasurementTimes)));
        }

        private IEnumerable<DateTimeOffset> ChannelMeasurementTimes(ChannelMeasurementBase channelMeasurement)
        {
            yield return channelMeasurement.MeasurementPeriod.Start;
            yield return channelMeasurement.MeasurementPeriod.End;

            if (!(channelMeasurement is ManualGaugingDischargeSection manualGauging)) yield break;

            foreach (var time in ManualGaugingTimes(manualGauging))
            {
                yield return time;
            }
        }

        private IEnumerable<DateTimeOffset> ManualGaugingTimes(ManualGaugingDischargeSection manualGauging)
        {
            return manualGauging
                .Verticals
                .Select(v => v.MeasurementTime ?? DateTimeOffset.MinValue);
        }

        public void AddReading(FieldVisitInfo fieldVisit, Reading reading)
        {
            fieldVisit.Readings.Add(reading);

            ExtendVisitPeriod(fieldVisit, reading.DateTimeOffset);
        }

        public void AddCalibration(FieldVisitInfo fieldVisit, Calibration calibration)
        {
            fieldVisit.Calibrations.Add(calibration);

            ExtendVisitPeriod(fieldVisit, calibration.DateTimeOffset);
        }

        public void AddInspection(FieldVisitInfo fieldVisit, Inspection inspection)
        {
            fieldVisit.Inspections.Add(inspection);

            ExtendVisitPeriod(fieldVisit, inspection.DateTimeOffset);
        }

        public void AddGageZeroFlowActivity(FieldVisitInfo fieldVisit, GageZeroFlowActivity gageZeroFlowActivity)
        {
            fieldVisit.GageZeroFlowActivities.Add(gageZeroFlowActivity);

            ExtendVisitPeriod(fieldVisit, gageZeroFlowActivity.ObservationDate);
        }

        public void AddCrossSectionSurvey(FieldVisitInfo fieldVisit, CrossSectionSurvey crossSectionSurvey)
        {
            fieldVisit.CrossSectionSurveys.Add(crossSectionSurvey);

            ExtendVisitPeriod(fieldVisit,
                new[] { crossSectionSurvey.SurveyPeriod.Start, crossSectionSurvey.SurveyPeriod.End });
        }

        public void AddLevelSurvey(FieldVisitInfo fieldVisit, LevelSurvey levelSurvey)
        {
            fieldVisit.LevelSurveys.Add(levelSurvey);

            ExtendVisitPeriod(fieldVisit,
                new[] { levelSurvey.SurveyPeriod.Start, levelSurvey.SurveyPeriod.End }
                    .Concat(levelSurvey.LevelSurveyMeasurements.Select(lsm => lsm.MeasurementTime)));
        }

        public void AddControlCondition(FieldVisitInfo fieldVisit, ControlCondition controlCondition)
        {
            fieldVisit.ControlConditions.Add(controlCondition);

            ExtendVisitPeriod(fieldVisit, controlCondition.DateCleaned);
        }

        private void ExtendVisitPeriod(FieldVisitInfo fieldVisit, DateTimeOffset? dateTimeOffset)
        {
            if (dateTimeOffset.HasValue)
            {
                ExtendVisitPeriod(fieldVisit, new[] { dateTimeOffset.Value });
            }
        }

        private void ExtendVisitPeriod(FieldVisitInfo fieldVisit, IEnumerable<DateTimeOffset> times)
        {
            var sortedTimes = times
                .Where(t => t != DateTimeOffset.MinValue)
                .OrderBy(t => t)
                .ToList();

            if (!sortedTimes.Any())
                return;

            fieldVisit.FieldVisitDetails.FieldVisitPeriod = ExpandInterval(
                fieldVisit.FieldVisitDetails.FieldVisitPeriod,
                sortedTimes.First(),
                sortedTimes.Last());
        }

        private static DateTimeInterval ExpandInterval(DateTimeInterval interval, DateTimeOffset startTime, DateTimeOffset endTime)
        {
            var minStart = interval.Start < startTime
                ? interval.Start
                : startTime;

            var maxEnd = interval.End > endTime
                ? interval.End
                : endTime;

            return new DateTimeInterval(minStart, maxEnd);
        }

        public void AddWellIntegrity(FieldVisitInfo fieldVisit, WellIntegrity wellIntegrity)
        {
            fieldVisit.WellIntegrity.Add(wellIntegrity);
            var allActivityPeriods = wellIntegrity.WellRedevelopments.Select(s => s.StartDate)
                .Concat(wellIntegrity.WellRepairs.Select(s => s.StartDate))
                .Concat(wellIntegrity.WellInspections.Select(s => s.StartDate))
                .Concat(wellIntegrity.WellAquiferConnections.Select(s => s.StartDate))
                .Concat(wellIntegrity.WellRepairs.Select(s => s.EndDate))
                .Concat(wellIntegrity.WellRedevelopments.Select(s => s.EndDate))
                .Concat(wellIntegrity.WellRepairs.Select(s => s.EndDate));
            ExtendVisitPeriod(fieldVisit, allActivityPeriods);
        }

        public void AddHydraulicTest(FieldVisitInfo fieldVisit, HydraulicTest hydraulicTest)
        {
            fieldVisit.HydraulicTests.Add(hydraulicTest);
            ExtendVisitPeriod(fieldVisit, new[] { hydraulicTest.StartDate, hydraulicTest.EndDate });
        }

        public void AddExtendedAttribute(FieldVisitInfo fieldVisit, ExtendedAttributePickList extendedAttribute)
        {
            fieldVisit.ExtendedAttributes.Add(extendedAttribute);
        }
    }
}
