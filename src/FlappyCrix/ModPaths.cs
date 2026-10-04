// Where the mod keeps what it writes: browser profiles (your saved progress on the site),
// the downloaded website engine, and selftest.txt. %LOCALAPPDATA%\FlappyCrix is always
// writable (the game itself may be under Program Files) and survives reinstalling the mod.
// No UnityEngine references. Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;
using System.IO;

namespace FlappyCrix
{
    public static class ModPaths
    {
        /// <summary>%LOCALAPPDATA%\FlappyCrix, or the mod folder's BrowserData if that can't be used.</summary>
        public static string DataRoot(string modFolder)
        {
            try
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(local))
                {
                    string d = Path.Combine(local, "FlappyCrix");
                    Directory.CreateDirectory(d);
                    return d;
                }
            }
            catch { }
            return Path.Combine(modFolder, "BrowserData");
        }
    }
}
