using System;
using System.Collections.Generic;
using FieldDataPluginFramework.Results;

namespace PluginTester
{
    public class Context
    {
        public string PluginPath { get; set; }
        public List<string> DataPaths { get; set; } = new List<string>();
        public string LocationIdentifier { get; set; }
        public TimeSpan LocationUtcOffset { get; set; } = TimeSpan.FromHours(-8);
        public string JsonPath { get; set; }
        public ParseFileStatus ExpectedStatus { get; set; } = ParseFileStatus.SuccessfullyParsedAndDataValid;
        public string ExpectedError { get; set; }
    }
}
