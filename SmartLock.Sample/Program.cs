using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SmartLock.Sample
{
    class Program
    {
        static void Main(string[] args)
        {
            Thread.CurrentThread.Name = $"Main thread [{Thread.CurrentThread.ManagedThreadId}]";

            SmartLocker.OnLockDelayed += (e) => Debug.WriteLine(e.Msg);
            SmartLocker.OnLockTimedOut += (e) => Debug.WriteLine(e.Msg);

            SmartLocker lockobj = new SmartLocker();

            // Let's create a second thread that simply tries to acquire the lock and print a message...
            bool finished = false;
            Task.Factory.StartNew(() =>
            {
                Thread.CurrentThread.Name = "Second thread";
                while (!finished)
                {
                    lockobj.PatientLock(() =>
                    {
                        Debug.WriteLine("Background thread looping...");
                        Task.Delay(TimeSpan.FromSeconds(1)).Wait();
                    });
                }
            });

            // A "Patient Lock" is almost identical to a standard lock(). It patiently waits FOREVER until the lock is acquired. Nonetheless, it will periodically warn the user that the lock is taking too long to get acquired
            lockobj.PatientLock(() =>
            {
                Debug.WriteLine("This code only executes after the lock has been acquired, and all other locks are blocked while this block of code runs. Just as a normal lock.");
            }, warnevery: TimeSpan.FromSeconds(1)); // every second that we are waiting before acquiring the lock

            lockobj.PatientLock(() => Debug.WriteLine("This code only executes after the lock has been acquired.")); // default warning ("lockobj.DefaultTimeout") is 5 seconds

            // A "Hard Lock" behaves as a normal lock, but if we are unable to acquire it within the given time it throws an exception and the code does not get executed.
            lockobj.HardLock(() => Debug.WriteLine($"This code only executes if we were able to acquire the lock within 7 seconds"), dieafter: TimeSpan.FromSeconds(7));

            // A "Lazy Lock" waits for the given amount of time, but if we are unable to acquire the lock, the code gets executed anyway and the lock is ignore.
            // BE VERY CAREFUL USING LAZY LOCKS! THIS WILL BREAK MULTI-THREADING SYNCHRONIZATION
            lockobj.LazyLock(() => Debug.WriteLine($"This code always executes, even if the lock couldn't be acquired within 20 seconds"), runAnywayAfter: TimeSpan.FromSeconds(20));

            // ==============================================================

            // You can also return values from within your lock statements...
            int someresult = lockobj.PatientLock(() => 123456789);

            // ==============================================================

            // You can also have a block of code that only executes after acquiring multiple locks...
            SmartLocker anotherlocker = new SmartLocker();
            lockobj.Combine(anotherlocker).PatientLock(() => Debug.WriteLine($"This code is probably blocking multiple other threads, the ones locked by both '{nameof(lockobj)}' and '{nameof(anotherlocker)}'."));

            // ==============================================================

            // You can block many threads, and these "combined locks" can also return values

            List<SmartLocker> manylockers = new List<SmartLocker>();
            for (int i = 0; i < 20; i++) // let's create a bunch of locker objects and threads
            {
                SmartLocker oneMoreLocker = new SmartLocker();
                manylockers.Add(oneMoreLocker);
                Task.Factory.StartNew(() =>
                {
                    // each thread will lock one locker object
                    Thread.CurrentThread.Name = $"Many-lockers #{i}";
                    oneMoreLocker.PatientLock(() => Task.Delay(10)); // each locker object gets locked for 10 seconds
                });
            }

            (string Some, string Complex, string Return, string Value) = manylockers.CombinedLocker().PatientLock(() =>
            {
                Debug.WriteLine($"This code is being blocked by many other locks running in many other threads");
                return ("some", "complext", "return", "value");
            });

            // ==============================================================

            // We also collect some statistics...
            Debug.WriteLine($"This object has been lazy-locked {lockobj.AmountLazyLocks} times, out of which {lockobj.AmountLazyLocksTimedOut} timed out, waited a total of {lockobj.LazyLockTotalWait.TotalMilliseconds}ms and an average of {lockobj.LazyLockAverageWait.TotalMilliseconds}ms.");
            Debug.WriteLine($"This object has been hard-locked {lockobj.AmountSuccessfulHardLocks} times, out of which {lockobj.AmountHardLocksTimedOut} timed out, waited a total of {lockobj.HardLockTotalWait.TotalMilliseconds}ms and an average of {lockobj.HardLockAverageWait.TotalMilliseconds}ms.");
            Debug.WriteLine($"This object has been patient-locked {lockobj.AmountPatientLocks} times, out of which {lockobj.AmountPatientLocksDelayed} got delayed, waited a total of {lockobj.PatientLockTotalWait.TotalMilliseconds}ms and an average of {lockobj.PatientLockAverageWait.TotalMilliseconds}ms.");
            Debug.WriteLine($"This object has been locked {lockobj.AmountLocks} times, waited a total of {lockobj.TotalWait.TotalMilliseconds}ms and an average of {lockobj.AverageWait.TotalMilliseconds}ms.");

            finished = true;
        }
    }
}
