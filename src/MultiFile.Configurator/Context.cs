namespace MultiFile.Configurator
{
    public class Context
    {
        public string Server { get; set; }
        public string Username { get; set; } = "admin";
        public string Password { get; set; } = "admin";
        public bool SaveOnServer { get; set; } = true;
        public string JsonPath { get; set; }
        public bool IncludeDisabledPluginSettings { get; set; }
        public bool GenerateForExternalUse { get; set; }
    }
}
