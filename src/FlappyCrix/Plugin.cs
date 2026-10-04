using System.IO;
using BepInEx;
using UnityEngine;

namespace FlappyCrix
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.crix.flappycrix";
        public const string Name = "Flappy Crix";
        public const string Version = "1.0.0";

        private FlappyCrixConfig config;
        private bool spawned;

        private void Awake()
        {
            config = new FlappyCrixConfig(Config);
            Logger.LogInfo(Name + " " + Version + " loaded from " + ModFolder);
            Logger.LogInfo("This mod was made with AI (Claude by Anthropic). Source: see README.md.");
        }

        /// <summary>Folder this DLL lives in: BepInEx/plugins/FlappyCrix/</summary>
        private string ModFolder => Path.GetDirectoryName(Info.Location);

        private void Update()
        {
            // Wait until the VR rig exists (the main camera is created with the player).
            if (spawned || Camera.main == null) return;
            spawned = true;

            var go = new GameObject("FlappyCrix");
            DontDestroyOnLoad(go);
            var c = go.AddComponent<FlappyCrixController>();
            c.Config = config;
            c.Logger = Logger;
            c.ModFolder = ModFolder;
        }
    }
}
