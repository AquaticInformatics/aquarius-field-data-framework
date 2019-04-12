using System;
using System.Collections.Generic;
using FieldDataPluginFramework.DataModel;
using FieldDataPluginFramework.DataModel.CrossSection;
using FieldDataPluginFramework.DataModel.PickLists;
using FieldDataPluginFramework.DataModel.Verticals;
using FluentAssertions;
using NUnit.Framework;
using ServiceStack;

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
    }
}
