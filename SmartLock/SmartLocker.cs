using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace SmartLock;

/// <summary>
/// Creates an object used to "lock" blocks of code, that is, pieces of code that cannot be executed at the same by different threads.
/// </summary>
public class SmartLocker
{
    private readonly object lockobj = new object();

    /// <summary>
    /// <see cref="Thread.ManagedThreadId"/> of the <see cref="Thread"/> that is currently keeping this object locked and is blocking other threads from executing.
    /// </summary>
    public int? LockingThreadID { get; private set; } = null;

    /// <summary>Stack trace that is holding this lock.</summary>
    public string HoldingTrace { get; private set; } = "";

    /// <summary>
    /// Last time a thread successfully obtained this lock.
    /// </summary>
    public DateTime? HeldSince { get; private set; } = null;

    /// <summary>
    /// Amount of time a thread has been holding this lock.
    /// </summary>
    public TimeSpan? HeldFor => DateTime.Now - HeldSince;

    /// <summary>
    /// Event triggered when a <see cref="HardLock(Action, TimeSpan?)"/> or a <see cref="LazyLock(Action, TimeSpan?)"/> has timed out and therefore the inner code will be either executed (in case of a Lazy Lock) or an exception is about to be thrown (in case of a Hard Lock).
    /// </summary>
    public static event Action<(SmartLocker TimedOutLockObj, string Msg)> OnLockTimedOut;

    /// <summary>
    /// Event triggered when a <see cref="PatientLock(Action, TimeSpan?)"/> is taking too long to acquire a lock, but it's still waiting.
    /// </summary>
    public static event Action<(SmartLocker DelayedLockObj, string Msg)> OnLockDelayed;

    internal static void ClearEvents() => OnLockTimedOut = OnLockDelayed = null;

    /// <summary>
    /// Default amount of time that this <see cref="SmartLocker"/> object will wait to acquire a lock before failing, executing anyway, or warning the user.
    /// </summary>
    public TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Amount of Lazy Locks that have been acquired or skipped (that is, whose blocks of code have been executed). This includes both clean locks as well as expired locks. This does not include lock requests that are still waiting to be acquired.</summary>
    public Int64 AmountLazyLocks { get; private set; } = 0;

    /// <summary>Maximum time awaited by a Lazy Lock before being acquired, out of all the times a Lazy Lock has been requested.</summary>
    public TimeSpan LazyLockLongestWait { get; private set; } = TimeSpan.Zero;

    /// <summary>Cumulative time awaited by all Lazy Lock requests.</summary>
    public TimeSpan LazyLockTotalWait { get; private set; } = TimeSpan.Zero;

    /// <summary>Average time taken by Lazy Lock requests to acquire a lock or to expire.</summary>
    public TimeSpan LazyLockAverageWait => AmountLazyLocks > 0 ? LazyLockTotalWait / AmountLazyLocks : TimeSpan.Zero;

    /// <summary>Amount of Lazy Locks that have timed out and their blocks of code executed regardless.</summary>
    public Int64 AmountLazyLocksTimedOut { get; private set; } = 0;

    /// <summary>Amount of Patient Lock requests that have fully acquired a lock.</summary>
    public Int64 AmountPatientLocks { get; private set; } = 0;

    /// <summary>Maximum time awaited by a Patient Lock before being acquired.</summary>
    public TimeSpan PatientLockLongestWait { get; private set; } = TimeSpan.Zero;

    /// <summary>Cumulative time awaited by all Patient Lock requests.</summary>
    public TimeSpan PatientLockTotalWait { get; private set; } = TimeSpan.Zero;

    /// <summary>Average time taken by Patient Lock requests to acquire a lock.</summary>
    public TimeSpan PatientLockAverageWait => AmountPatientLocks > 0 ? PatientLockTotalWait / AmountPatientLocks : TimeSpan.Zero;

    /// <summary>Amount of times Patient Locks have been delayed. This is counted every time a "warning timeout" expires, meaning that a single Patient Lock that waits for a long time and expires multiple times gets counted multiple times.</summary>
    public Int64 AmountPatientLocksDelayed { get; private set; } = 0;

    /// <summary>Amount of Hard Lock requests that have fully acquired a lock.</summary>
    public Int64 AmountSuccessfulHardLocks { get; private set; } = 0;

    /// <summary>Maximum time awaited by a Hard Lock before being acquired.</summary>
    public TimeSpan HardLockLongestWait { get; private set; } = TimeSpan.Zero;

    /// <summary>Total amount of time waited to acquire successfully-acquired Hard Locks. This does not count timed out (failed) Hard Locks.</summary>
    public TimeSpan HardLockTotalWait { get; private set; } = TimeSpan.Zero;

    /// <summary>Average time awaited for successfully-acquired Hard Locks. This does not count timed out (failed) Hard Locks.</summary>
    public TimeSpan HardLockAverageWait => AmountSuccessfulHardLocks > 0 ? HardLockTotalWait / AmountSuccessfulHardLocks : TimeSpan.Zero;

    /// <summary>Amount of times a Hard Lock has timed out, thrown an exception, and its code block skipped.</summary>
    public Int64 AmountHardLocksTimedOut { get; private set; } = 0;

    /// <summary>Total amount of locks requested.</summary>
    public Int64 AmountLocks => AmountLazyLocks + AmountPatientLocks + AmountSuccessfulHardLocks;

    /// <summary>Total amount of time waiting to acquire locks.</summary>
    public TimeSpan TotalWait => LazyLockTotalWait + PatientLockTotalWait + HardLockTotalWait;

    /// <summary>Average time awaited before executing locked blocks of code. This includes expired Lazy Locks (because their code gets executed anyway) but does NOT include expired expired Hard Locks (because their code doesn't get executed).</summary>
    public TimeSpan AverageWait => AmountLocks > 0 ? TotalWait / AmountLocks : TimeSpan.Zero;

    private readonly object statsLocker = new object();

    /// <summary>
    /// Tries to acquire a lock for a given amount of time (or the <see cref="DefaultTimeout"/> default time), but if the lock is not acquired the event <see cref="OnLockTimedOut"/> is triggered and the code executes anyway.
    /// </summary>
    /// <param name="action">Code to be executed only after acquiring the lock (or timing out).</param>
    /// <param name="runAnywayAfter">The code will be executed after waiting for this amount of time even if it fails to lock.</param>
    public void LazyLock(Action action, TimeSpan? runAnywayAfter = null)
    {
        runAnywayAfter ??= DefaultTimeout;
        try
        {
            Enter(runAnywayAfter.Value, keepretrying: false, throwexception: false);
            action.Invoke();
        }
        finally { Exit(); }
    }

    /// <summary>
    /// Tries to acquire a lock for a given amount of time (or the <see cref="DefaultTimeout"/> default time), but if the lock is not acquired the event <see cref="OnLockTimedOut"/> is triggered and the code executes anyway.
    /// </summary>
    /// <typeparam name="T">Type returned by the action to be executed within the lock.</typeparam>
    /// <param name="action">Code to be executed only after acquiring the lock (or timing out).</param>
    /// <param name="runAnywayAfter">The code will be executed after waiting for this amount of time even if it fails to lock.</param>
    public T LazyLock<T>(Func<T> action, TimeSpan? runAnywayAfter = null)
    {
        T ret;
        runAnywayAfter ??= DefaultTimeout;
        try
        {
            Enter(runAnywayAfter.Value, keepretrying: false, throwexception: false);
            ret = action.Invoke();
        }
        finally { Exit(); }

        return ret;
    }

    /// <summary>
    /// Tries to acquire a lock for as long as needed, but shows a warning every time it times out (or the <see cref="DefaultTimeout"/> default time).
    /// </summary>
    /// <param name="action">Code to be executed only after acquiring the lock (or timing out).</param>
    /// <param name="warnevery">Executes the <see cref="OnLockDelayed"/> every time it times out without acquiring the lock.</param>
    public void PatientLock(Action action, TimeSpan? warnevery = null)
    {
        warnevery ??= DefaultTimeout;
        try
        {
            Enter(warnevery.Value, keepretrying: true, throwexception: false);
            action.Invoke();
        }
        finally { Exit(); }
    }

    /// <summary>
    /// Tries to acquire a lock for as long as needed, but shows a warning every time it times out (or the <see cref="DefaultTimeout"/> default time).
    /// </summary>
    /// <param name="action">Code to be executed only after acquiring the lock (or timing out).</param>
    /// <param name="warnevery">Executes the <see cref="OnLockDelayed"/> every time it times out without acquiring the lock.</param>
    public T PatientLock<T>(Func<T> action, TimeSpan? warnevery = null)
    {
        T ret;
        warnevery ??= DefaultTimeout;
        try
        {
            Enter(warnevery.Value, keepretrying: true, throwexception: false);
            ret = action.Invoke();
        }
        finally { Exit(); }

        return ret;
    }

    /// <summary>
    /// Tries to acquire a lock for a given amount of time (or the <see cref="DefaultTimeout"/> default time) or throws an exception if unable to obtain it.
    /// </summary>
    /// <param name="action">Code to be executed only after (and if) acquiring the lock.</param>
    /// <param name="dieafter">Throws an exception if the lock is not acquired in this time.</param>
    public void HardLock(Action action, TimeSpan? dieafter = null)
    {
        dieafter ??= DefaultTimeout;
        try
        {
            Enter(dieafter.Value, keepretrying: false, throwexception: true);
            action.Invoke();
        }
        finally { Exit(); }
    }

    /// <summary>
    /// Try to enter a Hard Lock which instead of dying it automatically tries to acquire the lock again. This behavior in practice is similar to a Patient Lock, but if the Action inside this Hard Lock contains nested Hard Locks and those Hard Locks time out, this "Hard Lock With Retries" will try to acquire the nested Hard Locks again. 
    /// </summary>
    /// <remarks>This is used for creating multi-locks while avoiding race conditions between different <see cref="SmartLocker"/> objects. When you need to acquire multiple locks at the same time you should use a <see cref="HardLockWithRetries"/> first and nest inside it as many <see cref="HardLock"/> as you need.</remarks>
    /// <param name="action"></param>
    /// <param name="retryAfter"></param>
    public void HardLockWithRetries(Action action, TimeSpan retryAfter)
    {
        bool retry = true;
        while (retry)
        {
            try
            {
                retry = false;
                HardLock(action, retryAfter);
            }
            catch (HardLockTimeoutException) { retry = true; }
            catch (AggregateException ex)
            {
                Exception e = ex;
                while (!(e is HardLockTimeoutException) && !(e.InnerException is null)) e = e.InnerException;
                if (e is HardLockTimeoutException)
                    retry = true;
                else
                    throw;
            }
        }
    }

    /// <summary>
    /// Tries to acquire a lock for a given amount of time (or the <see cref="DefaultTimeout"/> default time) or throws an exception if unable to obtain it.
    /// </summary>
    /// <param name="action">Code to be executed only after (and if) acquiring the lock.</param>
    /// <param name="dieafter">Throws an exception if the lock is not acquired in this time.</param>
    public T HardLock<T>(Func<T> action, TimeSpan? dieafter = null)
    {
        T ret;
        dieafter ??= DefaultTimeout;
        try
        {
            Enter(dieafter.Value, keepretrying: false, throwexception: true);
            ret = action.Invoke();
        }
        finally { Exit(); }

        return ret;
    }

    void Enter(TimeSpan timeout, bool keepretrying, bool throwexception)
    {
        Stopwatch sw = Stopwatch.StartNew();
        bool locked;
        do
        {
            locked = Monitor.TryEnter(lockobj, timeout);
            if (locked)
                sw.Stop();
            else
            {
                if (throwexception)
                {
                    lock (statsLocker) AmountHardLocksTimedOut++;
                    OnLockTimedOut?.Invoke((this, $"Unable to acquire lock for {sw.Elapsed.TotalSeconds:N0} seconds, requested by:{Environment.NewLine}{GetStackTrace()}{Environment.NewLine}Being held for too long by:{Environment.NewLine}{HoldingTrace}{Environment.NewLine}Failing..."));
                    throw new HardLockTimeoutException($"Unable to acquire lock for {sw.Elapsed.TotalSeconds:N0} seconds, requested by:{Environment.NewLine}{GetStackTrace()}{Environment.NewLine}Being held for too long by:{Environment.NewLine}{HoldingTrace}{Environment.NewLine}.");
                }
                else if (keepretrying)
                {
                    lock (statsLocker) AmountPatientLocksDelayed++;
                    OnLockDelayed?.Invoke((this, $"Unable to acquire lock for {sw.Elapsed.TotalSeconds:N0} seconds, requested by:{Environment.NewLine}{GetStackTrace()}{Environment.NewLine}Being held for too long by:{Environment.NewLine}{HoldingTrace}{Environment.NewLine}Retrying..."));
                }
                else
                {
                    lock (statsLocker) AmountLazyLocksTimedOut++;
                    OnLockTimedOut?.Invoke((this, $"Unable to acquire lock for {sw.Elapsed.TotalSeconds:N0} seconds, requested by:{Environment.NewLine}{GetStackTrace()}{Environment.NewLine}Being held for too long by:{Environment.NewLine}{HoldingTrace}{Environment.NewLine}Executing anyway..."));
                }
            }
        } while (!locked && keepretrying);

        lock (statsLocker)
        {
            if (keepretrying)
            {
                // STATS FOR PATIENT LOCKER
                AmountPatientLocks++;
                if (PatientLockLongestWait < sw.Elapsed) PatientLockLongestWait = sw.Elapsed;
                PatientLockTotalWait += sw.Elapsed;
            }
            else if (throwexception)
            {
                // STATS FOR HARD LOCKER
                AmountSuccessfulHardLocks++;
                if (HardLockLongestWait < sw.Elapsed) HardLockLongestWait = sw.Elapsed;
                HardLockTotalWait += sw.Elapsed;
            }
            else
            {
                // STATS FOR LAZY LOCKER
                AmountLazyLocks++;
                if (LazyLockLongestWait < sw.Elapsed) LazyLockLongestWait = sw.Elapsed;
                LazyLockTotalWait += sw.Elapsed;
            }
        }

        //save a stack trace for the code that is holding the lock
        HoldingTrace = GetStackTrace();
        LockingThreadID = Thread.CurrentThread.ManagedThreadId;
        HeldSince = DateTime.Now;
    }

    /// <summary>
    /// List of namespaces to be skipped when storing and displaying a locking stack trace.
    /// </summary>
    /// <remarks>
    /// It is recommended to list all external libraries that your program uses which create threads/tasks which eventually hold <see cref="SmartLocker"/> objects inside your code. For example, if you use a third-party WebSocket Server, this server will probably spawn a thread per user request, and if you don't add here the WebSocket server namespace, your stack trace logging will be filled with internal WebSocket Server method calls.
    /// </remarks>
    public static readonly List<string> NamespacesToIgnoreInStackTrace = new List<string>();

    static string GetStackTrace()
    {
        StackTrace trace = new StackTrace();
        string threadID = Thread.CurrentThread.Name ?? "(unnamed thread)";
        List<string> traceLines = trace.ToString().Replace("\r\n", "\n").Replace("\n\r", "\n").Replace("\r", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(L => !L.Contains(typeof(SmartLocker).FullName)).ToList();
        while (traceLines.Count > 0 && (traceLines.Last().TrimStart().StartsWith("at System.") || NamespacesToIgnoreInStackTrace.Any(n => traceLines.Last().TrimStart().StartsWith($"at {n}.")))) traceLines.RemoveAt(traceLines.Count - 1); // remove not-my-code stracktrace
        return "[" + threadID + "]" + Environment.NewLine + string.Join(Environment.NewLine, traceLines);
    }

    void Exit()
    {
        try { Monitor.Exit(lockobj); }
        catch
        {
            // this typically happens when a Lazy Lock has not been acquired and therefore the code executed anyway
        }
    }

    /// <summary>Combine multiple <see cref="SmartLocker"/> objects like this one to be able to lock all of them at the same time.</summary>
    public SmartMultiLocker Combine(params SmartLocker[] otherSmartLocks) => new SmartMultiLocker(new SmartLocker[] {this}.Concat(otherSmartLocks).ToArray());
}
