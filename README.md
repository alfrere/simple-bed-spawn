![Vintage Story](https://img.shields.io/badge/Vintage%20Story-1.22.x-green)
![Mod version](https://img.shields.io/badge/Mod%20Version-1.4.0-blue)

# Simple Bed Spawn

A simple Vintage Story mod that sets your spawn point when you sleep in a bed.

Beware, sleeping in a new bed or destroying it will move the spawnpoint!

## Installation

Drop `SimpleBedSpawn_v1.*.*.zip` into your `Mods/` folder.

## Configuration

On first run, the mod creates `ModConfig/SimpleBedSpawnConfig.json` in your game data folder. Options:

- `Enabled` — master on/off switch for the mod (default `true`).
- `VerboseLogging` — logs extra detail (bed positions, per-sleep events, save/load counts) to the server log (default `false`).
- `BedWhitelist` — list of bed block codes allowed to set the spawn point (default empty = every bed is allowed). Wildcards are supported. Vanilla beds are `game:bed-{wood|hay|woodaged|village}-*`, e.g. `["game:bed-woodaged-*", "game:bed-village-*"]` disables spawn setting for wood and hay beds.
- `AllowSharedBeds` — when `false`, a bed can be the spawn point of one player only: other players sleeping in it don't get their spawn set, and the owner keeps it until they set their spawn on another bed or the bed is destroyed (default `true`).
- `SetSpawnCooldown` — real time a player must wait after setting their spawn before they can set it on a different bed, written as `"minutes"`, `"minutes:seconds"` or `"hours:minutes:seconds"`, e.g. `"10"`, `"10:30"` (10 min 30 s) or `"1:30:00"` (1 h 30 min). Sleeping again in their current spawn bed is never blocked. A player's cooldown is cleared when their bed is destroyed, and all cooldowns are cleared when the server restarts (default `"0"` = no cooldown).
- `Messages.ShowSpawnSetMessage` / `Messages.SpawnSetMessage` — whether/what to tell a player when their spawn is set.
- `Messages.ShowSpawnLostMessage` / `Messages.SpawnLostMessage` — whether/what to tell a player when their bed is destroyed and their spawn is reset.
- `Messages.ShowBedNotAllowedMessage` / `Messages.BedNotAllowedMessage` — whether/what to tell a player who sleeps in a bed that isn't whitelisted.
- `Messages.ShowBedTakenMessage` / `Messages.BedTakenMessage` — whether/what to tell a player who sleeps in a bed that is already another player's spawn point (only with `AllowSharedBeds` set to `false`).
- `Messages.ShowCooldownMessage` / `Messages.CooldownMessage` — whether/what to tell a player who tries to change spawn bed during the cooldown. `{0}` is replaced by the remaining time, e.g. `18 seconds` or `10 minutes 30 seconds`.

Edit the file and restart the server (or reload the world) to apply changes.

## Compatibility

- Vintage Story 1.22.0 and above.
- Should work with all vanilla beds and any modded bed using the standard seat system.

## Multiplayer

The mod is designed to work on multiplayer servers, but has not been tested yet.
If you encounter any issues, please open an issue.