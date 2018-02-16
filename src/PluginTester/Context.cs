using FieldDataPluginFramework.Results;

namespace PluginTester
{
    public class Context
    {
        public string PluginPath { get; set; }
        public string DataPath { get; set; }
        public string LocationIdentifier { get; set; }
        public string JsonPath { get; set; }
        public ParseFileStatus ExpectedStatus { get; set; } = ParseFileStatus.SuccessfullyParsedAndDataValid;
        public string ExpectedError { get; set; }
    }
}
