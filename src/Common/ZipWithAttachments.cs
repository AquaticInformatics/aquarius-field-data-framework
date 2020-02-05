using System.Collections.Generic;

namespace Common
{
    public class ZipWithAttachments
    {
        public byte[] PluginDataBytes { get; set; }
        public List<Attachment> Attachments { get; set; }
    }
}
