using System;
using System.Runtime.CompilerServices;
using System.Threading;

#if NETFRAMEWORK
namespace System.Runtime.CompilerServices
{
    // the compiler only needs the attribute's name; net48 does not ship it
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute { }
}
#endif

namespace ASH.Server.Tests
{
    /// <summary>The server-call tests block on Task.Run with a few seconds' limit (film worker, previews, uploads). With xUnit running classes
    /// in parallel, the thread pool adds threads slowly, so on a busy CI runner those tasks started after their own deadline
    /// and a different one of them failed each release. Enough ready threads from the start take the queueing out of the measurement.</summary>
    internal static class ThreadPoolWarmup
    {
        [ModuleInitializer]
        internal static void Init()
        {
            ThreadPool.GetMinThreads(out var worker, out var io);
            ThreadPool.SetMinThreads(Math.Max(worker, 64), Math.Max(io, 64));
        }
    }
}
