using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FieldVisitHotFolderService
{
    public class CommandLineUsage
    {
        private static readonly string UsageTextFormat = "{0}"
                                                         + "\n"
                                                         + "\nUsage: {1} [-option=value] [@optionsFile] ..."
                                                         + "\n"
                                                         + "\nSupported -option=value settings (/option=value works too):\n\n  {2}"
                                                         + "\n"
                                                         + "\nUse the @optionsFile syntax to read more options from a file."
                                                         + "\n"
                                                         + "\n  Each line in the file is treated as a command line option."
                                                         + "\n  Blank lines and leading/trailing whitespace is ignored."
                                                         + "\n  Comment lines begin with a # or // marker.";

        public static string ComposeUsageText(string generalPurposeLine, IEnumerable<CommandLineOption> options, params string[] extraLines)
        {
            var optionsList = options.ToList();

            SetMaximumKeyWidth(optionsList);

            var allOptionUsageText = string.Join("\n  ", optionsList.Select(o => o.UsageText()));

            if (extraLines.Any())
            {
                allOptionUsageText += string.Join("\n", new[]{"", ""}.Concat(extraLines));
            }

            var exeName = GetProgramName();
            return string.Format(UsageTextFormat, generalPurposeLine, exeName, allOptionUsageText);
        }

        private static void SetMaximumKeyWidth(IEnumerable<CommandLineOption> options)
        {
            CommandLineOption.SetKeyWidth(1 + options.Max(o => o.Key?.Length ?? 0));
        }

        private static string GetProgramName()
        {
            return Path.GetFileNameWithoutExtension(FileHelper.ExeFullPath);
        }
    }
}
