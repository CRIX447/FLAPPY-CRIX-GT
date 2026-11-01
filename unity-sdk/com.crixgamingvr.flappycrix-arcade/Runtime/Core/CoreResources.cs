// Where the core finds its baked font and pictures. Inside Unity, ArcadeGame points this at
// Resources/CrixArcade/*.bytes; outside Unity (tests) the files are read from a folder.
// No UnityEngine references.

using System;
using System.IO;

namespace CrixArcade.Core
{
    public static class CoreResources
    {
        /// <summary>Returns the bytes of "ui-font" or "sprites", or null.</summary>
        public static Func<string, byte[]> Load = name =>
        {
            string folder = Environment.GetEnvironmentVariable("CRIX_ARCADE_RESOURCES");
            if (string.IsNullOrEmpty(folder)) return null;
            string path = Path.Combine(folder, name + ".bytes");
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        };
    }
}
