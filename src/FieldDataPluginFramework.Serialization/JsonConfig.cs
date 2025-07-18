﻿using FieldDataPluginFramework.Context;
using FieldDataPluginFramework.DataModel;
using FieldDataPluginFramework.DataModel.Calibrations;
using FieldDataPluginFramework.DataModel.ChannelMeasurements;
using FieldDataPluginFramework.DataModel.CrossSection;
using FieldDataPluginFramework.DataModel.DischargeActivities;
using FieldDataPluginFramework.DataModel.Inspections;
using FieldDataPluginFramework.DataModel.LevelSurveys;
using FieldDataPluginFramework.DataModel.Meters;
using FieldDataPluginFramework.DataModel.PickLists;
using FieldDataPluginFramework.DataModel.Readings;
using FieldDataPluginFramework.DataModel.Verticals;
using ServiceStack;
using ServiceStack.Text;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace FieldDataPluginFramework.Serialization
{
    public class JsonConfig
    {
        private static readonly object SyncObject = new object();

        private static bool _isInitialized;

        public static void Configure()
        {
            lock (SyncObject)
            {
                if (_isInitialized) return;
                _isInitialized = true;
            }

            JsConfig.ExcludeTypeInfo = true;
            JsConfig.DateHandler = DateHandler.ISO8601DateTime;
            JsConfig.IncludeNullValues = true;
            JsConfig.IncludeNullValuesInDictionaries = true;

            JsConfig<DateTimeOffset>.SerializeFn = SerializeDateTimeOffset;
            JsConfig<DateTimeOffset?>.SerializeFn = offset => offset.HasValue
                ? SerializeDateTimeOffset(offset.Value)
                : string.Empty;

            JsConfig<DateTimeOffset>.DeSerializeFn = DeserializeDateTimeOffset;
            JsConfig<DateTimeOffset?>.DeSerializeFn = text => !string.IsNullOrEmpty(text)
                ? DeserializeDateTimeOffset(text)
                : (DateTimeOffset?)null;

            ConfigureSpecialParsers();
        }

        private static string SerializeDateTimeOffset(DateTimeOffset item)
        {
            return item.ToString("O");
        }

        private static DateTimeOffset DeserializeDateTimeOffset(string text)
        {
            return DateTimeOffset.ParseExact(text, "O", CultureInfo.InvariantCulture);
        }

        public class JsonParser
        {
            public JsonObject JsonObject { get; }
            public string JsonText { get; }

            public JsonParser(string jsonText)
            {
                JsonText = jsonText;
                JsonObject = JsonObject.Parse(JsonText);
            }

            public bool HasProperty(string propertyName)
            {
                return !string.IsNullOrEmpty(JsonObject[propertyName]);
            }

            public T GetObject<T>(string propertyName) where T : class
            {
                if (!HasProperty(propertyName))
                    return null;

                var jsonText = JsonObject
                    .Child(propertyName);

                return jsonText?.FromJson<T>();
            }

            public T Get<T>(string propertyName)
            {
                if (!HasProperty(propertyName))
                    return default(T);

                return JsonObject[propertyName]
                    .FromJson<T>();
            }

            public void AddItems<T>(string propertyName, ICollection<T> collection)
            {
                var items = GetObject<List<T>>(propertyName);

                if (items == null) return;

                foreach (var item in items)
                {
                    collection.Add(item);
                }
            }
        }

        private static void Configure<T>(Func<JsonParser, T> itemFactory, Action<JsonParser, T> itemAction = null) where T : class
        {
            JsConfig<T>.RawDeserializeFn = jsonText =>
            {
                if (string.IsNullOrEmpty(jsonText))
                    return null;

                var parser = new JsonParser(jsonText);

                var item = itemFactory(parser);

                SetItemProperties(parser, item);

                itemAction?.Invoke(parser, item);

                return item;
            };
        }


        // Since no derived class type information is serialized to JSON, this method is used to deserialize a derived class based on property names unique to the derived class.
        private static TBase FromDerivedClass<TBase>(JsonParser jsonParser, params (string PropertyName, Func<JsonParser, TBase> DerivedFactory)[] propertyExpressions)
            where TBase : class
        {
            foreach (var propertyExpression in propertyExpressions.Take(propertyExpressions.Length - 1))
            {
                if (jsonParser.HasProperty(propertyExpression.PropertyName))
                    return propertyExpression.DerivedFactory(jsonParser);
            }

            return propertyExpressions
                .Last()
                .DerivedFactory(jsonParser);
        }

        private static void SetItemProperties<T>(JsonParser parser, T item) where T : class
        {
            var type = typeof(T);

            if (!Setters.TryGetValue(type, out var setters))
            {
                setters = CreateSetters<T>();

                Setters[type] = setters;
            }

            if (!setters.Any()) return;

            foreach (var propertyName in parser.JsonObject.Keys)
            {
                if (!setters.TryGetValue(propertyName, out var setter)) continue;

                var value = parser.JsonObject[propertyName];

                if (value == null)
                {
                    var existingValue = setter.PropertyInfo.GetValue(item);

                    if (existingValue != null)
                        throw new ArgumentException($"Can't reset {type.Name}.{propertyName} to null from existing value '{existingValue}'");

                    continue;
                }

                var propertyValue = setter.PropertyParser.Invoke(parser, new object[] { propertyName });

                setter.PropertyInfo.SetValue(item, propertyValue);
            }
        }

        private static readonly
            Dictionary<Type, Dictionary<string, (PropertyInfo PropertyInfo, MethodInfo PropertyParser)>> Setters =
                new Dictionary<Type, Dictionary<string, (PropertyInfo PropertyInfo, MethodInfo PropertyParser)>>();

        private static Dictionary<string, (PropertyInfo PropertyInfo, MethodInfo PropertyParser)> CreateSetters<T>() where T : class
        {
            var type = typeof(T);

            var setters = type
                .GetProperties()
                .Where(p => p.CanWrite)
                .ToDictionary(
                    propertyInfo => propertyInfo.Name,
                    propertyInfo =>
                    {
                        var genericMethodName = propertyInfo.PropertyType.IsValueType
                            ? nameof(JsonParser.Get)
                            : nameof(JsonParser.GetObject);
                        var method = typeof(JsonParser).GetMethod(genericMethodName);

                        if (method == null)
                            throw new ArgumentException($"Can't find {nameof(JsonParser)}.{genericMethodName}<{type.Name}>() method.");

                        var genericMethod = method.MakeGenericMethod(propertyInfo.PropertyType);

                        return (PropertyInfo: propertyInfo, GenericMethod: genericMethod);
                    },
                    StringComparer.InvariantCultureIgnoreCase);

            return setters;
        }

        private static void ConfigureSpecialParsers()
        {
            // What is a "special parser"?
            // That is any framework DTO class for which at least one of these is true:
            // - Lacks a public parameter-less constructor.
            // - Performs a ValidationCheck.* predicate that throws on null property value assignment
            // - Has a read-only collection property (you can add items to the existing collection, but can't assign an entirely new collection)
            // - Has a set-only property with no getter (see ManualGaugingDischargeSection.Width (get-only type=Measurement) and WidthValue (set-only type=double?)
            // These classes need to configure a special deserializer to reconstruct an object from its JSON representation
            // without triggering the exceptions that the framework is trying to enforce to prevent bad data getting into the system.

            Configure(json => new DateTimeInterval(
                json.Get<DateTimeOffset>(nameof(DateTimeInterval.Start)),
                json.Get<DateTimeOffset>(nameof(DateTimeInterval.End))));

            Configure(json => InternalConstructor<LocationInfo>.Invoke(
                json.Get<string>(nameof(LocationInfo.LocationName)),
                json.Get<string>(nameof(LocationInfo.LocationIdentifier)),
                json.Get<long>(nameof(LocationInfo.LocationId)),
                json.Get<Guid>(nameof(LocationInfo.UniqueId)),
                json.Get<TimeSpan>(nameof(LocationInfo.UtcOffset)).TotalHours));

            Configure(json => InternalConstructor<FieldVisitInfo>.Invoke(
                    json.GetObject<LocationInfo>(nameof(FieldVisitInfo.LocationInfo)),
                    json.GetObject<FieldVisitDetails>(nameof(FieldVisitInfo.FieldVisitDetails))),
                (json, item) =>
                {
                    json.AddItems(nameof(item.DischargeActivities), item.DischargeActivities);
                    json.AddItems(nameof(item.ControlConditions), item.ControlConditions);
                    json.AddItems(nameof(item.CrossSectionSurveys), item.CrossSectionSurveys);
                    json.AddItems(nameof(item.LevelSurveys), item.LevelSurveys);
                    json.AddItems(nameof(item.Readings), item.Readings);
                    json.AddItems(nameof(item.Calibrations), item.Calibrations);
                    json.AddItems(nameof(item.Inspections), item.Inspections);
                    json.AddItems(nameof(item.GageZeroFlowActivities), item.GageZeroFlowActivities);
                });

            Configure(json => new FieldVisitDetails(
                    json.GetObject<DateTimeInterval>(nameof(FieldVisitDetails.FieldVisitPeriod))));

            Configure(json => new Measurement(
                json.Get<double>(nameof(Measurement.Value)),
                json.Get<string>(nameof(Measurement.UnitId))));

            Configure(json => new MeasurementDevice(
                json.Get<string>(nameof(MeasurementDevice.Manufacturer)),
                json.Get<string>(nameof(MeasurementDevice.Model)),
                json.Get<string>(nameof(MeasurementDevice.SerialNumber))));

            Configure(ReadingFactory);

            Configure(json => new Calibration(
                json.Get<string>(nameof(Calibration.ParameterId)),
                json.Get<string>(nameof(Calibration.UnitId)),
                json.Get<double>(nameof(Calibration.Value))));

            Configure(json => new Inspection(
                json.Get<InspectionType>(nameof(Inspection.InspectionType))));

            Configure(BackwardsCompatibleCrossSectionPointFactory);

            Configure(json => new CrossSectionSurvey(
                    json.GetObject<DateTimeInterval>(nameof(CrossSectionSurvey.SurveyPeriod)),
                    json.Get<string>(nameof(CrossSectionSurvey.ChannelName)),
                    json.Get<string>(nameof(CrossSectionSurvey.RelativeLocationName)),
                    json.Get<string>(nameof(CrossSectionSurvey.DistanceUnitId)),
                    json.Get<StartPointType>(nameof(CrossSectionSurvey.StartPoint))));

            Configure(json => new LevelSurvey(
                    json.Get<string>(nameof(LevelSurvey.OriginReferencePointName))));

            Configure(json => new LevelSurveyMeasurement(
                    json.Get<string>(nameof(LevelSurveyMeasurement.ReferencePointName)),
                    json.Get<DateTimeOffset>(nameof(LevelSurveyMeasurement.MeasurementTime)),
                    json.Get<double>(nameof(LevelSurveyMeasurement.MeasuredElevation))));

            Configure(json => new DischargeActivity(
                    json.GetObject<DateTimeInterval>(nameof(DischargeActivity.MeasurementPeriod)),
                    json.GetObject<Measurement>(nameof(DischargeActivity.Discharge))),
                (json, item) =>
                {
                    json.AddItems(nameof(item.GageHeightMeasurements), item.GageHeightMeasurements);
                    json.AddItems(nameof(item.ChannelMeasurements), item.ChannelMeasurements);
                });

            Configure(json => json.HasProperty(nameof(Grade.Code))
                ? Grade.FromCode(json.Get<int>(nameof(Grade.Code)))
                : Grade.FromDisplayName(json.Get<string>(nameof(Grade.DisplayName))));

            Configure(json => new GageHeightMeasurement(
                json.GetObject<Measurement>(nameof(GageHeightMeasurement.GageHeight)),
                json.Get<DateTimeOffset>(nameof(GageHeightMeasurement.MeasurementTime)),
                json.Get<bool>(nameof(GageHeightMeasurement.Include))));

            // deserializing derived classes are a bit more tricky. We need to decide based on the presence of unique properties
            Configure(json => FromDerivedClass<ChannelMeasurementBase>(json,
                (nameof(ManualGaugingDischargeSection.StartPoint), j => j.JsonText.FromJson<ManualGaugingDischargeSection>()),
                (nameof(AdcpDischargeSection.AdcpDeviceType), j => j.JsonText.FromJson<AdcpDischargeSection>()),
                (nameof(EngineeredStructureDischarge.EngineeredStructureType), j => j.JsonText.FromJson<EngineeredStructureDischarge>()),
                (nameof(VolumetricDischarge.MeasurementContainerUnit), j => j.JsonText.FromJson<VolumetricDischarge>()),
                (null, j => j.JsonText.FromJson<OtherDischargeSection>())));

            Configure(json => FromDerivedClass<MeasurementConditionData>(json,
                (nameof(IceCoveredData.WaterSurfaceToBottomOfIce), j => j.JsonText.FromJson<IceCoveredData>()),
                (null, j => j.JsonText.FromJson<OpenWaterData>())));

            Configure(json => new MeterCalibration(),
                (json, item) => json.AddItems(nameof(item.Equations), item.Equations));

            Configure(json => new VelocityObservation(),
                (json, item) => json.AddItems(nameof(item.Observations), item.Observations));

            Configure(json => new ManualGaugingDischargeSection(
                    json.GetObject<DateTimeInterval>(nameof(ManualGaugingDischargeSection.MeasurementPeriod)),
                    json.Get<string>(nameof(ManualGaugingDischargeSection.ChannelName)),
                    json.GetObject<Measurement>(nameof(ManualGaugingDischargeSection.Discharge)),
                    json.Get<string>(nameof(ManualGaugingDischargeSection.DistanceUnitId)),
                    json.Get<string>(nameof(ManualGaugingDischargeSection.AreaUnitId)),
                    json.Get<string>(nameof(ManualGaugingDischargeSection.VelocityUnitId))),
                (json, item) =>
                {
                    item.WidthValue = json.GetObject<Measurement>(nameof(item.Width))?.Value;
                    item.AreaValue = json.GetObject<Measurement>(nameof(item.Area))?.Value;
                    item.VelocityAverageValue = json.GetObject<Measurement>(nameof(item.VelocityAverage))?.Value;
                    json.AddItems(nameof(item.Verticals), item.Verticals);
                });

            Configure(json => new AdcpDischargeSection(
                    json.GetObject<DateTimeInterval>(nameof(AdcpDischargeSection.MeasurementPeriod)),
                    json.Get<string>(nameof(AdcpDischargeSection.ChannelName)),
                    json.GetObject<Measurement>(nameof(AdcpDischargeSection.Discharge)),
                    json.Get<string>(nameof(AdcpDischargeSection.AdcpDeviceType)),
                    json.Get<string>(nameof(AdcpDischargeSection.DistanceUnitId)),
                    json.Get<string>(nameof(AdcpDischargeSection.AreaUnitId)),
                    json.Get<string>(nameof(AdcpDischargeSection.VelocityUnitId))),
                (json, item) =>
                {
                    item.WidthValue = json.GetObject<Measurement>(nameof(item.Width))?.Value;
                    item.AreaValue = json.GetObject<Measurement>(nameof(item.Area))?.Value;
                    item.VelocityAverageValue = json.GetObject<Measurement>(nameof(item.VelocityAverage))?.Value;
                });

            Configure(json => new OtherDischargeSection(
                    json.GetObject<DateTimeInterval>(nameof(OtherDischargeSection.MeasurementPeriod)),
                    json.Get<string>(nameof(OtherDischargeSection.ChannelName)),
                    json.GetObject<Measurement>(nameof(OtherDischargeSection.Discharge)),
                    json.Get<string>(nameof(OtherDischargeSection.DistanceUnitId)),
                    json.Get<string>(nameof(OtherDischargeSection.MonitoringMethodCode))));

            Configure(json => new EngineeredStructureDischarge(
                    json.GetObject<DateTimeInterval>(nameof(EngineeredStructureDischarge.MeasurementPeriod)),
                    json.Get<string>(nameof(EngineeredStructureDischarge.ChannelName)),
                    json.GetObject<Measurement>(nameof(EngineeredStructureDischarge.Discharge)),
                    json.Get<string>(nameof(EngineeredStructureDischarge.DistanceUnitId)),
                    json.Get<string>(nameof(EngineeredStructureDischarge.MeanHeadUnitId))),
                (json, item) =>
                {
                    item.MeanHeadValue = json.GetObject<Measurement>(nameof(item.MeanHead))?.Value;
                });

            Configure(json => new VolumetricDischarge(
                    json.GetObject<DateTimeInterval>(nameof(VolumetricDischarge.MeasurementPeriod)),
                    json.Get<string>(nameof(VolumetricDischarge.ChannelName)),
                    json.GetObject<Measurement>(nameof(VolumetricDischarge.Discharge)),
                    json.Get<string>(nameof(VolumetricDischarge.DistanceUnitId)),
                    json.Get<string>(nameof(VolumetricDischarge.MeasurementContainerUnit))),
                (json, item) =>
                {
                    item.MeasurementContainerVolume = json.GetObject<Measurement>(nameof(VolumetricDischarge.MeasurementContainer))?.Value;
                });

            Configure(json => new BottomEstimateMethodPickList(json.Get<string>(nameof(PickList.IdOrDisplayName))));
            Configure(json => new ControlCodePickList(json.Get<string>(nameof(PickList.IdOrDisplayName))));
            Configure(json => new NavigationMethodPickList(json.Get<string>(nameof(PickList.IdOrDisplayName))));
            Configure(json => new ReadingQualifierPickList(json.Get<string>(nameof(PickList.IdOrDisplayName))));
            Configure(json => new TopEstimateMethodPickList(json.Get<string>(nameof(PickList.IdOrDisplayName))));

            Configure(json => CreatePickList(json, s => new ControlConditionPickList(s)));
            Configure(json => CreatePickList(json, s => new HydraulicTestMethodPickList(s)));
            Configure(json => CreatePickList(json, s => new HydraulicTestContextPickList(s)));
            Configure(json => CreatePickList(json, s => new HydraulicTestAquiferTypePickList(s)));
            Configure(json => CreatePickList(json, s => new WellAquiferConnectivityTypePickList(s)));
            Configure(json => CreatePickList(json, s => new WellComponentTypePickList(s)));
            Configure(json => CreatePickList(json, s => new WellConditionTypePickList(s)));
            Configure(json => CreatePickList(json, s => new WellInspectionMethodPickList(s)));
            Configure(json => CreatePickList(json, s => new WellInspectionMethodTypePickList(s)));
            Configure(json => CreatePickList(json, s => new WellRedevelopmentMethodTypePickList(s)));
            Configure(json => CreatePickList(json, s => new WellRepairTypePickList(s)));
        }

        public const int LegacyPointOrder = 0;

        private static CrossSectionPoint BackwardsCompatibleCrossSectionPointFactory(JsonParser json)
        {
            var pointOrder = json.HasProperty(nameof(CrossSectionPoint.PointOrder))
                ? json.Get<int>(nameof(CrossSectionPoint.PointOrder))
                : LegacyPointOrder;

            return new CrossSectionPoint(
                pointOrder,
                json.Get<double>(nameof(CrossSectionPoint.Distance)),
                json.Get<double>(nameof(CrossSectionPoint.Elevation)));
        }



        private static T CreatePickList<T>(JsonParser json, Func<string, T> constructor) where T : PickList
        {
            var pickListValue = json.JsonText.StartsWith("{")
            ? json.Get<string>(nameof(PickList.IdOrDisplayName))
            : json.JsonText;

            return constructor(pickListValue);
        }




        private static Reading ReadingFactory(JsonParser json)
        {
            var value = json.Get<double?>(nameof(Reading.Value));

            return json.HasProperty(nameof(Reading.Measurement))
                ? new Reading(
                    json.Get<string>(nameof(Reading.ParameterId)),
                    json.GetObject<Measurement>(nameof(Reading.Measurement)))
                : new Reading(
                    json.Get<string>(nameof(Reading.ParameterId)),
                    json.Get<string>(nameof(Reading.UnitId)),
                    value);
        }
    }
}
