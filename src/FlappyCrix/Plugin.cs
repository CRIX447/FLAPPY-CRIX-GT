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
        private float nextWaitLog = 30f;

        private void Awake()
        {
            Logger.LogInfo(Name + " " + Version + " loaded from " + ModFolder);
            Logger.LogInfo("This mod was made with AI (Claude by Anthropic). Source: see README.md.");
            try { config = new FlappyCrixConfig(Config); }
            catch (System.Exception e)
            {
                Logger.LogError("Could not read the settings file (" + Config.ConfigFilePath + "): " + e +
                                " - delete that file and start the game again to recreate it.");
            }
        }

        /// <summary>Folder this DLL lives in: BepInEx/plugins/FlappyCrix/</summary>
        private string ModFolder => Path.GetDirectoryName(Info.Location);

        private void Update()
        {
            // Wait until the VR rig exists (the main camera is created with the player).
            if (spawned || config == null) return;
            if (Camera.main == null)
            {
                if (Time.realtimeSinceStartup > nextWaitLog)
                {
                    nextWaitLog = Time.realtimeSinceStartup + 30f;
                    Logger.LogInfo("Still waiting for the player's camera before opening the screen");
                }
                return;
            }
            spawned = true;
            Logger.LogInfo("Player camera found; starting Flappy Crix");

            var go = new GameObject("FlappyCrix");
            DontDestroyOnLoad(go);
            var c = go.AddComponent<FlappyCrixController>();
            c.Config = config;
            c.Logger = Logger;
            c.ModFolder = ModFolder;
        }
    }
}
