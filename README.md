# Poly Car

A low-poly rally game for Windows PC. Originally made for the Microsoft Virtual Hackathon (2022);
remastered for Unity 6 in 2026 with a rally driving model, rivals on the clock, ghosts, three new tracks and a
diorama main menu. Handling, rivals, camera and menu follow techniques studied in art of rally (our own code).

## Play

| Action | Keyboard | Gamepad |
|---|---|---|
| Accelerate / brake and reverse | W S or Up Down | RT / LT |
| Steer | A D or Left Right | Left stick / d-pad |
| Handbrake (drift) | Space | A / RB |
| Boost | Shift (or N) | X / LB |
| Respawn on track | R | Y |
| Camera (rally / rally far / chase / hood), look back | C, Q | Back, R3 |
| Pause | Esc or P | Start |
| Quick restart | T | |

Drifting fills the boost meter, and drift points build a combo that banks after a short pause (a crash loses it).
Medals (bronze, silver, gold) on each track earn stars, and stars unlock cars in the garage.

## Modes

- **Race**: any track, 0-7 rivals (0 = time trial against your ghost), laps, difficulty.
- **Championship**: a rally over all four tracks against five rivals; the lowest total time wins.

Rivals don't drive on the road (as in art of rally): each rival's stage time is a baseline just under the gold
time x (1 + a deviation drawn from their difficulty band and talent), plus the odd mistake (spin, puncture...).
The HUD shows your live position against their pace and the gap to the rival ahead at every split.

## Tracks

- **Green Forest** (gravel): the original 4.1 km point-to-point forest stage.
- **Sakura Ring** (tarmac): 1.7 km lakeside circuit with cherry blossoms, torii gates and Mount Fuji (3 laps by default).
- **Frost Peak** (packed snow): 1.7 km mountain loop through spruce forest (3 laps by default).
- **Dust Canyon** (gravel): 3.2 km desert circuit through mesas (2 laps by default).

Each surface has its own grip and rolling drag (art of rally's surface table: gravel ~10 % and snow ~14 % less grip
than tarmac, off-road much less), and loose surfaces throw dust coloured by the ground. Stages are dressed like
rally stages: knockable marker poles every 20 m, crowds behind tape and hay bales on the big corners.

## Cars

Stallion GT, Outlaw 01 and Scoop Van are unlocked from the start. Interceptor (2 stars), Apex S (3),
Vortex R (5) and Zenith X (7) unlock with medals. Each has its own top speed, acceleration, grip, steering and drift.

## Features

- Rally car on raycast wheels: spring + two-stage damper suspension with anti-roll bars; Magic-Formula-style tyres
  whose side grip stays flat past the peak (slides can be held), slip that lags over a relaxation length, and a
  friction circle (throttle steers the rear); rear-biased four-wheel drive with a viscous coupling, rear-only
  handbrake, ABS, five-speed automatic. Arcade helpers on top: yaw from the raw steering input, a stability torque
  against big slides, a brake helper, air levelling and a landing stick. Speed-scaled steering with a counter-steer
  boost; on keyboard the wheels return slowly while sliding so a drift holds.
- `Poly Car > Debug > Handling Lab` runs scripted launch / braking / cornering / drift / lane-change tests on every car.
- Rally camera: high telephoto view that turns with the car, fades its follow out in spins, pulls in when terrain
  blocks the view and widens the lens slightly with speed.
- Main menu: a floating diorama of the selected track with your car on its road, seen through a long lens with
  tilt-shift depth of field; the camera glides between a shot per screen.
- Best-run ghosts, checkpoint split times against your record, lap and race records, medals and car unlocks.
- Auto-respawn when you are lost (off the map, on your roof, stuck on a bank), wrong-way warning.
- Layered engine audio (four RPM layers, gear shifts, backfires), tyre squeal, wind, music.
- Look: flat-colour palette, exponential fog fed into the sky, ACES grade with blue-lifted shadows, depth of
  field focused on the car and large-radius ambient occlusion on High and Ultra.
- Settings: resolution, window mode, vsync, FPS cap, quality preset, render scale, motion blur, volumes,
  units, camera, shake, FPS counter, progress reset.

## What's new in 2.0 (2026 remaster)

- Upgraded from Unity 2021.2 to Unity 6.6 (URP 17, Render Graph, Input System, UI Toolkit).
- New driving model, AI rivals, ghosts, splits, medals, unlockable cars, drift scoring and boost.
- Three new circuits (Sakura Ring, Frost Peak, Dust Canyon) built by a procedural map generator; Green Forest fixed
  (it had no respawn points, a broken finish trigger and a duplicated script that stopped compilation).
- Removed duplicate asset folders whose copied `.meta` files shared GUIDs (Unity was silently re-mapping
  car models, URP quality assets and post-processing profiles), and the PlayFab login that signed every
  player into the same account.
- Smooth on PC: on an Apple M5 at 1080p High every track holds about 110-120 fps with a 99th-percentile
  frame time under 16 ms (`-benchmark` mode, see below).

## Build

Open the project in **Unity 6000.6.0f1** (with Windows Build Support), then use the menu
**Poly Car > Build > Windows (64-bit)**. The player goes to `Builds/Windows/PolyCar.exe`.

Batch mode:

```
Unity -batchmode -quit -projectPath <path> -executeMethod PolyCar.Editor.SceneBuilders.BuildWindows -logFile build.log
```

Launch options: `-map <green_forest|sakura_ring|frost_peak|dust_canyon>` (skip the menu), `-car <id>`,
`-autopilot` (the AI drives your car), `-benchmark` (60 s autopilot run, writes `benchmark.txt` to the save
folder and quits), `-benchmarkSeconds <n>`, `-mute`, `-keyboardbot` (AI with keyboard-style inputs),
`-smoketest -autopilot` (clicks through the real menus, races every track and a championship, writes
`smoketest.txt` and quits).

## Project layout

- `Assets/PolyCar/Scripts`: runtime code (`PolyCar.Runtime` assembly): `Car/`, `Race/`, `UI/`, `Core/`.
- `Assets/PolyCar/Editor`: setup tools under the **Poly Car** menu: car factory, map catalog, Green Forest
  conversion, menu scene and its dioramas (`DioramaBuilder`), the procedural `MapBuilder` for the circuits,
  the Handling Lab, Windows build.
- `Assets/PolyCar/Resources`: catalogs, UI stylesheet and panel, audio, credits.
- `Assets/PolyCar/Maps`: generated circuit scenes and their meshes.
- `Assets/_Scenes`: `Menu` and `Green Forest`.
- `Assets/PolyCar/Tests/EditMode`: core logic tests (medals, records, track maths, ghosts, catalogs, rival times,
  championship standings) and scene integrity tests.

## Credits

See the in-game Credits screen (`Assets/PolyCar/Resources/Credits.txt`). Engine, tyre and effect sounds are CC0
(Freesound / OpenGameArt / Kenney); music is under the Pixabay Content License.
