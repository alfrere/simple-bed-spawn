### 🛏️ v1.4.0

- Fixed spawn point not being reset when a bed is destroyed by explosions, world edits or other mods, not just by a player (#1)
- Added `AllowSharedBeds` config option: set it to `false` so a bed can only be one player's spawn point (#3)
  - Added `Messages.ShowBedTakenMessage` / `Messages.BedTakenMessage` to customize the message shown when a bed is already taken
- Added `SetSpawnCooldown` config option: real-time cooldown before a player can set their spawn on a different bed, e.g. `"10:30"` for 10 min 30 s; cleared when their bed is destroyed (#4)
  - Added `Messages.ShowCooldownMessage` / `Messages.CooldownMessage` to customize the message shown during the cooldown

### 🛏️ v1.3.0

- Added BedWhitelist option to restrict which beds can set the spawn point. Supports wildcards (e.g. "game:bed-woodaged-*"); empty by default, so every bed still works.
- Added Messages.BedNotAllowedMessage, shown when a player sleeps in a bed that isn't whitelisted, with a ShowBedNotAllowedMessage flag to disable it.

### 🛏️ v1.2.0

- Added config file support (ModConfig/SimpleBedSpawnConfig.json), auto-created on first run.
- Added Enabled option, a master on/off switch for the mod.
- Added VerboseLogging option, enabling detailed server-log output (bed positions, per-sleep events, save/load counts); off by default.
- Added customizable in-game messages: Messages.SpawnSetMessage and Messages.SpawnLostMessage, each with a Show* flag to disable it entirely.
- Log output is now quiet by default; previously the mod always logged bed saves/loads and spawn events regardless of need.

### 🛏️ v1.1.1

- Bed positions are now persisted across server restarts.
- Destroying a bed while its owner is offline now queues a spawn reset, applied on their next login.
- Destroying a bed now resets the spawn point for all players who had it set as their spawn, not just the first one.

### 🛏️ v1.1.0

- Destroying a bed set as a spawn point now resets it to the world's default spawn.
- Improved server logs.
