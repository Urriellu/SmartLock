using System;

namespace SmartLock;

/// <summary>
/// Represents an error that occurs when a <see cref="SmartLocker"/> Hard Lock times out.
/// </summary>
public class HardLockTimeoutException : Exception
{
    /// <summary>
    /// Initialize a new Hard Lock Timeout Exception.
    /// </summary>
    /// <param name="msg"></param>
    public HardLockTimeoutException(string msg) : base(msg) { }

    /// <summary>
    /// Initialize a new Hard Lock Timeout Exception.
    /// </summary>
    public HardLockTimeoutException(string msg, Exception innerException) : base(msg, innerException) { }
}
