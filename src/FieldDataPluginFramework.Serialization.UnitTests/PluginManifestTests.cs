using System.Linq;
using Aquarius.TimeSeries.Client.ServiceModels.Provisioning;
using Common;
using NUnit.Framework;

namespace FieldDataPluginFramework.Serialization.UnitTests
{
    [TestFixture]
    public class PluginManifestTests
    {
        // This validation is what ensures that the plugin manifest contains enough property members for the AQTS server to install the plugin
        private static readonly string[] IgnoredTargetMembers =
        {
            nameof(FieldDataPlugin.IsEnabled),
            nameof(FieldDataPlugin.PluginPriority),
            nameof(FieldDataPlugin.UniqueId)
        };

        [Test]
        public void PluginManifest_MapsToSdkDto()
        {
            var manifest = new PluginManifest
            {
                AssemblyQualifiedTypeName = "assembly-qualified-type-name",
                Description = "plugin-description",
                PluginFolderName = "plugin-folder-name"
            };

            var sourceProperties = typeof(PluginManifest)
                .GetProperties()
                .Where(property => property.CanRead)
                .OrderBy(property => property.Name)
                .ToArray();

            var mappedTargetProperties = typeof(FieldDataPlugin)
                .GetProperties()
                .Where(property => property.CanWrite && !IgnoredTargetMembers.Contains(property.Name))
                .OrderBy(property => property.Name)
                .ToArray();

            CollectionAssert.AreEqual(
                sourceProperties.Select(property => property.Name).ToArray(),
                mappedTargetProperties.Select(property => property.Name).ToArray());

            CollectionAssert.AreEqual(
                sourceProperties.Select(property => property.PropertyType).ToArray(),
                mappedTargetProperties.Select(property => property.PropertyType).ToArray());

            var dto = new FieldDataPlugin();

            foreach (var sourceProperty in sourceProperties)
            {
                var targetProperty = typeof(FieldDataPlugin).GetProperty(sourceProperty.Name);
                var expectedValue = sourceProperty.GetValue(manifest);

                Assert.That(targetProperty, Is.Not.Null, sourceProperty.Name);

                targetProperty.SetValue(dto, expectedValue);

                Assert.That(targetProperty.GetValue(dto), Is.EqualTo(expectedValue), sourceProperty.Name);
            }
        }
    }
}
