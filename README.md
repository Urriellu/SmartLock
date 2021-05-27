# SmartLock

Multi-threading synchronization library for .NET which supports timed locks (optionally executing locked code upon time out), warnings on potential deadlocks (locks that take too long), and statistics on lock acquisition.

[![NuGet version (SmartLock)](https://img.shields.io/nuget/v/SmartLock.svg?style=flat-square)](https://www.nuget.org/packages/SmartLock/)

## Usage
- See [Sample Program](SmartLock.Sample/Program.cs)

## Features

- Easy to use.
- Multiplatform (platform agnostic).
- Patient Locks: Similar to a normal lock, but warns the user when the lock is taking too long.
- Hard Locks: Lock that throws an exception when it times out.
- Lazy Locks: Lock that executes the locked code even if the lock fails to get acquired within the given time out.
- All locking mechanism support returning values.
- Easily combine multiple locker objects and lock/wait on them at the same time.

## Links

- [Repository](https://github.com/Urriellu/SmartLock)
- [NuGet Package](https://www.nuget.org/packages/SmartLock)
- [License](LICENSE.md)
