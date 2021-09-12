using System.Collections.Generic;
using System.Linq;
using Aquarius.TimeSeries.Client.ServiceModels.Provisioning;

namespace FieldVisitHotFolderService
{
    public class ParameterIdLookup
    {
        private Dictionary<string,string> Lookup { get; }

        public ParameterIdLookup(ParametersResponse response)
        {
            Lookup = response
                .Results
                .ToDictionary(
                    p => p.Identifier,
                    p => p.ParameterId);
        }

        public bool TryGetValue(string parameterIdentifier, out string parameterId)
        {
            return Lookup.TryGetValue(parameterIdentifier, out parameterId);
        }
    }
}
