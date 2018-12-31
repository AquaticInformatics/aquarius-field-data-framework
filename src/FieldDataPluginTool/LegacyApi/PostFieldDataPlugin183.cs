using Aquarius.TimeSeries.Client.ServiceModels.Provisioning;
using ServiceStack;

namespace FieldDataPluginTool.LegacyApi
{
    [Route("/fielddataplugins", HttpMethods.Post, Summary = "Registers a field data plug-in")]
    public class PostFieldDataPlugin183 : IReturn<FieldDataPlugin>
    {
        public string PluginFolderName { get; set; }
        public string AssemblyQualifiedTypeName { get; set; }
        public int PluginPriority { get; set; }
        public string Description { get; set; }
    }
}
