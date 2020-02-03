using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Common
{
    public class ZipLoader
    {
        public static bool TryParse(byte[] dataBytes, out ZipWithAttachments result)
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

                    var attachments = fileEntries
                        .Where(e => !rootEntries.Contains(e))
                        .ToList();

                    if (rootEntries.Count != 1 || !attachments.Any())
                    {
                        result = null;
                        return false;
                    }

                    var pluginDataEntry = rootEntries.First();

                    result = new ZipWithAttachments
                    {
                        PluginDataBytes = ReadAllEntryBytes(pluginDataEntry),
                        Attachments = attachments
                            .Select(entry => new Attachment{ Path = entry.FullName, ByteSize = entry.Length})
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

        private static byte[] ReadAllEntryBytes(ZipArchiveEntry entry)
        {
            if (entry.Length > int.MaxValue)
                throw new InvalidDataException($"'{entry.FullName}' is too big. Uncompressed ByteSize={entry.Length}");

            using (var reader = new BinaryReader(entry.Open()))
            {
                return reader.ReadBytes((int)entry.Length);
            }
        }
    }
}
