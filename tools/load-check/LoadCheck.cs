// Loads FlappyCrix.dll the way a normal install has it - WITHOUT the optional
// UnityWebBrowser DLLs - and checks that the mod can still start:
//   1. the settings bind against the real BepInEx ConfigFile,
//   2. the controller's engine start-up runs through the missing engines without throwing,
//   3. every method outside the UWB-only classes compiles (Mono JIT).
// This catches the bug that hid the whole screen in the first 1.0.0 fix build: one direct
// call into a UWB class made Mono reject the entire start-up method.
// Run: tools/load-check/run.sh   (Mono; uses tools/refs/BepInEx.dll and the Unity stand-in)
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

static class LoadCheck
{
    // Classes that exist only for the optional engine; they are expected not to load without it.
    static readonly string[] UwbOnly = { "FlappyCrix.Web.UwbBrowserGame", "FlappyCrix.Web.FlappyCrixCefEngine", "FlappyCrix.Web.UniTaskBootstrap" };
    // The one method outside those classes that touches UWB on purpose: it is [NoInlining] and
    // only called after the UWB files were found, inside a try/catch.
    static readonly string[] UwbOnlyMethods = { "FlappyCrix.FlappyCrixController.CreateUwbGame" };

    static int failures;
    static void Check(bool ok, string what, string detail = "")
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + what + (detail.Length > 0 ? "  -- " + detail : ""));
        if (!ok) failures++;
    }

    static void SetPath(string name, string value) =>
        typeof(BepInEx.Paths).GetProperty(name, BindingFlags.Static | BindingFlags.Public).GetSetMethod(true).Invoke(null, new object[] { value });

    static int Main(string[] args)
    {
        string dll = Path.GetFullPath(args[0]);
        string dir = Path.GetDirectoryName(dll);
        Check(!File.Exists(Path.Combine(dir, "VoltstroStudios.UnityWebBrowser.dll")), "UnityWebBrowser DLLs absent (like a normal install)");
        var asm = Assembly.LoadFrom(dll);

        // 1. settings against the real BepInEx ConfigFile (BepInEx's paths are set up the way
        //    its preloader does it, for a pretend game folder)
        string game = Path.Combine(Path.GetTempPath(), "flappycrix-loadcheck-game-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(game, "BepInEx", "config"));
        SetPath("GameRootPath", game);
        SetPath("BepInExRootPath", Path.Combine(game, "BepInEx"));
        SetPath("ConfigPath", Path.Combine(game, "BepInEx", "config"));
        SetPath("BepInExConfigPath", Path.Combine(game, "BepInEx", "config", "BepInEx.cfg"));
        SetPath("PluginPath", Path.Combine(game, "BepInEx", "plugins"));
        // In the game BepInEx adds its Unity converters (Vector3 etc.) itself; do the same here.
        typeof(BepInEx.Paths).Assembly.GetType("BepInEx.Configuration.LazyTomlConverterLoader")
            .GetMethod("AddUnityEngineConverters", BindingFlags.Static | BindingFlags.Public).Invoke(null, null);
        string cfgPath = Path.Combine(Path.GetTempPath(), "flappycrix-loadcheck-" + Guid.NewGuid().ToString("N") + ".cfg");
        object config = null;
        try
        {
            var cfgFile = new BepInEx.Configuration.ConfigFile(cfgPath, true);
            config = Activator.CreateInstance(asm.GetType("FlappyCrix.FlappyCrixConfig"), cfgFile);
            Check(File.Exists(cfgPath) && File.ReadAllText(cfgPath).Contains("SettingsRevision = 6"), "settings bind against the real BepInEx ConfigFile and are written");
            var useWebsite = config.GetType().GetField("UseWebsite").GetValue(config);
            Check(!(bool)useWebsite.GetType().GetProperty("Value").GetValue(useWebsite), "the in-game version is the default (UseWebsite = false)");
        }
        catch (Exception e) { Check(false, "settings bind against the real BepInEx ConfigFile", (e.InnerException ?? e).ToString()); }
        finally { try { File.Delete(cfgPath); } catch { } }

        // 2. engine start-up: hidden browser not found here, UWB not installed -> must not throw to the caller
        if (config != null)
        {
            var ct = asm.GetType("FlappyCrix.FlappyCrixController");
            var c = Activator.CreateInstance(ct);
            ct.GetField("Config").SetValue(c, config);
            ct.GetField("Logger").SetValue(c, new BepInEx.Logging.ManualLogSource("load-check"));
            ct.GetField("ModFolder").SetValue(c, dir);
            var queue = (List<string>)ct.GetField("engineQueue", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(c);
            queue.Add("browsers"); queue.Add("uwb");
            try
            {
                ct.GetMethod("StartNextEngine", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(c, new object[] { null });
                Check(queue.Count == 0, "engine start-up skips the unavailable engines without throwing");
            }
            catch (TargetInvocationException e) { Check(false, "engine start-up skips the unavailable engines without throwing", e.InnerException.GetType().Name + ": " + e.InnerException.Message); }
        }

        // 3. the in-game version's pictures and font are inside the DLL, and it draws
        try
        {
            var sheet = asm.GetType("FlappyCrix.Native.SpriteSheet").GetMethod("LoadEmbedded").Invoke(null, null);
            int count = (int)sheet.GetType().GetProperty("Count").GetValue(sheet);
            var saveType = asm.GetType("FlappyCrix.Native.SaveData");
            var save = Activator.CreateInstance(saveType, new object[] { null });
            var appType = asm.GetType("FlappyCrix.Native.FlappyApp");
            var app = Activator.CreateInstance(appType, new object[] { save, sheet, 1, null, null });
            appType.GetMethod("Update").Invoke(app, new object[] { 0.016f });
            bool drew = (bool)appType.GetMethod("Render").Invoke(app, null);
            Check(count > 40 && drew, "the in-game version loads its pictures and font from the DLL and draws", count + " pictures");
        }
        catch (Exception e) { Check(false, "the in-game version loads its pictures and font from the DLL and draws", (e.InnerException ?? e).ToString()); }

        // 3b. the online parts that need data from the DLL: the site's chat filter, QR codes
        try
        {
            var filter = asm.GetType("FlappyCrix.Online.ChatFilter").GetMethod("Embedded").Invoke(null, null);
            int words = (int)filter.GetType().GetProperty("WordCount").GetValue(filter);
            string clean = (string)filter.GetType().GetMethod("Clean").Invoke(filter, new object[] { "what the fuck" });
            var qr = asm.GetType("FlappyCrix.Online.QrCode").GetMethod("Encode").Invoke(null, new object[] { "https://crixgamingvr.com/link?code=ABC-123" });
            int size = (int)qr.GetType().GetField("Size").GetValue(qr);
            Check(words > 50 && clean == "what the ****" && size == 29, "the site's chat filter is built in, and QR codes encode", words + " words, QR " + size + "x" + size);
        }
        catch (Exception e) { Check(false, "the site's chat filter is built in, and QR codes encode", (e.InnerException ?? e).ToString()); }

        // 4. JIT every method (last, so step 2 sees the start-up method compiled for the first time, as in the game)
        Type[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
        int compiled = 0;
        var bad = new List<string>();
        foreach (var t in types)
        {
            bool uwbOnly = UwbOnly.Any(n => t.FullName == n || t.FullName.StartsWith(n + "+"));
            if (uwbOnly || t.IsGenericTypeDefinition || t.IsInterface) continue;
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            IEnumerable<MethodBase> methods;
            try { methods = t.GetMethods(all).Cast<MethodBase>().Concat(t.GetConstructors(all)).ToList(); }
            catch (Exception e) { bad.Add(t.FullName + ": " + e.GetType().Name + ": " + e.Message); continue; }
            foreach (var m in methods)
            {
                if (m.IsAbstract || m.ContainsGenericParameters || (m.MethodImplementationFlags & MethodImplAttributes.InternalCall) != 0) continue;
                if ((m.Attributes & MethodAttributes.PinvokeImpl) != 0) continue;
                if (UwbOnlyMethods.Contains(t.FullName + "." + m.Name)) continue;
                try { m.MethodHandle.GetFunctionPointer(); compiled++; }
                catch (Exception e) { bad.Add(t.FullName + "." + m.Name + ": " + e.GetType().Name + ": " + e.Message); }
            }
        }
        Check(bad.Count == 0, "every method outside the UWB-only classes compiles without UWB", compiled + " methods" + (bad.Count > 0 ? "\n     " + string.Join("\n     ", bad) : ""));

        try { Directory.Delete(game, true); } catch { }
        Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        return failures == 0 ? 0 : 1;
    }
}
