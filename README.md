# Multi-Ghost Replays for Star Drift Evolution

[![MelonLoader](https://img.shields.io/badge/Dependency-MelonLoader-green?style=flat-square&logo=github)](https://github.com/LavaGang/MelonLoader)

**Multi-Ghost Replays** is a MelonLoader mod for *Star Drift Evolution* that allows players to batch download, cache, synchronize, and view up to **333 leaderboard ghosts** simultaneously in replays across Quick Race (Time Trial) and Daily Challenge modes.

---

## Features

- **Massive Multi-Ghost Support:** Select, download, and render up to **333 player ghosts** simultaneously directly from an extended in-game leaderboard browser.
- **Batch Leaderboard Loading:** Easily load entries in customizable batches (**+50, +100, +200, +500**) with automatic bounds checking that locks pagination when all track entries are fetched.
- **Country & Flag Filtering:** Filter leaderboard entries globally or by specific country flags using a dedicated modal flag picker, or by clicking directly on any player's flag within the table.
- **Flexible Batch Selection Controls:** Instantly select (`✓`) or deselect (`X`) entries, reset country filters to Global (`G`), and use convenient range-selection buttons to check or uncheck all entries up to a specific row. All batch viewing and downloading actions dynamically respect active country filters.
- **Automated Local Caching System:** Replays are automatically stored locally as high-performance JSON files to avoid redundant downloads and dramatically speed up replay loading times.
- **UTC-Based Daily Challenge Rollover:** Daily Challenge replays are safely isolated in a dedicated `Daily` cache folder with a global **07:00 UTC** auto-reset rollover mechanism for synchronized day shifts worldwide.
- **Audio Overflow Warning:** Displays a visual warning (`[!] AUDIO OVERFLOW` with a custom red glow) and safety tooltips when selecting over 166 ghosts, alerting users that exceeding Unity's audio engine channel limits may cause sound effects to stutter, glitch, or turn completely silent.
- **Tire Skidmarks & Dynamic Surface Color:** Renders ground skidmarks for ghost vehicles with surface color mapping based on track terrain.
- **Ghost Headlights Support:** Renders active headlights on ghost vehicles during night-track replays for enhanced immersion.
- **Custom Visual Toggles:** Toggle visual effects on demand (including tire smoke trails, night-track headlights, and ground skidmarks), all enabled by default.
- **Synchronized Playback:** Master timeline synchronization ensures all active ghosts remain aligned during play, pause, restart, and high-speed timeline scrubbing.
- **High-Performance List Virtualization:** The UI table utilizes list culling and virtualization, maintaining smooth high FPS even when managing thousands of loaded entries.
- **Toggleable Interface:** Easily hide or show the mod interface at any time during gameplay by pressing **F5**.

---

## Requirements

- [**Star Drift Evolution**](https://store.steampowered.com/app/1005400/Star_Drift_Evolution/)
- **[MelonLoader](https://github.com/LavaGang/MelonLoader) v0.5.7 (x86 / 32-bit version)**

---

## Installation

### Step 1: Install MelonLoader v0.5.7 (x86)
Because *Star Drift Evolution* runs on a 32-bit architecture, **you must use the x86 version of MelonLoader v0.5.7**.

1. Download **MelonLoader.Installer.exe** and the **LavaGang.MelonLoader.x86.zip** archive from the official [MelonLoader v0.5.7 Release Page](https://github.com/LavaGang/MelonLoader/releases?page=2#release-v0.5.7).
2. Run `MelonLoader.Installer.exe`.
3. Select the **Manual Zip** tab.
4. As the **Unity Game**, select **Star Drift Evolution** from your Steam directory:  
   `...\Steam\steamapps\common\Star Drift Evolution\Star Drift Evolution.exe`
5. As the **"Zip Archive"**, select the `LavaGang.MelonLoader.x86.zip` file you downloaded.
6. Click **Install**.

### Step 2: Install Multi-Ghost Replays
1. Download the latest version of `MultiGhostReplays.dll` from the [Releases](https://github.com/gustavocafirme/MultiGhostReplays/releases) page in this repository.
2. Move `MultiGhostReplays.dll` into the `Mods` folder inside your *Star Drift Evolution* installation directory:  
   `...\Steam\steamapps\common\Star Drift Evolution\Mods\MultiGhostReplays.dll`
3. Launch the game!

---

## Updating

### Update Multi-Ghost Replays
1. Download the latest version of `MultiGhostReplays.dll` from the [Releases](https://github.com/gustavocafirme/MultiGhostReplays/releases) page in this repository.
2. Move `MultiGhostReplays.dll` into the `Mods` folder inside your *Star Drift Evolution* installation directory:  
   `...\Steam\steamapps\common\Star Drift Evolution\Mods\MultiGhostReplays.dll`, replacing the older version of the dll.
3. Launch the game!

---

## How to Use

1. Navigate to either **Quick Race (Time Trial)** or **Daily Challenge**.
2. Make sure the replay option in the track selection menu is set to **REPLAY: LEADERBOARD**.
3. Click the Leaderboard button on the right side of the screen to open up the **Leaderboard Replay Browser**.
4. Select all the replays you wish to view through the checkboxes and batch buttons.
5. Click **VIEW WITH SELECTED**.
6. Wait for it to download the replay tracks and enter the replay view afterwards.
7. Watch all selected ghosts simultaneously!

---

## Controls

| Key | Function |
| :--- | :--- |
| **F5** | Toggle Mod UI Visibility |

---

## Credits & License

- Developed by **Gustavo Costa Alves Firme**
- Powered by [MelonLoader](https://github.com/LavaGang/MelonLoader)
