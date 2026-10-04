using System;
using System.Reflection;

namespace FlappyCrix.Web
{
    /// <summary>
    /// UnityWebBrowser awaits UniTask continuations (UniTask.WaitUntil,
    /// ReturnToMainThread). UniTask hooks itself into Unity's player loop from a
    /// [RuntimeInitializeOnLoadMethod] - which Unity only runs for assemblies that
    /// were part of the original build, never for DLLs BepInEx loads afterwards.
    /// Without this, the browser connects and then waits forever.
    ///
    /// PlayerLoopHelper.Init() is idempotent ("if (runners != null) return"), so it is
    /// safe even if Gorilla Tag already ships and initialised UniTask. Reflection keeps
    /// this mod free of a compile-time UniTask reference. Must run on the main thread.
    /// </summary>
    internal static class UniTaskBootstrap
    {
        private static bool done;

        public static void EnsureInitialised(Action<string> log)
        {
            if (done) return;
            done = true;
            Type helper = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                helper = asm.GetType("Cysharp.Threading.Tasks.PlayerLoopHelper", false);
                if (helper != null) break;
            }
            if (helper == null)
            {
                try { helper = Type.GetType("Cysharp.Threading.Tasks.PlayerLoopHelper, UniTask", false); } catch { }
            }
            if (helper == null)
            {
                log("UniTask not found - UnityWebBrowser's UniTask.dll must be in the mod folder.");
                return;
            }

            var isInjected = helper.GetMethod("IsInjectedUniTaskPlayerLoop", BindingFlags.Public | BindingFlags.Static);
            bool already = false;
            try { already = isInjected != null && (bool)isInjected.Invoke(null, null); } catch { }

            var init = helper.GetMethod("Init", BindingFlags.NonPublic | BindingFlags.Static);
            if (init == null)
            {
                log("UniTask PlayerLoopHelper.Init not found (unexpected UniTask version).");
                return;
            }
            try
            {
                init.Invoke(null, null);
                log(already ? "UniTask was already in the player loop." : "UniTask player loop initialised for UnityWebBrowser.");
            }
            catch (Exception e)
            {
                log("UniTask init failed: " + (e.InnerException ?? e).Message);
            }
        }
    }
}
