using System;

namespace SmartLock
{
    public class HardLockTimeoutException : Exception
    {
        public HardLockTimeoutException(string msg) : base(msg) { }

        public HardLockTimeoutException(string msg, Exception innerException) : base(msg, innerException) { }
    }
}
