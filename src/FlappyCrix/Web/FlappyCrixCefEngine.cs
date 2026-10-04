using System;
using System.Collections.Generic;
using System.IO;
using VoltstroStudios.UnityWebBrowser.Core.Engines;
using VoltstroStudios.UnityWebBrowser.Shared.Core;

namespace FlappyCrix.Web
{
    /// <summary>
    /// Points UnityWebBrowser at the CEF engine shipped inside the mod folder
    /// (BepInEx/plugins/FlappyCrix/UWB/UnityWebBrowser.Engine.Cef.exe) instead of the
    /// default "&lt;Game&gt;_Data/UWB/" location a normal Unity build would use.
    /// Nothing is ever written into the Gorilla Tag install outside BepInEx/.
    /// </summary>
    public sealed class FlappyCrixCefEngine : Engine
    {
        public const string ExeName = "UnityWebBrowser.Engine.Cef.exe";

        /// <summary>Absolute folder holding the engine. Set before the browser is initialised.</summary>
        public string EngineFolder;

        public override string GetEngineExecutableName() => ExeName;

        public override string GetEngineWorkingPath(Platform platform) => Path.GetFullPath(EngineFolder);

        public override string GetEngineAppPath(Platform platform) => Path.GetFullPath(Path.Combine(EngineFolder, ExeName));

        public override IEnumerable<EnginePlatformFiles> EngineFiles => new[]
        {
            new EnginePlatformFiles { platform = Platform.Windows64, engineBaseAppLocation = "", engineRuntimeLocation = "" }
        };

#pragma warning disable 809, 672
        [Obsolete]
        public override string EngineFilesNotFoundError => null;
#pragma warning restore 809, 672
    }
}
