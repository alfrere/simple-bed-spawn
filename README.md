![Vintage Story](https://img.shields.io/badge/Vintage%20Story-1.22.x-green)
![Mod version](https://img.shields.io/badge/Mod%20Version-1.2.0-blue)

# Simple Bed Spawn

A simple Vintage Story mod that sets your spawn point when you sleep in a bed.

Beware, sleeping in a new bed or destroying it will move the spawnpoint!

## Installation

Drop `SimpleBedSpawn_v1.*.*.zip` into your `Mods/` folder.

## Configuration

On first run, the mod creates `ModConfig/SimpleBedSpawnConfig.json` in your game data folder. Options:

- `Enabled` — master on/off switch for the mod (default `true`).
- `VerboseLogging` — logs extra detail (bed positions, per-sleep events, save/load counts) to the server log (default `false`).
- `Messages.ShowSpawnSetMessage` / `Messages.SpawnSetMessage` — whether/what to tell a player when their spawn is set.
- `Messages.ShowSpawnLostMessage` / `Messages.SpawnLostMessage` — whether/what to tell a player when their bed is destroyed and their spawn is reset.

Edit the file and restart the server (or reload the world) to apply changes.

## Compatibility

- Vintage Story 1.22.0 and above.
- Should work with all vanilla beds and any modded bed using the standard seat system.

## Multiplayer

The mod is designed to work on multiplayer servers, but has not been tested yet.
If you encounter any issues, please open an issue or reach out directly via Vintage Story ModDB.