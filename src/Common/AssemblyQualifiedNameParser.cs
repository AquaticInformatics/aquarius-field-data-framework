using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Common
{
    public class AssemblyQualifiedNameParser
    {
        public class Result
        {
            public string TypeName { get; set; }
            public string AssemblyName { get; set; }
            public string Version { get; set; }

            public Dictionary<string, string> Properties { get; } =
                new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
        }

        public static bool TryParse(string assemblyQualifiedName, out Result result)
        {
            result = default;

            var match = AssemblyQualifiedNameRegex.Match(assemblyQualifiedName);

            if (match.Success)
            {
                result = new Result
                {
                    TypeName = match.Groups["typeName"].Value.Trim(),
                    AssemblyName = match.Groups["assemblyName"].Value.Trim(),
                    Version = match.Groups["version"].Value.Trim(),
                };

                var names = match.Groups["propertyName"].Captures;
                var values = match.Groups["propertyValue"].Captures;

                if (names.Count != values.Count)
                    throw new ExpectedException($"'{assemblyQualifiedName}' has {names.Count} property names, but {values.Count} values.");

                for(var i = 0; i < names.Count; ++i)
                    result.Properties.Add(names[i].Value.Trim(), values[i].Value.Trim());
            }

            return match.Success;
        }

        private static readonly Regex AssemblyQualifiedNameRegex = new Regex(@"^(?<typeName>.+),\s+(?<assemblyName>\w+),\s*Version=(?<version>[^,]+)(\s*,\s*(?<propertyName>\w+)=(?<propertyValue>[^,]+))*$");

        public static Result Parse(string assemblyQualifiedName)
        {
            if (TryParse(assemblyQualifiedName, out var result))
                return result;

            throw new ExpectedException($"'{assemblyQualifiedName}' is not an assembly qualified name.");
        }
    }
}
