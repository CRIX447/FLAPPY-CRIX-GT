using System;
using UnityEngine;

namespace FlappyCrix
{
    /// <summary>
    /// Common surface for the in-game version (NativeFlappyGame, the default) and the website
    /// engines (hidden Edge/Chrome, UnityWebBrowser).
    /// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).
    /// </summary>
    public interface IFlappyGame : IDisposable
    {
        string ModeName { get; }

        /// <summary>True once the game can take input.</summary>
        bool IsReady { get; }

        /// <summary>True if this mode has failed and the controller should try the next one.</summary>
        bool HasFailed { get; }
        string FailureReason { get; }

        /// <summary>The picture for the screen (null until the first frame).</summary>
        Texture PanelTexture { get; }

        /// <summary>Which part of PanelTexture to show (material UV scale and offset).</summary>
        Vector2 TextureScale { get; }
        Vector2 TextureOffset { get; }

        /// <summary>Width / height of the game's picture.</summary>
        float Aspect { get; }

        /// <summary>"menu", "playing", "dead" or "paused".</summary>
        string Screen { get; }
        int Score { get; }

        void Flap();
        void Restart();
        void TogglePause();
        void Back();

        /// <summary>Arcade deck joystick: move the menu highlight. dx -1/+1 = left/right, dy +1/-1 = up/down.</summary>
        void Navigate(int dx, int dy);
        /// <summary>Arcade deck SELECT: activate the highlighted menu item (flaps while playing).</summary>
        void Select();
        /// <summary>Arcade deck START: start from the menu, retry from game over, resume from pause.</summary>
        void StartButton();

        /// <summary>Pointer on the panel in 0..1 UV, origin top-left.</summary>
        void PointerMove(Vector2 uv);
        void PointerDown(Vector2 uv);
        void PointerUp(Vector2 uv);
        void Scroll(Vector2 uv, int delta);

        /// <summary>The screen was opened (true) or hidden (false). Hidden = silent.</summary>
        void SetVisible(bool visible);

        /// <summary>Called every frame by the controller.</summary>
        void Tick();

        event Action<string> Log;
    }
}
