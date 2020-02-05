using System.Collections.Generic;
using FieldDataPluginFramework.Results;

namespace Common
{
    public class ParseFileResultWithAttachments
    {
        public ParseFileResult Result { get; set; }
        public List<Attachment> Attachments { get; set; }
    }
}
