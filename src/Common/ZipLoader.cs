using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FieldDataPluginFramework;
using FieldDataPluginFramework.Context;
using FieldDataPluginFramework.Results;

namespace Common
{
    public class ZipLoader
    {
        public IFieldDataPlugin Plugin { get; set; }
        public ILog Logger { get; set; }
        public IFieldDataResultsAppender Appender { get; set; }
        public LocationInfo LocationInfo { get; set; }

        public ParseFileResultWithAttachments ParseFile(byte[] dataBytes)
        {
            // Many plugins, like FlowTracker2, accept a ZIP archive that also matches the Zip-with-attachments pattern.
            // So always try the plugin first with the actual data
            if (TryParseFile(dataBytes, out var directAttempt)
                && IsSuccessfulParse(directAttempt.Result))
                return new ParseFileResultWithAttachments
                {
                    Result = directAttempt.Result
                };

            ParseFileResultWithAttachments zipAttempt = null;

            if (TryParse(dataBytes, out var zipWithAttachments)
                && TryParseFile(zipWithAttachments.PluginDataBytes, out zipAttempt)
                && IsSuccessfulParse(zipAttempt.Result))
                return new ParseFileResultWithAttachments
                {
                    Result = zipAttempt.Result,
                    Attachments = zipWithAttachments.Attachments
                };

            if (zipWithAttachments != null)
            {
                if (zipAttempt == null)
                    throw new ArgumentNullException(nameof(zipAttempt));

                // Something failed inside the Zip parser
                return new ParseFileResultWithAttachments
                {
                    Result = zipAttempt.Result,
                    Attachments = zipWithAttachments.Attachments,
                };
            }

            return new ParseFileResultWithAttachments
            {
                Result = directAttempt.Result,
            };
        }

        private static bool IsSuccessfulParse(ParseFileResult result)
        {
            return result.Status != ParseFileStatus.CannotParse;
        }

        private bool TryParseFile(byte[] dataBytes, out ParseFileResultWithAttachments result)
        {
            result = new ParseFileResultWithAttachments();

            try
            {
                using (var stream = new MemoryStream(dataBytes))
                {
                    result.Result = LocationInfo != null
                        ? Plugin.ParseFile(stream, LocationInfo, Appender, Logger)
                        : Plugin.ParseFile(stream, Appender, Logger);
                }

                return true;
            }
            catch (Exception exception)
            {
                result.Result = ParseFileResult.CannotParse(exception);

                return false;
            }
        }

        private bool TryParse(byte[] dataBytes, out ZipWithAttachments result)
        {
            try
            {
                using (var stream = new MemoryStream(dataBytes))
                using (var archive = new ZipArchive(stream))
                {
                    var fileEntries = archive
                        .Entries
                        .Where(e => !e.FullName.EndsWith("/"))
                        .ToList();

                    var rootEntries = fileEntries
                        .Where(e => e.Name == e.FullName)
                        .ToList();

                    var attachmentEntries = fileEntries
                        .Where(e => !rootEntries.Contains(e))
                        .ToList();

                    if (rootEntries.Count != 1 || !attachmentEntries.Any())
                    {
                        result = null;
                        return false;
                    }

                    var pluginDataEntry = rootEntries.First();

                    result = new ZipWithAttachments
                    {
                        PluginDataBytes = LoadAttachment(pluginDataEntry).Content,
                        Attachments = attachmentEntries
                            .Select(LoadAttachment)
                            .ToList()
                    };

                    return true;
                }
            }
            catch (InvalidDataException)
            {
                result = null;
                return false;
            }
        }

        private const int MaxAttachmentSize = int.MaxValue;

        private static Attachment LoadAttachment(ZipArchiveEntry entry)
        {
            if (entry.Length > MaxAttachmentSize)
                throw new InvalidDataException($"'{entry.FullName}' is too big. Uncompressed ByteSize={entry.Length}");

            using (var reader = new BinaryReader(entry.Open()))
            {
                return new Attachment
                {
                    Path = entry.FullName,
                    ByteSize = entry.Length,
                    LastWriteTime = entry.LastWriteTime,
                    Content = reader.ReadBytes((int) entry.Length)
                };
            }
        }
    }
}
