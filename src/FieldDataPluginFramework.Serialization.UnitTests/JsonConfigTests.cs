using AutoFixture;
using FieldDataPluginFramework.Context;
using FieldDataPluginFramework.DataModel;
using FieldDataPluginFramework.DataModel.Calibrations;
using FieldDataPluginFramework.DataModel.ChannelMeasurements;
using FieldDataPluginFramework.DataModel.ControlConditions;
using FieldDataPluginFramework.DataModel.CrossSection;
using FieldDataPluginFramework.DataModel.DischargeActivities;
using FieldDataPluginFramework.DataModel.GageZeroFlow;
using FieldDataPluginFramework.DataModel.Inspections;
using FieldDataPluginFramework.DataModel.LevelSurveys;
using FieldDataPluginFramework.DataModel.PickLists;
using FieldDataPluginFramework.DataModel.Readings;
using FieldDataPluginFramework.DataModel.Verticals;
using FluentAssertions;
using NUnit.Framework;
using ServiceStack;
using System;
using System.Collections.Generic;

namespace FieldDataPluginFramework.Serialization.UnitTests
{
    [TestFixture]
    public class JsonConfigTests
    {
        [OneTimeSetUp]
        public void SetupOnce()
        {
            JsonConfig.Configure();
        }

        private static readonly IEnumerable<TestCaseData> DateTimeOffsetTests = new[]
        {
            new TestCaseData(DateTimeOffset.MinValue, "MinValue"),
            new TestCaseData(DateTimeOffset.MaxValue, "MaxValue"),
            new TestCaseData(new DateTimeOffset(1867, 7, 1, 12, 0, 0, TimeSpan.FromHours(-4)), "Canada Confederation Day, noon, Ottawa local time"),
            new TestCaseData(new DateTimeOffset(2014, 4, 1, 0, 0, 0, TimeSpan.Zero), "April Fools 2014, UTC"),
        };

        [TestCaseSource(nameof(DateTimeOffsetTests))]
        public void DateTimeOffset_RoundTripsCorrectly(DateTimeOffset expected, string reason)
        {
            AssertObjectRoundTripsCorrectly(expected, reason);
        }

        [TestCase("", "Empty string")]
        [TestCase("Hello world", "Some value")]
        [TestCase("A \"quote", "Double quotes are handled")]
        [TestCase(null, "Null is null")]
        public void String_RoundTripsCorrectly(string expected, string reason)
        {
            AssertObjectRoundTripsCorrectly(expected, reason);
        }

        private void AssertObjectRoundTripsCorrectly<T>(T expected, string reason = "")
        {
            var jsonText = expected.ToJson();
            var actual = jsonText.FromJson<T>();

            actual.Should().BeEquivalentTo(expected, options => options.RespectingRuntimeTypes(), reason);
        }

        private static readonly IEnumerable<TestCaseData> DateTimeIntervalTests = new[]
        {
            new TestCaseData(new DateTimeInterval(DateTimeOffset.MinValue, DateTimeOffset.MaxValue), "Largest possible interval"),
            new TestCaseData(new DateTimeInterval(new DateTimeOffset(2012, 12, 31, 23, 59, 59, TimeSpan.FromHours(-3)), TimeSpan.FromDays(1)), "2013 New Year's day, less one second"),
            new TestCaseData(new DateTimeInterval(DateTimeOffset.MinValue, TimeSpan.Zero), "Shortest possible interval"),
        };

        [TestCaseSource(nameof(DateTimeIntervalTests))]
        public void DateTimeInterval_RoundTripsCorrectly(DateTimeInterval expected, string reason)
        {
            AssertObjectRoundTripsCorrectly(expected, reason);
        }

        [Test]
        public void Current_ControlCondition_RoundTripsCorrectly()
        {
            AssertObjectRoundTripsCorrectly(new ControlConditionPickList("SomeValue"));
        }

        [Test]
        public void Legacy_ControlConditionJson_IsUpgradedCorrectly()
        {
            const string enumValue = "OldEnumValueName";

            var actual = enumValue.FromJson<ControlConditionPickList>();

            actual.IdOrDisplayName.Should().BeEquivalentTo(enumValue);
        }

        [Test]
        public void Current_CrossSectionPoint_RoundTripsCorrectly()
        {
            AssertObjectRoundTripsCorrectly(new CrossSectionPoint(1, 2, 3)
            {
                Depth = 4,
                Comments = "A comment",
            });
        }

        [Test]
        public void Legacy_CrossSectionPointJson_IsUpgradedCorrectly()
        {
            var expected = new CrossSectionPoint(1, 2, 3)
            {
                Depth = 4,
                Comments = "A comment",
            };

            var jsonText = $"{{\"{nameof(expected.Distance)}\":{expected.Distance},\"{nameof(expected.Elevation)}\":{expected.Elevation},\"{nameof(expected.Depth)}\":{expected.Depth},\"{nameof(expected.Comments)}\":\"{expected.Comments}\"}}";

            jsonText.Should().NotContain(nameof(expected.PointOrder));

            var actual = jsonText.FromJson<CrossSectionPoint>();

            actual.PointOrder.Should().Be(JsonConfig.LegacyPointOrder, "Legacy points should receive an expected point order value");

            actual.Should().BeEquivalentTo(expected, options => options.Excluding(ctx => ctx.PointOrder));
        }

        private static readonly IEnumerable<TestCaseData> MeasurementConditionDataTests = new[]
        {
            new TestCaseData(new OpenWaterData{DistanceToMeter = 1}, "Open water"),
            new TestCaseData(new IceCoveredData{IceThickness = 4}, "Ice data"),
        };

        [TestCaseSource(nameof(MeasurementConditionDataTests))]
        public void MeasurementConditionData_RoundTripsCorrectly(MeasurementConditionData expected, string reason)
        {
            AssertObjectRoundTripsCorrectly(expected, reason);
        }

        private static readonly IEnumerable<TestCaseData> GradeTests = new[]
        {
            new TestCaseData(Grade.FromCode(123), "Grade.Code = 123"),
            new TestCaseData(Grade.FromDisplayName("SomeName"), "Grade.DisplayName = 'SomeName'"),
        };

        [TestCaseSource(nameof(GradeTests))]
        public void Grade_RoundTripsCorrectly(Grade expected, string reason)
        {
            AssertObjectRoundTripsCorrectly(expected, reason);
        }

        private static readonly IEnumerable<TestCaseData> ReadingTests = new[]
        {
            new TestCaseData(new Reading("TA", new Measurement(0, "degC")), "Temperature reading with a value"),
            new TestCaseData(new Reading("TA", "degC", null), "Temperature reading with no value"),
        };

        [TestCaseSource(nameof(ReadingTests))]
        public void Reading_RoundTripsCorrectly(Reading reading, string reason)
        {
            AssertObjectRoundTripsCorrectly(reading, reason);
        }

        private IFixture Fixture { get; set; }

        [Test]
        public void AppendedResults_RoundTripsCorrectly()
        {
            SetupFixture();

            var results = Fixture
                .Create<AppendedResults>();

            AssertObjectRoundTripsCorrectly(results, "Foo");
        }

        private void SetupFixture()
        {
            Fixture = new Fixture();

            Fixture.Register(CreateTimeSpan);
            Fixture.Register(CreateDateTimeInterval);
            Fixture.Register(CreateLocationInfo);
            Fixture.Register(CreateFieldVisitInfo);
            Fixture.Register(CreateEnumValue<InspectionType>);
            Fixture.Register(CreateDischargeActivity);
            Fixture.Register(CreateChannelMeasurement);
            Fixture.Register(CreateGageZeroFlowActivity);
        }

        private TEnum CreateEnumValue<TEnum>() where TEnum : struct
        {
            var type = typeof(TEnum);

            var values = Enum.GetValues(type);

            var index = 1 + (Fixture.Create<uint>() % (values.Length - 1));

            var value = (TEnum)values.GetValue(index);

            return value;
        }

        private TimeSpan CreateTimeSpan()
        {
            return TimeSpan.FromMinutes(Fixture.Create<int>());
        }

        private DateTimeInterval CreateDateTimeInterval()
        {
            return new DateTimeInterval(
                Fixture.Create<DateTimeOffset>(),
                Fixture.Create<TimeSpan>());
        }

        private LocationInfo CreateLocationInfo()
        {
            return InternalConstructor<LocationInfo>.Invoke(
                Fixture.Create<string>(),
                Fixture.Create<string>(),
                Fixture.Create<long>(),
                Fixture.Create<Guid>(),
                Fixture.Create<double>());
        }

        private DischargeActivity CreateDischargeActivity()
        {
            var discharge = new DischargeActivity(
                Fixture.Create<DateTimeInterval>(),
                Fixture.Create<Measurement>());

            foreach (var item in Fixture.CreateMany<ChannelMeasurementBase>())
            {
                discharge.ChannelMeasurements.Add(item);
            }

            foreach (var item in Fixture.CreateMany<GageHeightMeasurement>())
            {
                discharge.GageHeightMeasurements.Add(item);
            }

            return discharge;
        }

        private ChannelMeasurementBase CreateChannelMeasurement()
        {
            var channelMeasurement = CreateNextChannelMeasurement();

            NextMeasurementType = (ChannelMeasurementType)((1 + (int)NextMeasurementType) % ChannelMeasurementTypeCount);

            return channelMeasurement;
        }

        private ChannelMeasurementBase CreateNextChannelMeasurement()
        {
            switch (NextMeasurementType)
            {
                case ChannelMeasurementType.ManualGauging:
                    return Fixture.Create<ManualGaugingDischargeSection>();

                case ChannelMeasurementType.Adcp:
                    return Fixture.Create<AdcpDischargeSection>();

                case ChannelMeasurementType.Other:
                    return Fixture.Create<OtherDischargeSection>();

                case ChannelMeasurementType.Volumetric:
                    return Fixture.Create<VolumetricDischarge>();

                case ChannelMeasurementType.EngineeredStructure:
                    return Fixture.Create<EngineeredStructureDischarge>();

                default:
                    throw new InvalidOperationException($"{NextMeasurementType} is not a known {nameof(ChannelMeasurementType)}: {string.Join(", ", Enum.GetNames(typeof(ChannelMeasurementType)))}");
            }
        }

        private ChannelMeasurementType NextMeasurementType { get; set; }

        private enum ChannelMeasurementType
        {
            ManualGauging,
            Adcp,
            Other,
            Volumetric,
            EngineeredStructure,
        }

        private static readonly int ChannelMeasurementTypeCount =
            Enum.GetValues(typeof(ChannelMeasurementType)).Length;

        private GageZeroFlowActivity CreateGageZeroFlowActivity()
        {
            var applicableSinceDate = Fixture.Create<DateTimeOffset>();
            var observationDate = applicableSinceDate + Fixture.Create<TimeSpan>();

            return new GageZeroFlowActivity(
                observationDate,
                Fixture.Create<Measurement>(),
                Fixture.Create<double>(),
                Fixture.Create<double>())
            {
                ApplicableSinceDate = applicableSinceDate,
                Comments = Fixture.Create<string>(),
                Party = Fixture.Create<string>(),
                Certainty = Fixture.Create<double>()
            };
        }

        private FieldVisitInfo CreateFieldVisitInfo()
        {
            var visit = InternalConstructor<FieldVisitInfo>.Invoke(
                Fixture.Create<LocationInfo>(),
                Fixture.Create<FieldVisitDetails>());

            visit.ControlConditions.Add(Fixture.Create<ControlCondition>());

            foreach (var item in Fixture.CreateMany<DischargeActivity>(ChannelMeasurementTypeCount))
            {
                visit.DischargeActivities.Add(item);
            }

            foreach (var item in Fixture.CreateMany<Reading>())
            {
                visit.Readings.Add(item);
            }

            foreach (var item in Fixture.CreateMany<Inspection>())
            {
                visit.Inspections.Add(item);
            }

            foreach (var item in Fixture.CreateMany<Calibration>())
            {
                visit.Calibrations.Add(item);
            }

            foreach (var item in Fixture.CreateMany<CrossSectionSurvey>())
            {
                visit.CrossSectionSurveys.Add(item);
            }

            foreach (var item in Fixture.CreateMany<LevelSurvey>())
            {
                visit.LevelSurveys.Add(item);
            }

            visit.GageZeroFlowActivities.Add(Fixture.Create<GageZeroFlowActivity>());

            return visit;
        }
    }
}
