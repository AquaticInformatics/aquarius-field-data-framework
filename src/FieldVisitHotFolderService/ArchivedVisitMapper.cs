using System;
using System.Collections.Generic;
using System.Linq;
using Aquarius.TimeSeries.Client.ServiceModels.Publish;
using FieldDataPluginFramework;
using FieldDataPluginFramework.Context;
using FieldDataPluginFramework.DataModel;
using FieldDataPluginFramework.DataModel.ChannelMeasurements;
using FieldDataPluginFramework.DataModel.ControlConditions;
using FieldDataPluginFramework.DataModel.CrossSection;
using FieldDataPluginFramework.DataModel.DischargeActivities;
using FieldDataPluginFramework.DataModel.GageZeroFlow;
using FieldDataPluginFramework.DataModel.LevelSurveys;
using FieldDataPluginFramework.DataModel.Meters;
using FieldDataPluginFramework.DataModel.PickLists;
using FieldDataPluginFramework.DataModel.Readings;
using FieldDataPluginFramework.DataModel.Verticals;
using FieldDataPluginFramework.Units;
using CrossSectionPoint = Aquarius.TimeSeries.Client.ServiceModels.Publish.CrossSectionPoint;
using IceCoveredData = Aquarius.TimeSeries.Client.ServiceModels.Publish.IceCoveredData;
using OpenWaterData = Aquarius.TimeSeries.Client.ServiceModels.Publish.OpenWaterData;
using Reading = Aquarius.TimeSeries.Client.ServiceModels.Publish.Reading;
using StartPointType = Aquarius.TimeSeries.Client.ServiceModels.Publish.StartPointType;
using UncertaintyType = Aquarius.TimeSeries.Client.ServiceModels.Publish.UncertaintyType;
using VelocityDepthObservation = Aquarius.TimeSeries.Client.ServiceModels.Publish.VelocityDepthObservation;
using Vertical = Aquarius.TimeSeries.Client.ServiceModels.Publish.Vertical;
using VolumetricDischargeReading = Aquarius.TimeSeries.Client.ServiceModels.Publish.VolumetricDischargeReading;
using FrameworkCalibration = FieldDataPluginFramework.DataModel.Calibrations.Calibration;
using FrameworkDischargeActivity = FieldDataPluginFramework.DataModel.DischargeActivities.DischargeActivity;
using FrameworkGrade = FieldDataPluginFramework.DataModel.DischargeActivities.Grade;
using FrameworkLevelSurveyMeasurement = FieldDataPluginFramework.DataModel.LevelSurveys.LevelSurveyMeasurement;
using FrameworkPointVelocityObservationType = FieldDataPluginFramework.DataModel.Verticals.PointVelocityObservationType;
using FrameworkVelocityObservation = FieldDataPluginFramework.DataModel.Verticals.VelocityObservation;
using FrameworkVelocityDepthObservation = FieldDataPluginFramework.DataModel.Verticals.VelocityDepthObservation;
using FrameworkVolumetricDischargeReading = FieldDataPluginFramework.DataModel.ChannelMeasurements.VolumetricDischargeReading;
using FrameworkDeploymentMethodType = FieldDataPluginFramework.DataModel.ChannelMeasurements.DeploymentMethodType;
using FrameworkMeterType = FieldDataPluginFramework.DataModel.Meters.MeterType;
using FrameworkControlCleanedType = FieldDataPluginFramework.DataModel.ControlConditions.ControlCleanedType;
using FrameworkInspectionType = FieldDataPluginFramework.DataModel.Inspections.InspectionType;
using FrameworkInspection = FieldDataPluginFramework.DataModel.Inspections.Inspection;
using FrameworkReading = FieldDataPluginFramework.DataModel.Readings.Reading;
using FrameworkCalibrationType = FieldDataPluginFramework.DataModel.Calibrations.CalibrationType;
using FrameworkStandardDetails = FieldDataPluginFramework.DataModel.Calibrations.StandardDetails;
using FrameworkReadingType = FieldDataPluginFramework.DataModel.Readings.ReadingType;
using FrameworkStartPointType = FieldDataPluginFramework.DataModel.ChannelMeasurements.StartPointType;
using FrameworkCrossSectionPoint = FieldDataPluginFramework.DataModel.CrossSection.CrossSectionPoint;
using FrameworkAdjustmentType = FieldDataPluginFramework.DataModel.DischargeActivities.AdjustmentType;
using FrameworkReasonForAdjustmentType = FieldDataPluginFramework.DataModel.DischargeActivities.ReasonForAdjustmentType;
using FrameworkUncertaintyType = FieldDataPluginFramework.DataModel.DischargeActivities.UncertaintyType;
using FrameworkQualitativeUncertaintyType = FieldDataPluginFramework.DataModel.DischargeActivities.QualitativeUncertaintyType;
using FrameworkDischargeMethodType = FieldDataPluginFramework.DataModel.ChannelMeasurements.DischargeMethodType;
using FrameworkMeterSuspensionType = FieldDataPluginFramework.DataModel.ChannelMeasurements.MeterSuspensionType;
using FrameworkVertical = FieldDataPluginFramework.DataModel.Verticals.Vertical;
using FrameworkVerticalType = FieldDataPluginFramework.DataModel.Verticals.VerticalType;
using FrameworkFlowDirectionType = FieldDataPluginFramework.DataModel.Verticals.FlowDirectionType;
using FrameworkOpenWaterData = FieldDataPluginFramework.DataModel.Verticals.OpenWaterData;
using FrameworkIceCoveredData = FieldDataPluginFramework.DataModel.Verticals.IceCoveredData;

namespace FieldVisitHotFolderService
{
    public class ArchivedVisitMapper
    {
        public FieldDataResultsAppender Appender { get; set; }
        public ReferencePointCache ReferencePointCache { get; set; }
        public ParameterIdLookup ParameterIdLookup { get; set; }
        public MethodLookup MethodLookup { get; set; }
        public List<IFieldDataPlugin> Plugins { get; set; }

        private string VisitIdentifier { get; set; }
        private LocationInfo LocationInfo { get; set; }

        private string _jsonPluginTypeName;

        // ReSharper disable once InconsistentNaming
        public string GetJsonPluginAQFN()
        {
            if (_jsonPluginTypeName == null)
            {
                _jsonPluginTypeName = Plugins
                    // ReSharper disable once PossibleNullReferenceException
                    .First(p => p.GetType().AssemblyQualifiedName.Contains("JsonFieldData"))
                    .GetType().AssemblyQualifiedName;
            }

            return _jsonPluginTypeName;
        }

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
            var controlCondition = new ControlCondition
            {
                Comments = source.Comments,
                Party = source.Party,
                DateCleaned = source.DateCleaned,
                DistanceToGage = Map(nameof(source.DistanceToGage), source.DistanceToGage),
            };

            if (!string.IsNullOrEmpty(source.ControlCondition))
            {
                controlCondition.ConditionType = new ControlConditionPickList(source.ControlCondition);
            }

            if (!string.IsNullOrEmpty(source.ControlCode))
            {
                controlCondition.ControlCode = new ControlCodePickList(source.ControlCode);
            }

            if (TryParseEnum<FrameworkControlCleanedType>($"{source.ControlCleaned}", out var controlCleaned))
            {
                controlCondition.ControlCleaned = controlCleaned;
            }

            return controlCondition;
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

        private FrameworkLevelSurveyMeasurement Map(Aquarius.TimeSeries.Client.ServiceModels.Publish.LevelSurveyMeasurement source)
        {
            var referencePoint = GetReferencePoint(source.ReferencePointUniqueId);

            var measuredElevation = source.MeasuredElevation?.Numeric ??
                                    throw new InvalidOperationException($"{VisitIdentifier}: No measured elevation for level survey measurement at {source.MeasurementTime:O}");

            return new FrameworkLevelSurveyMeasurement(referencePoint.Name, source.MeasurementTime, measuredElevation)
            {
                Comments = source.Comments
            };
        }

        private FrameworkInspection Map(Inspection source)
        {
            if (!TryParseEnum<FrameworkInspectionType>($"{source.InspectionType}", out var inspectionType))
                throw new InvalidOperationException($"'{source.InspectionType}' is not a supported inspection type.");

            return new FrameworkInspection(inspectionType)
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

        private FrameworkReading Map(Reading source)
        {
            var parameterId = source.ParameterId ?? LookupParameterId(source.Parameter);

            var reading = new FrameworkReading(parameterId, source.Unit, source.Value?.Numeric)
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
                UseLocationDatumAsReference = source.UseLocationDatumAsReference,
                ReferencePointName = source.ReferencePointUniqueId.HasValue
                    ? GetReferencePoint(source.ReferencePointUniqueId.Value).Name
                    : null,
                ReadingQualifiers = source.ReadingQualifiers.Select(rq => new ReadingQualifierPickList(rq)).ToList(),
                GroundWaterMeasurementDetails = Map(source.GroundWaterMeasurement)
            };

            if (TryParseEnum<FrameworkReadingType>($"{source.ReadingType}", out var readingType))
            {
                reading.ReadingType = readingType;
            }

            return reading;
        }

        private string LookupParameterId(string parameterIdentifier)
        {
            if (ParameterIdLookup.TryGetValue(parameterIdentifier, out var parameterId))
                return parameterId;

            throw new InvalidOperationException($"{VisitIdentifier}: '{parameterIdentifier}' is an unknown parameter identifier.");
        }

        private string LookupParameterMethod(string parameterId, string methodName)
        {
            if (MethodLookup.TryGetValue(parameterId, methodName, out var methodCode))
                return methodCode;

            if (MethodLookup.IsAmbiguous(parameterId, methodName, out var ambiguousMethodCodes))
                throw new InvalidOperationException($"{VisitIdentifier}: '{methodName}' is an ambiguous method name for parameter '{parameterId}' using these method codes: {string.Join(", ", ambiguousMethodCodes)}");

            throw new InvalidOperationException($"{VisitIdentifier}: '{methodName}' is not a known method name for parameter '{parameterId}'.");
        }

        private FrameworkGrade Map(int? gradeCode)
        {
            if (!gradeCode.HasValue)
                return null;

            return FrameworkGrade.FromCode(gradeCode.Value);
        }

        private GroundWaterMeasurementDetails Map(GroundWaterMeasurement source)
        {
            var anyValue = source?.Cut?.Numeric
                           ?? source?.Hold?.Numeric
                           ?? source?.TapeCorrection?.Numeric
                           ?? source?.WaterLevel?.Numeric;

            if (!anyValue.HasValue)
                return null;

            return new GroundWaterMeasurementDetails
            {
                Cut = source.Cut?.Numeric,
                Hold = source.Hold?.Numeric,
                TapeCorrection = source.TapeCorrection?.Numeric,
                WaterLevel = source.WaterLevel?.Numeric
            };
        }

        private FrameworkCalibration Map(CalibrationCheck source)
        {
            var parameterId = source.ParameterId ?? LookupParameterId(source.Parameter);

            if (!source.Value.Numeric.HasValue)
                throw new InvalidOperationException($"{VisitIdentifier}: CalibrationCheck for '{parameterId}' has no value");

            var calibration = new FrameworkCalibration(parameterId, source.Unit, source.Value.Numeric.Value)
            {
                Comments = source.Comments,
                DateTimeOffset = source.Time,
                Method = LookupParameterMethod(parameterId, source.MonitoringMethod),
                Publish = source.Publish,
                Standard = source.Standard?.Numeric,
                SubLocation = source.SubLocationIdentifier,
                SensorUniqueId = source.SensorUniqueId,
                MeasurementDevice = Map(source.Manufacturer, source.Model, source.SerialNumber),
                StandardDetails = Map(source.StandardDetails)
            };

            if (TryParseEnum<FrameworkCalibrationType>($"{source.CalibrationCheckType}", out var calibrationType))
            {
                calibration.CalibrationType = calibrationType;
            }

            return calibration;
        }

        private FrameworkStandardDetails Map(StandardDetails source)
        {
            if (source == null)
                return null;

            return new FrameworkStandardDetails(source.LotNumber, source.StandardCode, source.ExpirationDate, source.Temperature?.Numeric);
        }

        private CrossSectionSurvey Map(CrossSectionSurveyActivity source)
        {
            return new CrossSectionSurvey(
                new DateTimeInterval(source.StartTime, source.EndTime),
                source.Channel,
                source.RelativeLocation,
                source.Stage.Unit,
                Map(source.StartingPoint))
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

        private FrameworkStartPointType Map(StartPointType source)
        {
            if (SupportedStartPointTypes.TryGetValue(source, out var startPointType))
                return startPointType;

            throw new InvalidOperationException($"{VisitIdentifier}: {source} is not a supported starting point type");
        }

        private FrameworkCrossSectionPoint Map(CrossSectionPoint source)
        {
            if (!source.Distance.Numeric.HasValue)
                throw new InvalidOperationException($"{VisitIdentifier}: Cross-section point {source.PointOrder} has no Distance value");

            if (!source.Elevation.Numeric.HasValue)
                throw new InvalidOperationException($"{VisitIdentifier}: Cross-section point {source.PointOrder} has no Elevation value");

            return new FrameworkCrossSectionPoint(source.PointOrder,
                source.Distance.Numeric.Value, source.Elevation.Numeric.Value)
            {
                Comments = source.Comments,
                Depth = source.Depth.Numeric
            };
        }

        private static readonly Dictionary<StartPointType, FrameworkStartPointType> SupportedStartPointTypes
            = new Dictionary<StartPointType, FrameworkStartPointType>
            {
                { StartPointType.LeftEdgeOfWater, FrameworkStartPointType.LeftEdgeOfWater },
                { StartPointType.RightEdgeOfWater, FrameworkStartPointType.RightEdgeOfWater },
            };

        private FrameworkDischargeActivity DischargeActivity { get; set; }

        private FrameworkDischargeActivity Map(Aquarius.TimeSeries.Client.ServiceModels.Publish.DischargeActivity source)
        {
            var summary = source.DischargeSummary;

            if (!summary.MeasurementStartTime.HasValue)
                throw new InvalidOperationException($"{VisitIdentifier}: No discharge start time");

            if (!summary.MeasurementEndTime.HasValue)
                throw new InvalidOperationException($"{VisitIdentifier}: No discharge end time");

            var adjustment = summary.Adjustment;
            var gageHeightUnit = summary.MeanGageHeight.Unit;

            var dischargeActivity = new FrameworkDischargeActivity(
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
                AdjustmentAmount = adjustment.AdjustmentAmount,
                MeanGageHeightDifferenceDuringVisit = Map(nameof(summary.DifferenceDuringVisit), gageHeightUnit, summary.DifferenceDuringVisit),
                MeanGageHeightDurationHours = summary.DurationInHours.Numeric,
            };

            if (TryParseEnum<FrameworkAdjustmentType>($"{adjustment.AdjustmentType}", out var adjustmentType))
            {
                dischargeActivity.AdjustmentType = adjustmentType;
            }

            if (TryParseEnum<FrameworkReasonForAdjustmentType>($"{adjustment.ReasonForAdjustment}", out var reasonForAdjustment))
            {
                dischargeActivity.ReasonForAdjustment = reasonForAdjustment;
            }

            var dischargeUncertainty = summary.DischargeUncertainty;

            switch (dischargeUncertainty.ActiveUncertaintyType)
            {
                case UncertaintyType.Qualitative:
                    dischargeActivity.ActiveUncertaintyType = FrameworkUncertaintyType.Qualitative;

                    if (TryParseEnum<FrameworkQualitativeUncertaintyType>($"{dischargeUncertainty.QualitativeUncertainty}", out var qualitativeUncertainty))
                    {
                        dischargeActivity.QualitativeUncertainty = qualitativeUncertainty;
                    }
                    break;

                case UncertaintyType.Quantitative:
                    dischargeActivity.ActiveUncertaintyType = FrameworkUncertaintyType.Quantitative;
                    dischargeActivity.QuantitativeUncertainty = dischargeUncertainty.QuantitativeUncertainty.Numeric;
                    break;
            }

            switch (source.DischargeSummary.GageHeightCalculation)
            {
                case GageHeightCalculationType.ManuallyCalculated:
                    dischargeActivity.ManuallyCalculatedMeanGageHeight = Map(nameof(summary.MeanGageHeight), summary.MeanGageHeight);
                    break;

                case GageHeightCalculationType.SimpleAverage:
                    foreach (var reading in summary.GageHeightReadings)
                    {
                        dischargeActivity.GageHeightMeasurements.Add(Map(reading, gageHeightUnit));
                    }
                    break;
            }

            DischargeActivity = dischargeActivity;

            foreach (var channelMeasurement in MapChannelMeasurements(source))
            {
                dischargeActivity.ChannelMeasurements.Add(channelMeasurement);
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

        private IEnumerable<ChannelMeasurementBase> MapChannelMeasurements(Aquarius.TimeSeries.Client.ServiceModels.Publish.DischargeActivity source)
        {
            foreach (var channel in source.PointVelocityDischargeActivities)
            {
                yield return Map(channel);
            }

            foreach (var channel in source.AdcpDischargeActivities)
            {
                yield return Map(channel);
            }

            foreach (var channel in source.VolumetricDischargeActivities)
            {
                yield return Map(channel);
            }

            foreach (var channel in source.EngineeredStructureDischargeActivities)
            {
                yield return Map(channel);
            }

            foreach (var channel in source.OtherMethodDischargeActivities)
            {
                yield return Map(channel);
            }
        }

        private ChannelMeasurementBase Map(PointVelocityDischargeActivity source)
        {
            var sourceChannel = source.DischargeChannelMeasurement;
            var measurementPeriod = GetMeasurementPeriod(sourceChannel);

            var unitSystem = CreateUnitSystem(sourceChannel, () => source.Area?.Unit, () => source.VelocityAverage?.Unit);

            var channel = new ManualGaugingDischargeSection(
                measurementPeriod,
                sourceChannel.Channel,
                Map(nameof(sourceChannel.Discharge), sourceChannel.Discharge),
                unitSystem.DistanceUnitId,
                unitSystem.AreaUnitId,
                unitSystem.VelocityUnitId)
            {
                WidthValue = source.Width?.Numeric,
                AreaValue = source.Area?.Numeric,
                VelocityAverageValue = source.VelocityAverage?.Numeric,
                NumberOfVerticals = source.NumberOfPanels,
            };

            if (SupportedStartPointTypes.TryGetValue(source.StartPoint, out var startPointType))
            {
                channel.StartPoint = startPointType;
            }

            if (TryParseEnum<FrameworkPointVelocityObservationType>(source.VelocityObservationMethod, out var velocityObservationMethod))
            {
                channel.VelocityObservationMethod = velocityObservationMethod;
            }

            if (TryParseEnum<FrameworkDischargeMethodType>($"{source.DischargeMethod}", out var dischargeMethod))
            {
                channel.DischargeMethod = dischargeMethod;
            }

            if (TryParseEnum<FrameworkMeterSuspensionType>($"{sourceChannel.MeterSuspension}", out var meterSuspension))
            {
                channel.MeterSuspension = meterSuspension;
            }

            if (TryParseEnum<FrameworkDeploymentMethodType>($"{sourceChannel.DeploymentMethod}", out var deploymentMethod))
            {
                channel.DeploymentMethod = deploymentMethod;
            }

            SetCommonChannelProperties(channel, sourceChannel);

            foreach (var vertical in source.Verticals ?? new List<Vertical>())
            {
                channel.Verticals.Add(Map(vertical, sourceChannel));
            }

            channel.MeterCalibration = channel
                .Verticals
                .Select(v => v.VelocityObservation.MeterCalibration)
                .FirstOrDefault(); // TODO: Find most common meter

            return channel;
        }

        private UnitSystem CreateUnitSystem(DischargeChannelMeasurement source, Func<string> areaFunc = null, Func<string> velocityFunc = null)
        {
            var dischargeUnitId = source.Discharge?.Unit ?? "m^3/s";

            var inferredDistanceUnitId = dischargeUnitId.Replace("^3/s", "");
            var inferredAreaUnitId = $"{inferredDistanceUnitId}^2";
            var inferredVelocityUnitId = $"{inferredDistanceUnitId}/s";

            var distanceUnitId = source.DistanceToGage?.Unit ?? inferredDistanceUnitId;
            var areaUnitId = areaFunc?.Invoke() ?? inferredAreaUnitId;
            var velocityUnitId = velocityFunc?.Invoke() ?? inferredVelocityUnitId;

            return new UnitSystem
            {
                AreaUnitId = areaUnitId,
                DischargeUnitId = dischargeUnitId,
                DistanceUnitId = distanceUnitId,
                VelocityUnitId = velocityUnitId,
            };
        }

        private DateTimeInterval GetMeasurementPeriod(DischargeChannelMeasurement source)
        {
            return new DateTimeInterval(
                source.StartTime ?? DischargeActivity.MeasurementStartTime,
                source.EndTime ?? DischargeActivity.MeasurementEndTime);
        }

        private void SetCommonChannelProperties(ChannelMeasurementBase channel, DischargeChannelMeasurement source)
        {
            channel.Party = source.Party;
            channel.Comments = source.Comments;
        }

        private FrameworkVertical Map(Vertical source, DischargeChannelMeasurement sourceChannel)
        {
            var vertical = new FrameworkVertical
            {
                Comments = source.Comments,
                SequenceNumber = (int)source.VerticalNumber,
                TaglinePosition = source.TaglinePosition?.Numeric ?? 0,
                SoundedDepth = source.SoundedDepth?.Numeric ?? 0,
                EffectiveDepth = source.EffectiveDepth?.Numeric ?? 0,
                IsSoundedDepthEstimated = source.IsSoundedDepthEstimated,
                ObliqueFlowCorrection = source.CosineOfUniqueFlow,
                MeasurementTime = source.MeasurementTime,
                MeasurementConditionData = source.OpenWaterData != null ? Map(source.OpenWaterData) : Map(source.IceCoveredData),
                Segment = MapSegment(source),
                VelocityObservation = MapVelocity(source, sourceChannel)
            };

            if (TryParseEnum<FrameworkVerticalType>($"{source.VerticalType}", out var verticalType))
            {
                vertical.VerticalType = verticalType;
            }

            if (TryParseEnum<FrameworkFlowDirectionType>($"{source.FlowDirection}", out var flowDirection))
            {
                vertical.FlowDirection = flowDirection;
            }

            return vertical;
        }

        private MeasurementConditionData Map(OpenWaterData source)
        {
            return new FrameworkOpenWaterData
            {
                DistanceToMeter = source.DistanceToMeter?.Numeric,
                DistanceToWaterSurface = source.DistanceToWaterSurface?.Numeric,
                DryLineAngle = source.DryLineAngle,
                DryLineCorrection = source.DryLineCorrection,
                SurfaceCoefficient = source.SurfaceCoefficient,
                SuspensionWeight = source.SuspensionWeight,
                WetLineCorrection = source.WetLineCorrection
            };
        }

        private MeasurementConditionData Map(IceCoveredData source)
        {
            return new FrameworkIceCoveredData
            {
                AboveFooting = source.AboveFooting?.Numeric,
                BelowFooting = source.BelowFooting?.Numeric,
                IceAssemblyType = source.IceAssemblyType,
                IceThickness = source.IceThickness?.Numeric,
                UnderIceCoefficient = source.UnderIceCoefficient,
                WaterSurfaceToBottomOfIce = source.WaterSurfaceToBottomOfIce?.Numeric ?? 0,
                WaterSurfaceToBottomOfSlush = source.WaterSurfaceToBottomOfSlush?.Numeric ?? 0
            };
        }

        private Segment MapSegment(Vertical source)
        {
            var anySegmentValue = source.SegmentArea?.Numeric ?? source.SegmentDischarge?.Numeric ??
                source.SegmentVelocity?.Numeric ?? source.SegmentWidth?.Numeric;

            if (!anySegmentValue.HasValue)
                return null;

            return new Segment
            {
                Area = source.SegmentArea?.Numeric ?? 0,
                Discharge = source.SegmentDischarge?.Numeric ?? 0,
                Width = source.SegmentWidth?.Numeric ?? 0,
                Velocity = source.SegmentVelocity?.Numeric ?? 0,
                IsDischargeEstimated = source.IsDischargeEstimated,
                TotalDischargePortion = source.PercentageOfTotalDischarge
            };
        }

        private FrameworkVelocityObservation MapVelocity(Vertical source, DischargeChannelMeasurement sourceChannel)
        {
            var velocityObservation = new FrameworkVelocityObservation
            {
                MeanVelocity = source.MeanVelocity?.Numeric ?? 0,
                MeterCalibration = MapMeter(source, sourceChannel)
            };

            if (TryParseEnum<FrameworkDeploymentMethodType>($"{source.VelocityObservation.DeploymentMethod}", out var deploymentMethod))
            {
                velocityObservation.DeploymentMethod = deploymentMethod;
            }

            if (TryParseEnum<FrameworkPointVelocityObservationType>($"{source.VelocityObservationMethod}", out var velocityObservationMethod))
            {
                velocityObservation.VelocityObservationMethod = velocityObservationMethod;
            }

            foreach (var depthObservation in source.VelocityObservation?.Observations ?? new List<VelocityDepthObservation>())
            {
                velocityObservation.Observations.Add(Map(depthObservation));
            }

            return velocityObservation;
        }

        private MeterCalibration MapMeter(Vertical source, DischargeChannelMeasurement sourceChannel)
        {
            TryParseEnum<FrameworkMeterType>($"{sourceChannel.CurrentMeter}", out var meterType);

            var meterCalibration = new MeterCalibration
            {
                Manufacturer = source.CurrentMeter.Manufacturer,
                Model = source.CurrentMeter.Model,
                SerialNumber = source.CurrentMeter.SerialNumber,
                // TODO: No Publish API property SoftwareVersion = ,
                // TODO: No Publish API property FirmwareVersion = ,
                // TODO: No Publish API property Configuration = ,
                MeterType = meterType
            };

            foreach (var calibration in source.Calibrations ?? new List<Calibration>())
            {
                meterCalibration.Equations.Add(Map(calibration));
            }

            return meterCalibration;
        }

        private MeterCalibrationEquation Map(Calibration source)
        {
            return new MeterCalibrationEquation
            {
                RangeStart = source.RangeStart,
                RangeEnd = source.RangeEnd,
                Intercept = source.Intercept,
                Slope = source.Slope,
                InterceptUnitId = source.InterceptUnit
            };
        }

        private FrameworkVelocityDepthObservation Map(VelocityDepthObservation source)
        {
            return new FrameworkVelocityDepthObservation
            {
                Depth = source.Depth?.Numeric ?? 0,
                Velocity = source.Velocity?.Numeric ?? 0,
                RevolutionCount = source.RevolutionCount,
                ObservationInterval = source.ObservationIntervalInSeconds?.Numeric,
                IsVelocityEstimated = source.IsVelocityEstimated
            };
        }

        private ChannelMeasurementBase Map(AdcpDischargeActivity source)
        {
            var sourceChannel = source.DischargeChannelMeasurement;
            var measurementPeriod = GetMeasurementPeriod(sourceChannel);

            var unitSystem = CreateUnitSystem(sourceChannel, () => source.Area?.Unit, () => source.VelocityAverage?.Unit);

            var channel = new AdcpDischargeSection(
                measurementPeriod,
                sourceChannel.Channel,
                Map(nameof(sourceChannel.Discharge), sourceChannel.Discharge),
                source.AdcpDeviceType,
                unitSystem.DistanceUnitId,
                unitSystem.AreaUnitId,
                unitSystem.VelocityUnitId)
            {
                SoftwareVersion = source.SoftwareVersion,
                FirmwareVersion = source.FirmwareVersion,
                AreaValue = source.Area?.Numeric,
                WidthValue = source.Width?.Numeric,
                VelocityAverageValue = source.VelocityAverage?.Numeric,
                NumberOfTransects = source.NumberOfTransects,
                TransducerDepth = source.TransducerDepth?.Numeric,
                PercentOfDischargeMeasured = source.PercentOfDischargeMeasured?.Numeric,
                DischargeCoefficientVariation = source.DischargeCoefficientVariation?.Numeric,
                MagneticVariation = source.MagneticVariation?.Numeric,
                BottomEstimateExponent = source.BottomEstimateExponent?.Numeric,
                TopEstimateExponent = source.TopEstimateExponent?.Numeric,
                MeasurementDevice = Map(source.Manufacturer, source.Model, source.SerialNumber),
            };

            if (!string.IsNullOrEmpty(source.BottomEstimateMethod))
            {
                channel.BottomEstimateMethod = new BottomEstimateMethodPickList(source.BottomEstimateMethod);
            }

            if (!string.IsNullOrEmpty(source.TopEstimateMethod))
            {
                channel.TopEstimateMethod = new TopEstimateMethodPickList(source.TopEstimateMethod);
            }

            if (!string.IsNullOrEmpty(source.NavigationMethod))
            {
                channel.NavigationMethod = new NavigationMethodPickList(source.NavigationMethod);
            }

            if (TryParseEnum<DepthReferenceType>(source.DepthReference, out var depthReference))
            {
                channel.DepthReference = depthReference;
            }

            if (TryParseEnum<AdcpMeterSuspensionType>($"{sourceChannel.MeterSuspension}", out var meterSuspension))
            {
                channel.MeterSuspension = meterSuspension;
            }

            if (TryParseEnum<AdcpDeploymentMethodType>($"{sourceChannel.DeploymentMethod}", out var deploymentMethod))
            {
                channel.DeploymentMethod = deploymentMethod;
            }

            SetCommonChannelProperties(channel, sourceChannel);

            return channel;
        }

        private ChannelMeasurementBase Map(VolumetricDischargeActivity source)
        {
            var sourceChannel = source.DischargeChannelMeasurement;
            var measurementPeriod = GetMeasurementPeriod(sourceChannel);

            var unitSystem = CreateUnitSystem(sourceChannel);

            var channel = new VolumetricDischarge(
                measurementPeriod,
                sourceChannel.Channel,
                Map(nameof(sourceChannel.Discharge), sourceChannel.Discharge),
                unitSystem.DistanceUnitId,
                source.MeasurementContainerVolume.Unit)
            {
                IsObserved = source.IsObserved,
                MeasurementContainerVolume = source.MeasurementContainerVolume?.Numeric,
            };

            SetCommonChannelProperties(channel, sourceChannel);

            foreach (var reading in source.VolumetricDischargeReadings ?? new List<VolumetricDischargeReading>())
            {
                channel.Readings.Add(Map(reading));
            }

            return channel;
        }

        private FrameworkVolumetricDischargeReading Map(VolumetricDischargeReading source)
        {
            return new FrameworkVolumetricDischargeReading
            {
                IsUsed = source.IsUsed,
                Name = source.Name,
                Discharge = source.Discharge?.Numeric,
                DurationSeconds = source.DurationInSeconds?.Numeric,
                StartingVolume = source.StartingVolume?.Numeric,
                EndingVolume = source.EndingVolume?.Numeric,
                VolumeChange = source.VolumeChange?.Numeric,
            };
        }

        private ChannelMeasurementBase Map(EngineeredStructureDischargeActivity source)
        {
            var sourceChannel = source.DischargeChannelMeasurement;
            var measurementPeriod = GetMeasurementPeriod(sourceChannel);

            var unitSystem = CreateUnitSystem(sourceChannel);

            var channel = new EngineeredStructureDischarge(
                measurementPeriod,
                sourceChannel.Channel,
                Map(nameof(sourceChannel.Discharge), sourceChannel.Discharge),
                unitSystem.DistanceUnitId,
                source.MeanHead?.Unit ?? unitSystem.DistanceUnitId)
            {
                MeanHeadValue = source.MeanHead?.Numeric,
                StructureEquation = source.EquationForSelectedStructure,
                // TODO: HeadReadings = No Publish API equivalent
            };

            if (TryParseEnum<EngineeredStructureType>(source.StructureType, out var structureType))
            {
                channel.EngineeredStructureType = structureType;
            }

            SetCommonChannelProperties(channel, sourceChannel);

            return channel;
        }

        private ChannelMeasurementBase Map(OtherMethodDischargeActivity source)
        {
            var sourceChannel = source.DischargeChannelMeasurement;
            var measurementPeriod = GetMeasurementPeriod(sourceChannel);

            var unitSystem = CreateUnitSystem(sourceChannel);

            var channel = new OtherDischargeSection(
                measurementPeriod,
                sourceChannel.Channel,
                Map(nameof(sourceChannel.Discharge), sourceChannel.Discharge),
                unitSystem.DistanceUnitId,
                LookupParameterMethod(ParameterIds.Discharge, sourceChannel.MonitoringMethod));

            SetCommonChannelProperties(channel, sourceChannel);

            return channel;
        }

        private Measurement Map(string name, QuantityWithDisplay source)
        {
            if (source == null || string.IsNullOrEmpty(source.Display) && !source.Numeric.HasValue)
                return null;

            if (!source.Numeric.HasValue)
                throw new InvalidOperationException($"{VisitIdentifier}: {name} has no value Display={source.Display} Unit={source.Unit}");

            return new Measurement(source.Numeric.Value, source.Unit);
        }

        private Measurement Map(string name, string unitId, DoubleWithDisplay source)
        {
            if (source == null || !source.Numeric.HasValue && string.IsNullOrEmpty(source.Display))
                return null;

            if (!source.Numeric.HasValue)
                throw new InvalidOperationException($"{VisitIdentifier}: {name} has no value Display={source.Display} Unit={unitId}");

            return new Measurement(source.Numeric.Value, unitId);
        }

        private bool TryParseEnum<TTargetEnum>(string source, out TTargetEnum targetEnum)
            where TTargetEnum : struct
        {
            if (string.IsNullOrEmpty(source) || source == "Unknown")
            {
                targetEnum = default;
                return false;
            }

            if (Enum.TryParse(source, true, out targetEnum))
                return true;

            throw new ArgumentOutOfRangeException(nameof(source), $"{VisitIdentifier}: '{source}' is an invalid {typeof(TTargetEnum).Name} value. Must be one of {string.Join(", ", Enum.GetNames(typeof(TTargetEnum)))}");
        }
    }
}
