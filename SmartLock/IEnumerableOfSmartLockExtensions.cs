using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SmartLock
{
    /// <summary>Extension methods for <see cref="IEnumerable{T}"/> of <see cref="SmartLocker"/>.</summary>
    public static class IEnumerableOfSmartLockExtensions
    {
        /// <summary>Combine multiple <see cref="SmartLocker"/> objects to be able to lock all of them at the same time.</summary>
        public static SmartMultiLocker CombinedLocker(this IEnumerable<SmartLocker> lockObjs) => new SmartMultiLocker(lockObjs.ToArray());
    }
}
