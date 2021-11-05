namespace Common
{
    // Equivalent to Aquarius.TimeSeries.Client.ServiceModels.Provisioning.FieldDataPlugin, with some properties removed
    // The partial DTO is replicated here to avoid needing to reference the entire Aquarius.SDK in the plugin tester & packager tools
    public class PluginManifest
    {
        public const string EntryName = "manifest.json";

        public string AssemblyQualifiedTypeName { get; set; }
        public string Description { get; set; }
        public string PluginFolderName { get; set; }
    }
}
