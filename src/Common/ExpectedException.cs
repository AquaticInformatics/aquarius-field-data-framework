using System;

namespace Common
{
    public class ExpectedException : Exception
    {
        public ExpectedException(string message)
            : base(message)
        {
        }
    }
}
