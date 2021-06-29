using System;
using System.Collections.Generic;

namespace FieldVisitHotFolderService
{
    public class Context
    {
        public string Server { get; set; }
        public string Username { get; set; } = "admin";
        public string Password { get; set; } = "admin";
        public string HotFolderPath { get; set; }
        public string FileMask { get; set; }
        public string ProcessingFolder { get; set; } = "Processing";
        public string UploadedFolder { get; set; } = "Uploaded";
        public string PartialFolder { get; set; } = "PartialUploads";
        public string ArchivedFolder { get; set; } = "Archived";
        public string FailedFolder { get; set; } = "Failed";
        public TimeSpan FileQuietDelay { get; set; } = TimeSpan.FromSeconds(5);
        public TimeSpan FileScanInterval { get; set; } = TimeSpan.FromMinutes(1);
        public int MaximumConnectionAttempts { get; set; } = 3;
        public TimeSpan ConnectionRetryDelay { get; set; } = TimeSpan.FromMinutes(1);
        public MergeMode MergeMode { get; set; } = MergeMode.Skip;
        public bool OverlapIncludesWholeDay { get; set; }
        public int MaximumConcurrentRequests { get; set; } = Environment.ProcessorCount;
        public int? MaximumFileCount { get; set; }
        public TimeSpan? MaximumFileWaitInterval { get; set; }
        public int MaximumDuplicateRetry { get; set; } = 3;
        public Dictionary<string, string> LocationAliases { get; } = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
        public TimeSpan MaximumVisitDuration { get; set; } = TimeSpan.FromDays(1.25);
        public bool DryRun { get; set; }
        public Dictionary<string, Dictionary<string,string>> PluginSettings { get; } = new Dictionary<string, Dictionary<string, string>>(StringComparer.InvariantCultureIgnoreCase);
        public Dictionary<string, int> PluginPriority { get; } = new Dictionary<string, int>(StringComparer.InvariantCultureIgnoreCase);
    }
}
