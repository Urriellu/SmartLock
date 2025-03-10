using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SmartLock.Tests;

[TestClass]
public class SmartLockTests
{
    [TestMethod]
    public void T01_PatientLockTest()
    {
        try { Thread.CurrentThread.Name = nameof(T01_PatientLockTest); } catch { }

        SmartLocker locker = new();

        bool isLockedByMain = false;

        Task t = null;

        int amountwarns = 0;
        SmartLocker.OnLockTimedOut += (err) => Assert.Fail();
        SmartLocker.OnLockDelayed += (err) =>
        {
            Debug.WriteLine(err);
            amountwarns++;
        };

        // lock it by main thread
        locker.HardLock(() =>
        {
            isLockedByMain = true;

            t = Task.Factory.StartNew(() =>
            {
                Thread.CurrentThread.Name = $"Background locker";

                Assert.IsTrue(isLockedByMain);
                // wait until the main thread
                Stopwatch sw = Stopwatch.StartNew();
                locker.PatientLock(() =>
                {
                    Assert.IsFalse(isLockedByMain);
                }, TimeSpan.FromSeconds(1)); // we request one warning per second
                TimeSpan waited = sw.Elapsed;
                Assert.IsTrue(waited > TimeSpan.FromSeconds(4));
            });

            Task.Delay(TimeSpan.FromSeconds(5)).Wait();
            isLockedByMain = false;
        });

        Assert.IsFalse(isLockedByMain);
        Assert.IsTrue(amountwarns >= 4 && amountwarns <= 5); // one warning per second

        t.Wait(); // wait until background thread has finished

        Assert.AreEqual(locker.AmountLazyLocks, 0);
        Assert.AreEqual(locker.AmountSuccessfulHardLocks, 1);
        Assert.IsTrue(locker.HardLockTotalWait == locker.HardLockAverageWait);
        Assert.IsTrue(locker.HardLockTotalWait < TimeSpan.FromSeconds(1));
        Assert.IsTrue(locker.HardLockLongestWait < TimeSpan.FromSeconds(1));
        Assert.AreEqual(locker.AmountPatientLocks, 1);
        Assert.IsTrue(locker.PatientLockTotalWait == locker.PatientLockAverageWait);
        Assert.IsTrue(locker.PatientLockAverageWait > TimeSpan.FromSeconds(4));
        Assert.IsTrue(locker.PatientLockLongestWait > TimeSpan.FromSeconds(4));

        SmartLocker.ClearEvents();
    }

    [TestMethod]
    public void T02_HardAndLazyLocksTimeoutTest()
    {
        try { Thread.CurrentThread.Name = nameof(T02_HardAndLazyLocksTimeoutTest); } catch { }

        int amounttimeouts = 0;
        SmartLocker.OnLockTimedOut += (err) =>
        {
            Debug.WriteLine(err.Msg);
            amounttimeouts++;
        };
        SmartLocker.OnLockDelayed += (err) => Assert.Fail();

        SmartLocker locker = new();

        bool testfinished = false;
        AutoResetEvent waitForeverStarted = new(false);
        AutoResetEvent waitForever = new(false);

        Task taskLockForever = Task.Factory.StartNew(() =>
        {
            Thread.CurrentThread.Name = "Lock forever";
            locker.HardLock(() =>
            {
                waitForeverStarted.Set();
                waitForever.WaitOne(); // wait until the end of the test
                Assert.IsTrue(testfinished);
            });
        });
        Assert.IsTrue(waitForeverStarted.WaitOne(TimeSpan.FromSeconds(5)));

        bool lazylockcodeexecuted = false;
        locker.LazyLock(() =>
        {
            Debug.WriteLine($"Executing code even after lock was not acquired on time.");
            lazylockcodeexecuted = true;
        }, runAnywayAfter: TimeSpan.FromSeconds(3));
        Assert.IsTrue(lazylockcodeexecuted == true);

        bool hardlockexceptionthrown = false;
        bool? hardlockcodeexecuted = null;
        try
        {
            locker.HardLock(() =>
            {
                Assert.Fail($"This code should never be executed because we expect this lock to time out.");
                hardlockcodeexecuted = true;
            }, dieafter: TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"As expected, the hard lock timed out and threw an exception: {ex.Message}.");
            hardlockexceptionthrown = true;
        }
        Assert.IsTrue(hardlockcodeexecuted != true);
        Assert.IsTrue(hardlockexceptionthrown);

        Assert.AreEqual(1, locker.AmountSuccessfulHardLocks);
        Assert.IsTrue(locker.HardLockTotalWait == locker.HardLockAverageWait);
        Assert.IsTrue(locker.HardLockAverageWait < TimeSpan.FromSeconds(1));
        Assert.IsTrue(locker.HardLockLongestWait < TimeSpan.FromSeconds(1));

        Assert.AreEqual(locker.AmountPatientLocks, 0);

        Assert.AreEqual(locker.AmountLazyLocks, 1);
        Assert.IsTrue(locker.LazyLockTotalWait == locker.LazyLockAverageWait);
        Assert.IsTrue(locker.LazyLockAverageWait > TimeSpan.FromSeconds(2));
        Assert.IsTrue(locker.LazyLockLongestWait > TimeSpan.FromSeconds(2));

        Assert.AreEqual(amounttimeouts, 2);

        // let's stop the "wait-forever" task and lock...
        testfinished = true;
        waitForever.Set();
        Assert.IsTrue(taskLockForever.Wait(TimeSpan.FromSeconds(3)));

        SmartLocker.ClearEvents();
    }

    [TestMethod]
    public void T03_MultilockTest()
    {
        try { Thread.CurrentThread.Name = nameof(T03_MultilockTest); } catch { }

        int amountwarns = 0;
        SmartLocker.OnLockTimedOut += (err) => Assert.Fail();
        SmartLocker.OnLockDelayed += (err) =>
        {
            Debug.WriteLine(err);
            amountwarns++;
        };

        AutoResetEvent firstLockEntered = new(false);
        AutoResetEvent secondLockEntered = new(false);
        SmartLocker lock1 = new();
        SmartLocker lock2 = new();

        bool mainWaitedAndExecuted = false;
        bool mainreleased = false;

        Task t1 = Task.Factory.StartNew(() =>
        {
            Thread.CurrentThread.Name = "First locking thread";
            lock1.HardLock(() =>
            {
                firstLockEntered.Set();
                Task.Delay(TimeSpan.FromSeconds(5)).Wait(); // keep it locked for 5 seconds
                Assert.IsFalse(mainWaitedAndExecuted);
                Assert.IsFalse(mainreleased);
            });
        });

        Task t2 = Task.Factory.StartNew(() =>
        {
            Thread.CurrentThread.Name = "Second locking thread";
            lock2.HardLock(() =>
            {
                secondLockEntered.Set();
                Task.Delay(TimeSpan.FromSeconds(5)).Wait(); // keep it locked for 5 seconds
                Assert.IsFalse(mainWaitedAndExecuted);
                Assert.IsFalse(mainreleased);
            });
        });

        // wait until the locks have been acquired
        firstLockEntered.WaitOne(TimeSpan.FromSeconds(5));
        secondLockEntered.WaitOne(TimeSpan.FromSeconds(5));

        // now try to acquire, but I will not be able to for a while
        Stopwatch sw = Stopwatch.StartNew();
        lock1.Combine(lock2).PatientLock(() =>
        {
            mainWaitedAndExecuted = true;
        }, TimeSpan.FromSeconds(1));

        mainreleased = true;

        TimeSpan waited = sw.Elapsed;
        Assert.IsTrue(amountwarns > 3 * 2 && amountwarns < 7 * 2);
        Assert.IsTrue(waited > TimeSpan.FromSeconds(3) && waited < TimeSpan.FromSeconds(7));
        Assert.IsTrue(mainWaitedAndExecuted);

        Task.WaitAll(t1, t2); // wait until all background tasks finished

        SmartLocker.ClearEvents();
    }



    [TestMethod]
    public void T04_ReentryTest()
    {
        try { Thread.CurrentThread.Name = nameof(T04_ReentryTest); } catch { }

        SmartLocker.OnLockTimedOut += (err) => Assert.Fail();
        SmartLocker.OnLockDelayed += (err) => Debug.WriteLine(err);

        int blocksExecuted = 0;

        SmartLocker locker = new();
        Debug.WriteLine($"Going to enter first lock...");
        locker.HardLock(() =>
        {
            Debug.WriteLine($"Entered first lock");
            blocksExecuted++;
            locker.HardLock(() =>
            {
                Debug.WriteLine($"Entered second lock");
                blocksExecuted++;
                locker.HardLock(() =>
                {
                    Debug.WriteLine($"Entered third lock");
                    blocksExecuted++;
                });
                Debug.WriteLine($"Left third lock");
            });
            Debug.WriteLine($"Left second lock");
        });
        Debug.WriteLine($"Left all locks");

        Assert.AreEqual(3, blocksExecuted);
    }

    [TestMethod, Timeout(60 * 1000)]
    public void T05_CancellationTokenTest()
    {
        try { Thread.CurrentThread.Name = nameof(T05_CancellationTokenTest); } catch { }

        int delayCount = 0;
        SemaphoreSlim semWaitUntilDelayed = new(0, 1);

        SmartLocker.OnLockTimedOut += (err) => Assert.Fail();
        SmartLocker.OnLockDelayed += (err) =>
        {
            Debug.WriteLine(err);
            Interlocked.Increment(ref delayCount);
            semWaitUntilDelayed.Release();
        };

        SmartLocker locker = new();
        SemaphoreSlim semTestFinished = new(0, 1);
        CancellationTokenSource cts2 = new();

        SemaphoreSlim semWaitUntilLocked = new(0, 1);
        bool firstLockEntered = false;
        Task t1 = Task.Factory.StartNew(() =>
        {
            Thread.CurrentThread.Name = "First locking thread, waiting forever";
            Thread.Sleep(TimeSpan.FromSeconds(1));
            locker.PatientLock(() =>
            {
                firstLockEntered = true;
                semWaitUntilLocked.Release();
                semTestFinished.Wait();
            }, TimeSpan.FromSeconds(5));
        });
        Assert.IsFalse(firstLockEntered);

        semWaitUntilLocked.Wait(); // wait until the first thread has locked the object

        SemaphoreSlim semWaitUntilSecondThreadEntered = new(0, 1);
        SemaphoreSlim semWaitUntilSecondThreadCanceled = new(0, 1);
        bool secondLockEntered = false;
        Task t2 = Task.Factory.StartNew(() =>
        {
            Thread.CurrentThread.Name = "Second locking thread, waiting forever";
            try
            {
                locker.PatientLock(() =>
            {
                Assert.IsTrue(firstLockEntered);
                secondLockEntered = true;
                semWaitUntilSecondThreadEntered.Release();
                Assert.Fail("This code should never be executed, because we should have canceled the lock.");
            }, TimeSpan.FromSeconds(5), cts2.Token);
            }
            catch (OperationCanceledException)
            {
                semWaitUntilSecondThreadCanceled.Release();
            }
        });

        Task.Delay(TimeSpan.FromSeconds(5)).Wait(); // wait until the second thread has tried to lock
        Assert.IsTrue(firstLockEntered);
        Assert.IsFalse(secondLockEntered);

        semWaitUntilDelayed.Wait(); // wait until the second thread has been delayed
        cts2.Cancel(); // stop waiting on the second thread
        semWaitUntilSecondThreadCanceled.Wait(); // wait until the second thread has been actually canceled
        Assert.AreEqual(0, semWaitUntilSecondThreadEntered.CurrentCount);
        semTestFinished.Release(); // release the first thread

        Task.WaitAll(t1, t2); // wait until all background tasks finished
        Assert.AreNotEqual(0, delayCount);
        Assert.AreEqual(0, semWaitUntilSecondThreadEntered.CurrentCount);
    }

}
