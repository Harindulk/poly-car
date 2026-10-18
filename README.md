# Poly Car

A low-poly arcade drift racer for Windows PC. Originally made for the Microsoft Virtual Hackathon (2022);
remastered for Unity 6 in 2026 with a new driving model, AI rivals, ghosts, three tracks and a full menu.

## Play

| Action | Keyboard | Gamepad |
|---|---|---|
| Accelerate / brake and reverse | W S or Up Down | RT / LT |
| Steer | A D or Left Right | Left stick / d-pad |
| Handbrake (drift) | Space | A / RB |
| Boost | Shift (or N) | X / LB |
| Respawn on track | R | Y |
| Camera (chase / near / hood), look back | C, Q | Back, R3 |
| Pause | Esc or P | Start |
| Quick restart | T | |

Drifting fills the boost meter, and drift points build a combo that banks after a short pause (a crash loses it).
Medals (bronze, silver, gold) on each track earn stars, and stars unlock cars in the garage.

## Tracks

- **Green Forest**: the original 4.1 km point-to-point mountain stage.
- **Sakura Ring**: 1.7 km lakeside circuit with cherry blossoms, torii gates and Mount Fuji (3 laps by default).
- **Dust Canyon**: 3.2 km desert circuit through mesas (2 laps by default).

## Cars

Stallion GT, Outlaw 01 and Scoop Van are unlocked from the start. Interceptor (2 stars), Apex S (3),
Vortex R (5) and Zenith X (7) unlock with medals. Each has its own top speed, acceleration, grip, steering and drift.

## Features

- Arcade raycast-suspension car: speed eases toward a target, the yaw rate follows the steering, and sideways
  slip decays with grip. Handbrake or full lock at speed drops into drift grip. Speed-scaled steering,
  quick counter-steer, a slide that holds when you let go, anti-spin assist, a landing "stick" and
  self-levelling in the air.
- Up to 5 AI rivals that drive from a precomputed corner and braking speed profile, with mild catch-up,
  three difficulties and stuck recovery.
- Best-run ghosts, checkpoint split times against your record, lap and race records, medals and car unlocks.
- Auto-respawn when you are lost (off the map, on your roof, stuck on a bank), wrong-way warning.
- Layered engine audio (four RPM layers, gear shifts, backfires), tyre squeal, wind, music.
- Settings: resolution, window mode, vsync, FPS cap, quality preset, render scale, motion blur, volumes,
  units, camera, shake, FPS counter, progress reset.

## Build

Open the project in **Unity 6000.6.0f1** (with Windows Build Support), then use the menu
**Poly Car > Build > Windows (64-bit)**. The player goes to `Builds/Windows/PolyCar.exe`.

Batch mode:

```
Unity -batchmode -quit -projectPath <path> -executeMethod PolyCar.Editor.SceneBuilders.BuildWindows -logFile build.log
```

Launch the build with `-autopilot` to let the AI drive your car (benchmark / attract mode).

## Project layout

- `Assets/PolyCar/Scripts`: runtime code (`PolyCar.Runtime` assembly): `Car/`, `Race/`, `UI/`, `Core/`.
- `Assets/PolyCar/Editor`: setup tools under the **Poly Car** menu: car factory, map catalog, Green Forest
  conversion, menu scene, the procedural `MapBuilder` for the circuits, Windows build.
- `Assets/PolyCar/Resources`: catalogs, UI stylesheet and panel, audio, credits.
- `Assets/PolyCar/Maps`: generated circuit scenes and their meshes.
- `Assets/_Scenes`: `Menu` and `Green Forest`.
- `Assets/PolyCar/Tests/EditMode`: core logic tests (medals, records, track maths, ghosts, catalogs).

## Credits

See the in-game Credits screen (`Assets/PolyCar/Resources/Credits.txt`). Engine, tyre and effect sounds are CC0
(Freesound / OpenGameArt / Kenney); music is under the Pixabay Content License.
