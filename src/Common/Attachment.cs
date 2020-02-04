using System;

namespace Common
{
    public class Attachment
    {
        public string Path { get; set; }
        public byte[] Content { get; set; }
        public DateTimeOffset LastWriteTime { get; set; }
        public long ByteSize { get; set; }
    }
}
