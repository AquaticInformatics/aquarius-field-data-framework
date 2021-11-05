using System;
using System.Collections.Generic;

namespace PluginTester
{
    public class Context
    {
        public string FrameworkAssemblyPath { get; set; }
        public string PluginPath { get; set; }
        public List<string> DataPaths { get; set; } = new List<string>();
        public string LocationIdentifier { get; set; }
        public TimeSpan LocationUtcOffset { get; set; } = TimeSpan.FromHours(-8);
        public string JsonPath { get; set; }
        public StatusType ExpectedStatus { get; set; } = StatusType.SuccessfullyParsedAndDataValid;
        public string ExpectedError { get; set; }
        public bool RecursiveSearch { get; set; }
        public Dictionary<string,string> Settings { get; } = new Dictionary<string, string>();
        public bool Verbose { get; set; }
    }
}
