using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlappyCrix.Web
{
    /// <summary>
    /// Result of the in-game WebView test. It is measured inside Gorilla Tag, from the
    /// real engine, after the page has loaded - not inferred from files existing.
    /// </summary>
    public sealed class SelfTestReport
    {
#pragma warning disable 649 // filled by JsonUtility
        [Serializable]
        private class Raw
        {
            public bool js, css, fontLoaded, birdSprite, canvas;
            public int imagesLoaded = -1, imagesFailed, audioFetched, canvasColours, inputKeys, inputClicks, fps;
            public string mp3 = "", webm = "", screen = "", error = "";
        }
#pragma warning restore 649

        public string Mode;
        public string RawJson;
        public float EngineStartSeconds;
        public int UnityTextureFps;
        public bool TextureChanging;
        public bool Passed;
        public string FailureSummary = "";

        private Raw r = new Raw();
        private readonly List<string> lines = new List<string>();
        private readonly List<string> critical = new List<string>();

        public void Parse(string json)
        {
            try { r = JsonUtility.FromJson<Raw>(json) ?? new Raw(); }
            catch (Exception e) { r = new Raw { error = "unreadable report: " + e.Message }; }
        }

        public void Evaluate()
        {
            lines.Clear(); critical.Clear();
            lines.Add("==== Flappy Crix WebView self test (" + Mode + ") ====");
            Row("Engine started and page loaded", true, EngineStartSeconds.ToString("0.0") + " s", false);
            Row("JavaScript executes (game functions present)", r.js, null, true);
            Row("CSS applied", r.css, null, false);
            Row("Website font loaded", r.fontLoaded, null, false);
            Row("Local images load", r.imagesLoaded > 0 && r.imagesFailed == 0, r.imagesLoaded + " ok, " + r.imagesFailed + " failed", false);
            Row("Bird sprite decoded", r.birdSprite, null, true);
            Row("Audio files load", r.audioFetched > 0, r.audioFetched + " files", false);
            Row("Engine can decode MP3", r.mp3 == "probably" || r.mp3 == "maybe", "'" + r.mp3 + "'", false);
            Row("Canvas renders", r.canvas && r.canvasColours >= 2, r.canvasColours + " colours sampled", true);
            Row("Unity texture receives frames", TextureChanging || UnityTextureFps > 0, UnityTextureFps + " fps", true);
            Row("Unity key input reaches page", r.inputKeys > 0, r.inputKeys + " key events", true);
            Row("Page frame rate acceptable (>= 30)", r.fps >= 30, r.fps + " fps", false);
            if (!string.IsNullOrEmpty(r.error)) lines.Add("  page error: " + r.error);
            Passed = critical.Count == 0;
            FailureSummary = string.Join(", ", critical.ToArray());
            lines.Add(Passed ? "RESULT: PASS - website mode is working." : "RESULT: FAIL - " + FailureSummary);
        }

        private void Row(string name, bool ok, string detail, bool isCritical)
        {
            lines.Add((ok ? "  [PASS] " : isCritical ? "  [FAIL] " : "  [WARN] ") + name + (detail != null ? "  (" + detail + ")" : ""));
            if (!ok && isCritical) critical.Add(name);
        }

        public IEnumerable<string> Lines() => lines;
    }
}
