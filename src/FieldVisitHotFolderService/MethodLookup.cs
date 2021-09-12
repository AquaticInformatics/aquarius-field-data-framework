using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Aquarius.TimeSeries.Client.ServiceModels.Provisioning;
using Humanizer;
using log4net;

namespace FieldVisitHotFolderService
{
    public class MethodLookup
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private Dictionary<string, Dictionary<string, string>> Lookup { get; } =
            new Dictionary<string, Dictionary<string, string>>();

        private Dictionary<string, Dictionary<string, HashSet<string>>> AmbiguousMethods { get; } =
            new Dictionary<string, Dictionary<string, HashSet<string>>>();

        public MethodLookup(MonitoringMethodsResponse response)
        {
            foreach (var grouping in response.Results.GroupBy(m => m.ParameterId ?? string.Empty))
            {
                var parameterId = grouping.Key;
                var parameterMethods = new Dictionary<string, string>();

                Lookup.Add(parameterId, parameterMethods);

                foreach (var m in grouping)
                {
                    if (parameterMethods.TryGetValue(m.DisplayName, out var existingMethodCode))
                    {
                        AddAmbiguousMethod(parameterId, m.DisplayName, m.MethodCode);
                        AddAmbiguousMethod(parameterId, m.DisplayName, existingMethodCode);
                    }
                    else
                    {
                        parameterMethods.Add(m.DisplayName, m.MethodCode);
                    }
                }
            }

            if (!AmbiguousMethods.Any())
                return;

            Log.Warn($"{"ambiguous method".ToQuantity(AmbiguousMethods.Sum(kvp => kvp.Value.Sum(kvp2 => kvp2.Value.Count)))} detected. Field visit data using these methods will not be exported.");

            foreach (var kvp in AmbiguousMethods)
            {
                var parameterId = kvp.Key;
                var ambiguousNames = kvp.Value;

                foreach (var kvp2 in ambiguousNames)
                {
                    var ambiguousName = kvp2.Key;
                    var ambiguousMethodCodes = kvp2.Value;

                    Log.Warn($"ParameterId='{parameterId}' MethodDisplayName='{ambiguousName}' is used by {kvp2.Value.Count} method codes: {string.Join(", ", ambiguousMethodCodes.OrderBy(s => s))}");
                    Lookup[parameterId].Remove(ambiguousName);
                }
            }
        }

        private void AddAmbiguousMethod(string parameterId, string methodDisplayName, string methodCode)
        {
            if (!AmbiguousMethods.TryGetValue(parameterId, out var ambiguousNames))
            {
                ambiguousNames = new Dictionary<string, HashSet<string>>();
                AmbiguousMethods.Add(parameterId, ambiguousNames);
            }

            if (!ambiguousNames.TryGetValue(methodDisplayName, out var ambiguousMethodCodes))
            {
                ambiguousMethodCodes = new HashSet<string>();
                ambiguousNames.Add(methodDisplayName, ambiguousMethodCodes);
            }

            ambiguousMethodCodes.Add(methodCode);
        }

        public bool TryGetValue(string parameterId, string methodDisplayName, out string methodCode)
        {
            const string defaultMethodName = "None";
            const string defaultMethodCode = "DefaultNone";

            methodCode = default;

            if (Lookup.TryGetValue(parameterId, out var parameterMethods) && parameterMethods.TryGetValue(methodDisplayName, out methodCode))
                return true;

            if (!methodDisplayName.Equals(defaultMethodName))
                return false;

            methodCode = defaultMethodCode;
            return true;
        }

        public bool IsAmbiguous(string parameterId, string methodDisplayName, out HashSet<string> ambiguousMethodCodes)
        {
            ambiguousMethodCodes = default;

            return AmbiguousMethods.TryGetValue(parameterId, out var ambiguousNames)
                   && ambiguousNames.TryGetValue(methodDisplayName, out ambiguousMethodCodes);
        }
    }
}
