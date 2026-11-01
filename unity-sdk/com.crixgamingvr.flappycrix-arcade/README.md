# Flappy Crix Arcade (Unity SDK)

A playable **Flappy Crix arcade cabinet** for VR games and Gorilla Tag fan games. It's the in-game
version from the Flappy Crix Gorilla Tag mod, rebuilt as a drop-in Unity package and revamped
into a classic upright arcade machine: lit marquee, sloped screen, joystick, four big buttons and
a coin door, with neon edges.

- **Single player**: a real arcade machine. Attract mode when nobody's playing, free play or one
  coin per game, and a high-score table with three-letter initials entered on the joystick.
- **Multiplayer, without extra CCU**: cabinets that stand together can be linked. Players nearby
  press **RACE** and race each other on the same pipes, see each other as ghost birds, can watch a
  race that's already on, and see who's using which cabinet. High scores are shared. All of it goes
  through the room your game is already in, so it **never adds a player to your Photon CCU**.

Works with the Built-in pipeline and URP, Unity 2021.3 or newer, PC VR and Quest (Android).
Not for WebGL (the game runs on its own thread).

## Install

Package Manager → **+** → *Add package from git URL*:

```
https://github.com/CRIX447/FLAPPY-CRIX-GT.git?path=/unity-sdk/com.crixgamingvr.flappycrix-arcade
```

## Add a cabinet

1. **GameObject → CRIX → Flappy Crix Arcade Machine**. It appears where the scene view is looking.
   The blue arrow (Z) points to where the player stands. The menu also makes two materials in
   `Assets/CrixArcade` with your pipeline's shaders, so the cabinet keeps its looks in a build.
2. **VR hands**: put an **ArcadeHand** on each fingertip (or hand) of your *local* player.
   Gorilla Tag fan games: import the *Gorilla Tag Fan Game* sample and put **ArcadeFanGameHands**
   on your player rig; it finds the hands and adds the buzz.
3. Press Play. On a desktop: click the screen and buttons, or use the keyboard on the nearest
   cabinet (Space flap, Enter start, Backspace back, arrows move, Tab select, C coin).

Controls on the cabinet: **FLAP** (big red), **START** (green), **SELECT** (blue), **BACK**
(yellow), the **joystick** for the menus and initials, the **coin slot** (touch it) and the
**screen** itself (tap the menus).

## Multiplayer (races)

1. Use **GameObject → CRIX → Flappy Crix Arcade Machine (linked for races)**, or type the same
   **Link Group** (for example `arcade`) on every cabinet that stands in the same place.
2. Import the sample for your networking and add its setup component to the scene:
   - **Photon PUN 2** → `PunArcadeSetup` (uses `RaiseEvent`, event code 177).
   - **Photon Fusion 2** → `FusionArcadeSetup` next to your `NetworkRunner`.
   - Anything else (Normcore, Mirror, Netcode…): implement `IArcadeTransport` (send bytes to some
     players or everyone, raise `Received` / `PlayerLeft`) and call
     `ArcadeLink.Instance.SetTransport(yourTransport)`.

How it works:

| | |
|---|---|
| Who can race | Players standing within **Nearby Radius** (ArcadeLink, 12 m) of cabinets in the same link group who press RACE. |
| Start | Once two or more are in the lobby, the player with the lowest id sends one countdown with a shared seed: every racer gets the same pipes and coins. Power-ups and seasonal extras are off in races. |
| During the race | Each racer sends its bird ~8 times a second, only to the other racers and players near the cabinets. Others show as ghost birds with names. |
| Watching | Pressing RACE while a race is on nearby shows the best racer's run on your cabinet. |
| End | Everyone sees the result when the last racer crashes. |
| In use | A cabinet someone else is playing shows **IN USE** with their name and live score on your copy. |
| High scores | Shared with everyone in the room, saved on each player's device. |
| Network cost | About 1 small message a second per player, plus 8 a second per racer during a race. No extra connection, room or CCU. |

Testing a race alone: `LoopbackTransport` connects several pretend players in one process
(see `Tests~/ArcadeTest.cs`).

## Settings (ArcadeMachine)

| Setting | |
|---|---|
| Cabinet Id | Unique per cabinet, the same for every player (set by the menu). Names its save and score table. |
| Free Play | Off = one coin per game: players touch the coin slot first. |
| Attract After | Seconds alone on the menu before the attract screen. |
| Link Group | Empty = single player. Same text = these cabinets race each other. |
| Build Cabinet / Style | The model and its colours, glow, marquee text and size. Turn off to use your own model: set Screen Renderer and add ArcadeButton / ArcadeJoystick / ArcadeCoinSlot to your parts. |
| Add Colliders | Solid boxes so players can't walk through it; the panel and screen stay reachable. |
| Active Distance | Further than this from the camera, the screen stops drawing and goes quiet. |
| Frame Rate | Screen pictures per second (30 is plenty). |

Code: `machine.Press(ArcadeAction.Flap)`, `machine.Click(ray)` (laser pointers),
`machine.Game.App` (the game: `HighScores`, `Credits`, `InsertCoin()`…).

## Saves

Progress (coins, cosmetics, best score…) and each cabinet's high scores are saved in
`Application.persistentDataPath/CrixArcade/`.

## Tests (no Unity needed)

```
sh Tests~/run.sh               # the game, attract mode, coins, initials, a 2-player race, watching, IN USE
```

`Tests~/CabinetPreview.cs` + `render.py` build the cabinet with a stand-in UnityEngine and draw it.

## Credits & licences

- Flappy Crix game, art, sounds and music © CRIX — [crixgamingvr.com](https://crixgamingvr.com).
  You may use this package in your game; the Flappy Crix content stays CRIX's (see LICENSE.md).
- The SDK's code: MIT ([LICENSE.md](LICENSE.md)).
- UI font Bubble Sans (SIL OFL 1.1) and Noto Color Emoji (Apache 2.0): [Third Party Notices.md](Third%20Party%20Notices.md).
- Made with AI: Claude by Anthropic.

Not affiliated with Another Axiom. Gorilla Tag is a trademark of Another Axiom.
