using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Common
{
    public class FileHelper
    {
        // ReSharper disable once PossibleNullReferenceException
        public static string ExeFullPath => Path.GetFullPath(Assembly.GetEntryAssembly().Location);
        public static string ExeDirectory => Path.GetDirectoryName(ExeFullPath);
        public static string ExeVersion => FileVersionInfo.GetVersionInfo(ExeFullPath).FileVersion;
        public static string ExeNameAndVersion => $"{Path.GetFileNameWithoutExtension(ExeFullPath)} (v{ExeVersion})";
    }
}
