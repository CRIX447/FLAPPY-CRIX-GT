using System;
using UnityEngine;

namespace FlappyCrix
{
    /// <summary>Common surface for the website (WebFlappyGame) and the native fallback (NativeFlappyGame).</summary>
    public interface IFlappyGame : IDisposable
    {
        string ModeName { get; }

        /// <summary>True once the game can take input.</summary>
        bool IsReady { get; }

        /// <summary>True if this mode has failed and the controller should fall back.</summary>
        bool HasFailed { get; }
        string FailureReason { get; }

        /// <summary>Texture to show on the panel, or null if the mode draws its own geometry.</summary>
        Texture PanelTexture { get; }

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

        /// <summary>Called every frame by the controller.</summary>
        void Tick();

        event Action<string> Log;
    }
}
