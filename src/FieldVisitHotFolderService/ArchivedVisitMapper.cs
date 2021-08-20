using System;
using System.Collections.Generic;
using System.Linq;
using Aquarius.TimeSeries.Client.ServiceModels.Publish;
using FieldDataPluginFramework.Context;
using FieldDataPluginFramework.DataModel;
using FieldDataPluginFramework.DataModel.ControlConditions;
using FieldDataPluginFramework.DataModel.CrossSection;
using FieldDataPluginFramework.DataModel.DischargeActivities;
using FieldDataPluginFramework.DataModel.GageZeroFlow;
using FieldDataPluginFramework.DataModel.LevelSurveys;
using FieldDataPluginFramework.DataModel.PickLists;
using FieldDataPluginFramework.DataModel.Readings;
using AdjustmentType = Aquarius.TimeSeries.Client.ServiceModels.Publish.AdjustmentType;
using Calibration = FieldDataPluginFramework.DataModel.Calibrations.Calibration;
using CrossSectionPoint = Aquarius.TimeSeries.Client.ServiceModels.Publish.CrossSectionPoint;
using DischargeActivity = FieldDataPluginFramework.DataModel.DischargeActivities.DischargeActivity;
using Grade = FieldDataPluginFramework.DataModel.DischargeActivities.Grade;
using LevelSurveyMeasurement = FieldDataPluginFramework.DataModel.LevelSurveys.LevelSurveyMeasurement;
using QualitativeUncertaintyType = Aquarius.TimeSeries.Client.ServiceModels.Publish.QualitativeUncertaintyType;
using Reading = Aquarius.TimeSeries.Client.ServiceModels.Publish.Reading;
using ReadingType = Aquarius.TimeSeries.Client.ServiceModels.Publish.ReadingType;
using ReasonForAdjustmentType = Aquarius.TimeSeries.Client.ServiceModels.Publish.ReasonForAdjustmentType;
using UncertaintyType = Aquarius.TimeSeries.Client.ServiceModels.Publish.UncertaintyType;

namespace FieldVisitHotFolderService
{
    public class ArchivedVisitMapper
    {
        public FieldDataResultsAppender Appender { get; set; }
        public ReferencePointCache ReferencePointCache { get; set; }
        public Dictionary<string,string> ParameterIdsByIdentifier { get; set; }
        public Dictionary<string, Dictionary<string, string>> MethodLookup { get; set; }

        private string VisitIdentifier { get; set; }
        private LocationInfo LocationInfo { get; set; }

        public FieldVisitInfo Map(ArchivedVisit archivedVisit)
        {
            VisitIdentifier = $"{archivedVisit.Summary.StartTime:O}@{archivedVisit.Summary.LocationIdentifier}";

            LocationInfo = Appender.GetLocationByIdentifier(archivedVisit.Summary.LocationIdentifier);
            var visit = Appender.AddFieldVisit(LocationInfo, Map(archivedVisit.Summary));

            MapActivities(visit, archivedVisit.Activities);

            return visit;
        }

        private FieldVisitDetails Map(FieldVisitDescription visit)
        {
            return new FieldVisitDetails(new DateTimeInterval(
                visit.StartTime ?? throw new InvalidOperationException($"{VisitIdentifier}: Unknown visit start"),
                visit.EndTime ?? throw new InvalidOperationException($"{VisitIdentifier}: Unknown visit end")))
            {
                CollectionAgency = visit.CompletedWork.CollectionAgency,
                Comments = visit.Remarks,
                Party = visit.Party,
                Weather = visit.Weather,
                CompletedVisitActivities = new CompletedVisitActivities
                {
                    BiologicalSample = visit.CompletedWork.BiologicalSampleTaken,
                    ConductedLevelSurvey = visit.CompletedWork.LevelsPerformed,
                    GroundWaterLevels = visit.CompletedWork.GroundWaterLevelPerformed,
                    OtherSample = visit.CompletedWork.OtherSampleTaken,
                    RecorderDataCollected = visit.CompletedWork.RecorderDataCollected,
                    SafetyInspectionPerformed = visit.CompletedWork.SafetyInspectionPerformed,
                    SedimentSample = visit.CompletedWork.SedimentSampleTaken,
                    WaterQualitySample = visit.CompletedWork.WaterQualitySampleTaken
                }
            };
        }

        private void MapActivities(FieldVisitInfo visit, FieldVisitDataServiceResponse activities)
        {
            if (activities.ControlConditionActivity != null)
            {
                visit.ControlConditions.Add(Map(activities.ControlConditionActivity));
            }

            if (activities.GageHeightAtZeroFlowActivity != null)
            {
                visit.GageZeroFlowActivities.Add(Map(activities.GageHeightAtZeroFlowActivity));
            }

            if (activities.LevelSurveyActivity != null)
            {
                visit.LevelSurveys.Add(Map(activities.LevelSurveyActivity));
            }

            foreach (var activity in activities.InspectionActivity.Inspections ?? new List<Inspection>())
            {
                visit.Inspections.Add(Map(activity));
            }

            foreach (var activity in activities.InspectionActivity.Readings ?? new List<Reading>())
            {
                visit.Readings.Add(Map(activity));
            }

            foreach (var activity in activities.InspectionActivity.CalibrationChecks ?? new List<CalibrationCheck>())
            {
                visit.Calibrations.Add(Map(activity));
            }

            foreach (var activity in activities.CrossSectionSurveyActivity)
            {
                visit.CrossSectionSurveys.Add(Map(activity));
            }

            foreach (var activity in activities.DischargeActivities)
            {
                visit.DischargeActivities.Add(Map(activity));
            }
        }

        private ControlCondition Map(ControlConditionActivity source)
        {
            return new ControlCondition
            {
                Comments = source.Comments,
                Party = source.Party,
                ConditionType = new ControlConditionPickList(source.ControlCondition),
                ControlCleaned = Map<Aquarius.TimeSeries.Client.ServiceModels.Publish.ControlCleanedType, FieldDataPluginFramework.DataModel.ControlConditions.ControlCleanedType>(source.ControlCleaned),
                DateCleaned = source.DateCleaned,
                DistanceToGage = Map(nameof(source.DistanceToGage), source.DistanceToGage),
                ControlCode = new ControlCodePickList(source.ControlCode)
            };
        }

        private GageZeroFlowActivity Map(GageHeightAtZeroFlowActivity source)
        {
            if (!source.ObservedDate.HasValue)
                throw new ArgumentNullException($"{VisitIdentifier}: No observation date for GageZeroFlow");

            return new GageZeroFlowActivity(source.ObservedDate.Value,
                Map(nameof(source.ZeroFlowHeight), source.Unit, source.ZeroFlowHeight))
            {
                ApplicableSinceDate = source.ApplicableSince,
                Party = source.Party,
                Comments = source.Comments,
                Stage = source.CalculatedDetails?.Stage?.Numeric,
                WaterDepth = source.CalculatedDetails?.Depth?.Numeric,
                Certainty = source.CalculatedDetails?.DepthCertainty?.Numeric
            };
        }

        private LevelSurvey Map(LevelSurveyActivity source)
        {
            var originReferencePoint = GetReferencePoint(source.OriginReferencePointUniqueId);

            return new LevelSurvey(originReferencePoint.Name)
            {
                Party = source.Party,
                Comments = source.Comments,
                Method = source.Method,
                LevelSurveyMeasurements = source
                    .LevelMeasurements
                    .Select(Map)
                    .ToList()
            };
        }

        private ReferencePoint GetReferencePoint(Guid uniqueId)
        {
            var referencePoint = ReferencePointCache.Find(LocationInfo.LocationIdentifier, uniqueId);

            if (referencePoint == null)
                throw new ArgumentOutOfRangeException($"{VisitIdentifier}: '{uniqueId:N}' is an unknown reference point unique Id");

            return referencePoint;
        }

        private LevelSurveyMeasurement Map(Aquarius.TimeSeries.Client.ServiceModels.Publish.LevelSurveyMeasurement source)
        {
            var referencePoint = GetReferencePoint(source.ReferencePointUniqueId);

            var measuredElevation = source.MeasuredElevation?.Numeric ??
                                    throw new InvalidOperationException($"{VisitIdentifier}: No measured elevation for level survey measurement at {source.MeasurementTime:O}");

            return new LevelSurveyMeasurement(referencePoint.Name, source.MeasurementTime, measuredElevation)
            {
                Comments = source.Comments
            };
        }

        private FieldDataPluginFramework.DataModel.Inspections.Inspection Map(Inspection source)
        {
            var inspectionType = Map<InspectionType, FieldDataPluginFramework.DataModel.Inspections.InspectionType>(source.InspectionType);

            return new FieldDataPluginFramework.DataModel.Inspections.Inspection(inspectionType)
            {
                Comments = source.Comments,
                DateTimeOffset = source.Time,
                SubLocation = source.SubLocationIdentifier,
                MeasurementDevice = Map(source.Manufacturer, source.Model, source.SerialNumber)
            };
        }

        private MeasurementDevice Map(string manufacturer, string model, string serialNumber)
        {
            if (string.IsNullOrWhiteSpace(manufacturer) && string.IsNullOrWhiteSpace(model) && string.IsNullOrWhiteSpace(serialNumber))
                return null;

            return new MeasurementDevice(manufacturer, model, serialNumber);
        }

        private FieldDataPluginFramework.DataModel.Readings.Reading Map(Reading source)
        {
            var parameterId = LookupParameterId(source.Parameter);
            var readingType = Map<ReadingType, FieldDataPluginFramework.DataModel.Readings.ReadingType>(source.ReadingType);

            return new FieldDataPluginFramework.DataModel.Readings.Reading(parameterId, source.Unit, source.Value.Numeric)
            {
                Comments = source.Comments,
                DateTimeOffset = source.Time,
                Grade = Map(source.GradeCode),
                Publish = source.Publish,
                Method = LookupParameterMethod(parameterId, source.MonitoringMethod),
                MeasurementDevice = Map(source.Manufacturer, source.Model, source.SerialNumber),
                SensorUniqueId = source.SensorUniqueId,
                SubLocation = source.SubLocationIdentifier,
                Uncertainty = source.Uncertainty?.Numeric,
                ReadingType = readingType,
                UseLocationDatumAsReference = source.UseLocationDatumAsReference,
                ReferencePointName = source.ReferencePointUniqueId.HasValue
                    ? GetReferencePoint(source.ReferencePointUniqueId.Value).Name
                    : null,
                ReadingQualifiers = source.ReadingQualifiers.Select(rq => new ReadingQualifierPickList(rq)).ToList(),
                GroundWaterMeasurementDetails = Map(source.GroundWaterMeasurement)
            };
        }

        private string LookupParameterId(string parameterIdentifier)
        {
            if (ParameterIdsByIdentifier.TryGetValue(parameterIdentifier, out var parameterId))
                return parameterId;

            throw new InvalidOperationException($"{VisitIdentifier}: '{parameterIdentifier}' is an unknown parameter identifier.");
        }

        private string LookupParameterMethod(string parameterId, string methodName)
        {
            if (MethodLookup.TryGetValue(parameterId, out var parameterMethods) &&
                parameterMethods.TryGetValue(methodName, out var methodCode))
                return methodCode;

            if (parameterMethods == null)
            {
                const string defaultMethodName = "None";
                const string defaultMethodCode = "DefaultNone";

                if (methodName.Equals(defaultMethodName))
                    return defaultMethodCode;
            }

            var expectedNames = parameterMethods != null
                ? parameterMethods.Keys.ToList()
                : new List<string>();

            throw new InvalidOperationException($"{VisitIdentifier}: '{methodName}' is not a known method name for parameter '{parameterId}'. Should be one of {string.Join(", ", expectedNames)}");
        }

        private Grade Map(int? gradeCode)
        {
            if (!gradeCode.HasValue)
                return null;

            return Grade.FromCode(gradeCode.Value);
        }

        private GroundWaterMeasurementDetails Map(GroundWaterMeasurement source)
        {
            if (source == null)
                return null;

            return new GroundWaterMeasurementDetails
            {
                Cut = source.Cut?.Numeric,
                Hold = source.Hold?.Numeric,
                TapeCorrection = source.TapeCorrection?.Numeric,
                WaterLevel = source.WaterLevel?.Numeric
            };
        }

        private Calibration Map(CalibrationCheck source)
        {
            var parameterId = LookupParameterId(source.Parameter);

            if (!source.Value.Numeric.HasValue)
                throw new InvalidOperationException($"{VisitIdentifier}: CalibrationCheck for '{parameterId}' has no value");

            var calibrationType = Map<CalibrationCheckType, FieldDataPluginFramework.DataModel.Calibrations.CalibrationType>(source.CalibrationCheckType);

            return new Calibration(parameterId, source.Unit, source.Value.Numeric.Value)
            {
                Comments = source.Comments,
                DateTimeOffset = source.Time,
                CalibrationType = calibrationType,
                Method = LookupParameterMethod(parameterId, source.MonitoringMethod),
                Publish = source.Publish,
                Standard = source.Standard?.Numeric,
                SubLocation = source.SubLocationIdentifier,
                SensorUniqueId = source.SensorUniqueId,
                MeasurementDevice = Map(source.Manufacturer, source.Model, source.SerialNumber),
                StandardDetails = Map(source.StandardDetails)
            };
        }

        private FieldDataPluginFramework.DataModel.Calibrations.StandardDetails Map(StandardDetails source)
        {
            if (source == null)
                return null;

            return new FieldDataPluginFramework.DataModel.Calibrations.StandardDetails(source.LotNumber,
                source.StandardCode, source.ExpirationDate, source.Temperature?.Numeric);
        }

        private CrossSectionSurvey Map(CrossSectionSurveyActivity source)
        {
            if (!SupportedStartPointTypes.TryGetValue(source.StartingPoint, out var startPointType))
                throw new InvalidOperationException($"{VisitIdentifier}: {source.StartingPoint} is not a supported starting point type");

            return new CrossSectionSurvey(
                new DateTimeInterval(source.StartTime, source.EndTime),
                source.Channel,
                source.RelativeLocation,
                source.Stage.Unit,
                startPointType)
            {
                Comments = source.Comments,
                Party = source.Party,
                ChannelName = source.Channel,
                RelativeLocationName = source.RelativeLocation,
                StageMeasurement = Map(nameof(source.Stage), source.Stage),
                CrossSectionPoints = source
                    .CrossSectionPoints
                    .Select(Map)
                    .ToList()
            };
    }

        private FieldDataPluginFramework.DataModel.CrossSection.CrossSectionPoint Map(CrossSectionPoint source)
        {
            if (!source.Distance.Numeric.HasValue)
                throw new InvalidOperationException($"{VisitIdentifier}: Cross-section point {source.PointOrder} has no Distance value");

            if (!source.Elevation.Numeric.HasValue)
                throw new InvalidOperationException($"{VisitIdentifier}: Cross-section point {source.PointOrder} has no Elevation value");

            return new FieldDataPluginFramework.DataModel.CrossSection.CrossSectionPoint(source.PointOrder,
                source.Distance.Numeric.Value, source.Elevation.Numeric.Value)
            {
                Comments = source.Comments,
                Depth = source.Depth.Numeric
            };
        }

        private static readonly
            Dictionary<StartPointType, FieldDataPluginFramework.DataModel.ChannelMeasurements.StartPointType>
            SupportedStartPointTypes
                = new Dictionary<StartPointType, FieldDataPluginFramework.DataModel.ChannelMeasurements.StartPointType>
                {
                    {
                        StartPointType.LeftEdgeOfWater,
                        FieldDataPluginFramework.DataModel.ChannelMeasurements.StartPointType.LeftEdgeOfWater
                    },
                    {
                        StartPointType.RightEdgeOfWater,
                        FieldDataPluginFramework.DataModel.ChannelMeasurements.StartPointType.RightEdgeOfWater
                    },
                };

        private DischargeActivity Map(Aquarius.TimeSeries.Client.ServiceModels.Publish.DischargeActivity source)
        {
            var summary = source.DischargeSummary;

            if (!summary.MeasurementStartTime.HasValue)
                throw new InvalidOperationException($"{VisitIdentifier}: No discharge start time");

            if (!summary.MeasurementEndTime.HasValue)
                throw new InvalidOperationException($"{VisitIdentifier}: No discharge end time");

            var adjustment = summary.Adjustment;

            var dischargeActivity = new DischargeActivity(
                new DateTimeInterval(summary.MeasurementStartTime.Value, summary.MeasurementEndTime.Value),
                Map(nameof(summary.Discharge), summary.Discharge))
            {
                Comments = summary.Comments,
                Party = summary.Party,
                MeasurementTime = summary.MeasurementTime,
                ShowInDataCorrection = summary.Publish,
                ShowInRatingDevelopment = summary.Publish,
                PreventAutomaticPublishing = !summary.Publish,
                MeasurementId = summary.MeasurementId,
                MeasurementGrade = Map(summary.GradeCode),
                QualityAssuranceComments = summary.QualityAssuranceComments,
                MeanIndexVelocity = Map(nameof(summary.MeanIndexVelocity), summary.MeanIndexVelocity),
                AdjustmentType = Map<AdjustmentType, FieldDataPluginFramework.DataModel.DischargeActivities.AdjustmentType>(adjustment.AdjustmentType),
                AdjustmentAmount = adjustment.AdjustmentAmount,
                ReasonForAdjustment = Map<ReasonForAdjustmentType, FieldDataPluginFramework.DataModel.DischargeActivities.ReasonForAdjustmentType>(adjustment.ReasonForAdjustment),
            };

            var dischargeUncertainty = summary.DischargeUncertainty;

            switch (dischargeUncertainty.ActiveUncertaintyType)
            {
                case UncertaintyType.Qualitative:
                    dischargeActivity.ActiveUncertaintyType = FieldDataPluginFramework.DataModel.DischargeActivities.UncertaintyType.Qualitative;
                    dischargeActivity.QualitativeUncertainty = Map<QualitativeUncertaintyType, FieldDataPluginFramework.DataModel.DischargeActivities.QualitativeUncertaintyType>(dischargeUncertainty.QualitativeUncertainty);
                    break;
                case UncertaintyType.Quantitative:
                    dischargeActivity.ActiveUncertaintyType = FieldDataPluginFramework.DataModel.DischargeActivities.UncertaintyType.Quantitative;
                    dischargeActivity.QuantitativeUncertainty = dischargeUncertainty.QuantitativeUncertainty.Numeric;
                    break;
            }


//        public double? MeanGageHeightDurationHours { get; set; }
//        public Measurement MeanGageHeightDifferenceDuringVisit { get; set; }
//        public Measurement ManuallyCalculatedMeanGageHeight { get; set; }
//        public ICollection<GageHeightMeasurement> GageHeightMeasurements { get; }
//        public ICollection<ChannelMeasurementBase> ChannelMeasurements { get; }

            switch (source.DischargeSummary.GageHeightCalculation)
            {
                case GageHeightCalculationType.ManuallyCalculated:
                    dischargeActivity.ManuallyCalculatedMeanGageHeight = Map(nameof(summary.MeanGageHeight), summary.MeanGageHeight);
                    break;
                case GageHeightCalculationType.SimpleAverage:
                    foreach (var reading in summary.GageHeightReadings)
                    {
                        dischargeActivity.GageHeightMeasurements.Add(Map(reading, summary.MeanGageHeight.Unit));
                    }
                    break;
            }

            
            return dischargeActivity;
        }

        private GageHeightMeasurement Map(GageHeightReading source, string stageUnitId)
        {
            return new GageHeightMeasurement(
                Map(nameof(source.GageHeight), stageUnitId, source.GageHeight),
                source.ReadingTime ?? throw new InvalidOperationException($"{VisitIdentifier}: Gage height reading has no time"),
                source.IsUsed);
        }

        private Measurement Map(string name, QuantityWithDisplay source)
        {
            if (source == null)
                return null;

            if (!source.Numeric.HasValue)
                throw new InvalidOperationException($"{VisitIdentifier}: {name} has no value Display={source.Display} Unit={source.Unit}");

            return new Measurement(source.Numeric.Value, source.Unit);
        }

        private Measurement Map(string name, string unitId, DoubleWithDisplay source)
        {
            if (source == null)
                return null;

            if (!source.Numeric.HasValue)
                throw new InvalidOperationException($"{VisitIdentifier}: {name} has no value Display={source.Display} Unit={unitId}");

            return new Measurement(source.Numeric.Value, unitId);
        }

        private TTargetEnum Map<TSourceEnum, TTargetEnum>(TSourceEnum source)
            where TSourceEnum :struct
            where TTargetEnum :struct
        {
            if (Enum.TryParse<TTargetEnum>($"{source}", true, out var targetEnum))
                return targetEnum;

            throw new ArgumentOutOfRangeException(nameof(source), $"{VisitIdentifier}: '{source}' is an invalid {typeof(TTargetEnum).Name} value. Must be one of {string.Join(", ", Enum.GetNames(typeof(TSourceEnum)))}");
        }
    }
}
