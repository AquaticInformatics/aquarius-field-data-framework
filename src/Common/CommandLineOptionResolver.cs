using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Common
{
    public class CommandLineOptionResolver
    {
        private static readonly Regex ArgRegex = new Regex(@"^([/-])(?<key>[^=]+)=(?<value>.*)$", RegexOptions.Compiled);
        private static readonly IReadOnlyList<string> HelpArgs = 
            new List<string>{"/?","-?","/h","-h","-help","/help"};

        public void Resolve(string[] args, IReadOnlyList<CommandLineOption> options, string usage, Func<string,bool> positionArgumentResolver = null)
        {
            var resolvedArgs = args
                .SelectMany(ResolveOptionsFromFile);

            foreach (var arg in resolvedArgs)
            {
                if (IsHelpArg(args.Length, arg))
                {
                    throw new ExpectedException(usage);
                }

                var match = ArgRegex.Match(arg);

                if (!match.Success)
                {
                    if (positionArgumentResolver != null && positionArgumentResolver(arg))
                        continue;

                    throw new ExpectedException($"Unknown argument: {arg}\n\n{usage}");
                }

                var key = match.Groups["key"].Value.ToLower();
                var value = match.Groups["value"].Value;

                var option =
                    options.FirstOrDefault(o => o.Key != null && o.Key.Equals(key, StringComparison.InvariantCultureIgnoreCase));

                if (option == null)
                {
                    throw new ExpectedException($"Unknown -option=value: {arg}\n\n{usage}");
                }

                option.Setter(value);
            }
        }

        private bool IsHelpArg(int argsLength, string arg)
        {
            if (string.IsNullOrWhiteSpace(arg))
                return false;

            var lowerArg = arg.ToLower();

            return argsLength == 1 &&
                   HelpArgs.Any(a => string.Equals(lowerArg, a) || lowerArg.EndsWith(a));
        }

        private static IEnumerable<string> ResolveOptionsFromFile(string arg)
        {
            if (!arg.StartsWith("@"))
                return new[] { arg };

            var path = arg.Substring(1);

            if (!File.Exists(path))
                throw new ExpectedException($"Options file '{path}' does not exist.");

            return File.ReadAllLines(path)
                       .Where(s => !string.IsNullOrWhiteSpace(s))
                       .Select(s => s.Trim())
                       .Where(s => !s.StartsWith("#") && !s.StartsWith("//"));
        }
    }
}
