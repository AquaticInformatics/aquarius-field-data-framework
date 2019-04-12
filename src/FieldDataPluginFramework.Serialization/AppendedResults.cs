using System.Collections.Generic;
using FieldDataPluginFramework.Context;

namespace FieldDataPluginFramework.Serialization
{
    public class AppendedResults
    {
        public string FrameworkAssemblyQualifiedName { get; set; }
        public string PluginAssemblyQualifiedTypeName { get; set; }
        public List<FieldVisitInfo> AppendedVisits { get; set; } = new List<FieldVisitInfo>();
    }
}
