using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SmartLock
{
    /// <summary>
    /// Represents a combination of multiple <see cref="SmartLocker"/> locking objects, allowing to request locks on all of them at the same time.
    /// </summary>
    public class SmartMultiLocker
    {
        readonly List<SmartLocker> LockObjs;

        /// <summary>
        /// Instantiate a new group of <see cref="SmartLocker"/> objects so that we can lock on them at the same time.
        /// </summary>
        /// <param name="lockobjs">List of objects to be locked together</param>
        public SmartMultiLocker(params SmartLocker[] lockobjs)
        {
            this.LockObjs = lockobjs.Distinct().ToList();
        }

        /// <summary>
        /// Tries to acquire a lock for as long as needed, but shows a warning every time it times out (or the <see cref="SmartLocker.DefaultTimeout"/> default time).
        /// </summary>
        /// <param name="action">Code to be executed only after acquiring the lock (or timing out).</param>
        /// <param name="warnevery">Executes the <see cref="SmartLocker.OnLockDelayed"/> every time it times out without acquiring the lock.</param>
        public void PatientLock(Action action, TimeSpan? warnevery = null)
        {
            int acquired = 0;
            SemaphoreSlim readyToReleaseAll = new SemaphoreSlim(0);
            SemaphoreSlim smMaybeAllAlreadyAcquired = new SemaphoreSlim(0);
            void commonActionAfterEntering()
            {
                acquired++;
                smMaybeAllAlreadyAcquired.Release();
                readyToReleaseAll.Wait();
            }

            // Start acquiring locks...
            List<Task> acquiringTasks = new List<Task>();
            foreach (SmartLocker toLock in LockObjs)
            {
                acquiringTasks.Add(Task.Factory.StartNew(() =>
                {
                    Thread.CurrentThread.Name = $"{nameof(SmartMultiLocker)}: Obtain one lock for {toLock.HoldingTrace}";
                    toLock.PatientLock(commonActionAfterEntering, warnevery);
                }));
            }

            // Wait until all locks have been acquired
            while (acquired < LockObjs.Count) smMaybeAllAlreadyAcquired.Wait();

            // execute action
            action();

            // release all locks
            readyToReleaseAll.Release(int.MaxValue);

            Task.WaitAll(acquiringTasks.ToArray());
        }

        /// <summary>
        /// Tries to acquire a lock for as long as needed, but shows a warning every time it times out (or the <see cref="SmartLocker.DefaultTimeout"/> default time).
        /// </summary>
        /// <param name="action">Code to be executed only after acquiring the lock (or timing out).</param>
        /// <param name="warnevery">Executes the <see cref="SmartLocker.OnLockDelayed"/> every time it times out without acquiring the lock.</param>
        public T PatientLock<T>(Func<T> action, TimeSpan? warnevery = null)
        {
            int acquired = 0;
            SemaphoreSlim readyToReleaseAll = new SemaphoreSlim(0);
            SemaphoreSlim smMaybeAllAlreadyAcquired = new SemaphoreSlim(0);
            void commonActionAfterEntering()
            {
                acquired++;
                smMaybeAllAlreadyAcquired.Release();
                readyToReleaseAll.Wait();
            }

            // Start acquiring locks...
            List<Task> acquiringTasks = new List<Task>();
            foreach (var toLock in LockObjs)
            {
                acquiringTasks.Add(Task.Factory.StartNew(() =>
                {
                    Thread.CurrentThread.Name = $"{nameof(SmartMultiLocker)}: Obtain one lock for {toLock.HoldingTrace}";
                    toLock.PatientLock(commonActionAfterEntering, warnevery);
                }));
            }

            // Wait until all locks have been acquired
            while (acquired < LockObjs.Count) smMaybeAllAlreadyAcquired.Wait();

            // execute action
            T ret = action();

            // release all locks
            readyToReleaseAll.Release(int.MaxValue);

            Task.WaitAll(acquiringTasks.ToArray());

            return ret;
        }

        /// <summary>
        /// Tries to acquire a lock for a given amount of time (or the <see cref="SmartLocker.DefaultTimeout"/> default time), but if the lock is not acquired the event <see cref="SmartLocker.OnLockTimedOut"/> is triggered and the code executes anyway.
        /// </summary>
        /// <param name="action">Code to be executed only after acquiring the lock (or timing out).</param>
        /// <param name="runAnywayAfter">The code will be executed after waiting for this amount of time even if it fails to lock.</param>
        public void LazyLock(Action action, TimeSpan? runAnywayAfter = null)
        {
            int acquired = 0;
            SemaphoreSlim readyToReleaseAll = new SemaphoreSlim(0);
            SemaphoreSlim smMaybeAllAlreadyAcquired = new SemaphoreSlim(0);
            void commonActionAfterEntering()
            {
                acquired++;
                smMaybeAllAlreadyAcquired.Release();
                readyToReleaseAll.Wait();
            }

            // Start acquiring locks...
            List<Task> acquiringTasks = new List<Task>();
            foreach (var toLock in LockObjs)
            {
                acquiringTasks.Add(Task.Factory.StartNew(() =>
                {
                    Thread.CurrentThread.Name = $"{nameof(SmartMultiLocker)}: Obtain one lock for {toLock.HoldingTrace}";
                    toLock.LazyLock(commonActionAfterEntering, runAnywayAfter);
                }));
            }

            // Wait until all locks have been acquired
            while (acquired < LockObjs.Count) smMaybeAllAlreadyAcquired.Wait();

            // execute action
            action();

            // release all locks
            readyToReleaseAll.Release(int.MaxValue);

            Task.WaitAll(acquiringTasks.ToArray());
        }

        /// <summary>
        /// Tries to acquire a lock for a given amount of time (or the <see cref="SmartLocker.DefaultTimeout"/> default time), but if the lock is not acquired the event <see cref="SmartLocker.OnLockTimedOut"/> is triggered and the code executes anyway.
        /// </summary>
        /// <typeparam name="T">Type returned by the action to be executed within the lock.</typeparam>
        /// <param name="action">Code to be executed only after acquiring the lock (or timing out).</param>
        /// <param name="timeout">The code will be executed after waiting for this amount of time even if it fails to lock.</param>
        public T LazyLock<T>(Func<T> action, TimeSpan? timeout = null)
        {
            int acquired = 0;
            SemaphoreSlim readyToReleaseAll = new SemaphoreSlim(0);
            SemaphoreSlim smMaybeAllAlreadyAcquired = new SemaphoreSlim(0);
            void commonActionAfterEntering()
            {
                acquired++;
                smMaybeAllAlreadyAcquired.Release();
                readyToReleaseAll.Wait();
            }

            // Start acquiring locks...
            List<Task> acquiringTasks = new List<Task>();
            foreach (var toLock in LockObjs)
            {
                acquiringTasks.Add(Task.Factory.StartNew(() =>
                {
                    Thread.CurrentThread.Name = $"{nameof(SmartMultiLocker)}: Obtain one lock for {toLock.HoldingTrace}";
                    toLock.LazyLock(commonActionAfterEntering, timeout);
                }));
            }

            // Wait until all locks have been acquired
            while (acquired < LockObjs.Count) smMaybeAllAlreadyAcquired.Wait();

            // execute action
            T ret = action();

            // release all locks
            readyToReleaseAll.Release(int.MaxValue);

            Task.WaitAll(acquiringTasks.ToArray());

            return ret;
        }

        /// <summary>
        /// Tries to acquire a lock for a given amount of time (or the <see cref="SmartLocker.DefaultTimeout"/> default time) or throws an exception if unable to obtain it.
        /// </summary>
        /// <param name="action">Code to be executed only after (and if) acquiring the lock.</param>
        /// <param name="dieafter">Throws an exception if the lock is not acquired in this time.</param>
        public void HardLock(Action action, TimeSpan? dieafter = null)
        {
            int acquired = 0;
            SemaphoreSlim readyToReleaseAll = new SemaphoreSlim(0);
            SemaphoreSlim smMaybeAllAlreadyAcquired = new SemaphoreSlim(0);
            void commonActionAfterEntering()
            {
                acquired++;
                smMaybeAllAlreadyAcquired.Release();
                readyToReleaseAll.Wait();
            }

            // Start acquiring locks...
            List<Task> acquiringTasks = new List<Task>();
            foreach (var toLock in LockObjs)
            {
                acquiringTasks.Add(Task.Factory.StartNew(() =>
                {
                    Thread.CurrentThread.Name = $"{nameof(SmartMultiLocker)}: Obtain one lock for {toLock.HoldingTrace}";
                    toLock.HardLock(commonActionAfterEntering, dieafter);
                }));
            }

            // Wait until all locks have been acquired
            while (acquired < LockObjs.Count) smMaybeAllAlreadyAcquired.Wait();

            // execute action
            action();

            // release all locks
            readyToReleaseAll.Release(int.MaxValue);

            Task.WaitAll(acquiringTasks.ToArray());
        }

        /// <summary>
        /// Tries to acquire a lock for a given amount of time (or the <see cref="SmartLocker.DefaultTimeout"/> default time) or throws an exception if unable to obtain it.
        /// </summary>
        /// <param name="action">Code to be executed only after (and if) acquiring the lock.</param>
        /// <param name="dieafter">Throws an exception if the lock is not acquired in this time.</param>
        public T HardLock<T>(Func<T> action, TimeSpan? dieafter = null)
        {
            int acquired = 0;
            SemaphoreSlim readyToReleaseAll = new SemaphoreSlim(0);
            SemaphoreSlim smMaybeAllAlreadyAcquired = new SemaphoreSlim(0);
            void commonActionAfterEntering()
            {
                acquired++;
                smMaybeAllAlreadyAcquired.Release();
                readyToReleaseAll.Wait();
            }

            // Start acquiring locks...
            List<Task> acquiringTasks = new List<Task>();
            foreach (var toLock in LockObjs)
            {
                acquiringTasks.Add(Task.Factory.StartNew(() =>
                {
                    Thread.CurrentThread.Name = $"{nameof(SmartMultiLocker)}: Obtain one lock for {toLock.HoldingTrace}";
                    toLock.HardLock(commonActionAfterEntering, dieafter);
                }));
            }

            // Wait until all locks have been acquired
            while (acquired < LockObjs.Count) smMaybeAllAlreadyAcquired.Wait();

            // execute action
            T ret = action();

            // release all locks
            readyToReleaseAll.Release(int.MaxValue);

            Task.WaitAll(acquiringTasks.ToArray());

            return ret;
        }
    }
}