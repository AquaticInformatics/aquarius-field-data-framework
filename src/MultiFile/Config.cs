using System.Collections.Generic;

namespace MultiFile
{
    public class Config
    {
        public List<PluginConfig> Plugins { get; set; } = new List<PluginConfig>();
        public bool Verbose { get; set; }
    }

    public class PluginConfig
    {
        public string Path { get; set; }
        public int? PluginPriority { get; set; }
        public Dictionary<string, string> Settings { get; set; } = new Dictionary<string, string>();
    }
}
