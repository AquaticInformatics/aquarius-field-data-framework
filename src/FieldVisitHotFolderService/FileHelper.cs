using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace FieldVisitHotFolderService
{
    public class FileHelper
    {
        public static string ExeFullPath => Assembly.GetEntryAssembly().Location;
        public static string ExeDirectory => Path.GetDirectoryName(ExeFullPath);
        public static string ExeVersion => FileVersionInfo.GetVersionInfo(ExeFullPath).FileVersion;
        public static string ExeNameAndVersion => $"{Path.GetFileNameWithoutExtension(ExeFullPath)} (v{ExeVersion})";
    }
}
