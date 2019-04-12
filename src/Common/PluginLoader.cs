using System.Collections.Generic;
using System.Linq;
using FieldDataPluginFramework;

namespace Common
{
    public class PluginLoader
    {
        public List<IFieldDataPlugin> LoadPlugins(List<string> paths)
        {
            return paths
                .Select(LoadPlugin)
                .ToList();
        }

        private IFieldDataPlugin LoadPlugin(string path)
        {
            return null;
        }
    }
}
