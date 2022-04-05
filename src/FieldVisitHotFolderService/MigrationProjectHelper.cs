using System.IO.Compression;
using System.Text.RegularExpressions;
using Common;

namespace FieldVisitHotFolderService
{
    public class MigrationProjectHelper
    {
        public static bool IsFieldVisitEntry(ZipArchiveEntry entry)
        {
            return LocationVisitRegex.IsMatch(entry.FullName) || FieldVisitRegex.IsMatch(entry.FullName);
        }

        public static string GetLocationIdentifier(ZipArchiveEntry entry)
        {
            var match = LocationVisitRegex.Match(entry.FullName);

            return match.Success
                ? match.Groups["location"].Value.Trim()
                : null;
        }

        private static readonly Regex LocationVisitRegex = new Regex(@"^Locations/(?<location>[^/]+)/FieldVisits/[^/]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex FieldVisitRegex = new Regex(@"^FieldVisits/.+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
