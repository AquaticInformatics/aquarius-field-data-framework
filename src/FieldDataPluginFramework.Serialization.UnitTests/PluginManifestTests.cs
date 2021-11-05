using Aquarius.TimeSeries.Client.ServiceModels.Provisioning;
using AutoMapper;
using Common;
using NUnit.Framework;

namespace FieldDataPluginFramework.Serialization.UnitTests
{
    [TestFixture]
    public class PluginManifestTests
    {
        [Test]
        public void PluginManifest_MapsToSdkDto()
        {
            // This automap validation is what ensures that the plugin manifest contains enough property members for the AQTS server to install the plugin
            var config = new MapperConfiguration(cfg => {
                cfg.CreateMap<PluginManifest, FieldDataPlugin>()
                    .ForMember(dest => dest.IsEnabled, opt => opt.Ignore())
                    .ForMember(dest => dest.PluginPriority, opt => opt.Ignore())
                    .ForMember(dest => dest.UniqueId, opt => opt.Ignore());
            });

            config.AssertConfigurationIsValid();
        }
    }
}
