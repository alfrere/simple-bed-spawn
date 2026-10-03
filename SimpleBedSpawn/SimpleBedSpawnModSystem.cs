using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace SimpleBedSpawn
{
    // <summary>
    // SimpleBedSpawn sets the player's spawn point when they sleep in a bed.
    // Compatible with Vintage Story 1.22+
    // </summary>

    public class SimpleBedSpawnSystem : ModSystem
    {
        private static SimpleBedSpawnSystem? instance;
        private ICoreServerAPI? sapi;
        private Harmony? harmony;
        private SimpleBedSpawnConfig config = new();
        // Parsed from config.SetSpawnCooldown
        private TimeSpan spawnCooldown = TimeSpan.Zero;

        // Track the registered WatchedAttribute listeners per player so we can clean up
        private readonly Dictionary<string, Action> mountListenersByPlayerUid = new();
        private Dictionary<string, BlockPos> bedPosByPlayerUid = new();
        private HashSet<string> pendingSpawnResets = new();
        // In memory only: a server restart clears every cooldown
        private readonly Dictionary<string, DateTime> lastSpawnSetByPlayerUid = new();

        private const string SaveKey = "SimpleBedSpawn_BedPositions";
        private const string ConfigFileName = "SimpleBedSpawnConfig.json";
        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            LoadConfig();

            if (!config.Enabled)
            {
                api.Logger.Notification("[SimpleBedSpawn] Mod disabled via config, not registering any handlers.");
                return;
            }

            api.Event.SaveGameLoaded += OnSaveGameLoaded;
            api.Event.GameWorldSave += OnGameWorldSave;
            api.Event.ServerRunPhase(EnumServerRunPhase.Shutdown, OnShutdown);

            api.Event.PlayerNowPlaying += OnPlayerNowPlaying;
            api.Event.PlayerDisconnect += OnPlayerDisconnect;

            instance = this;
            harmony = new Harmony(Mod.Info.ModID);
            harmony.Patch(
                AccessTools.Method(typeof(BlockEntity), nameof(BlockEntity.OnBlockRemoved)),
                postfix: new HarmonyMethod(typeof(SimpleBedSpawnSystem), nameof(OnBlockEntityRemovedPostfix))
            );
        }

        private void LoadConfig()
        {
            try
            {
                config = sapi?.LoadModConfig<SimpleBedSpawnConfig>(ConfigFileName) ?? new SimpleBedSpawnConfig();
            }
            catch (Exception e)
            {
                sapi?.Logger.Error("[SimpleBedSpawn] Failed to load {0}, falling back to defaults: {1}", ConfigFileName, e.Message);
                config = new SimpleBedSpawnConfig();
            }

            sapi?.StoreModConfig(config, ConfigFileName);

            if (!TryParseCooldown(config.SetSpawnCooldown, out spawnCooldown))
            {
                sapi?.Logger.Warning("[SimpleBedSpawn] Invalid SetSpawnCooldown \"{0}\", expected \"minutes\", " +
                    "\"minutes:seconds\" or \"hours:minutes:seconds\" (e.g. \"10:30\"). Cooldown disabled.",
                    config.SetSpawnCooldown);
                spawnCooldown = TimeSpan.Zero;
            }
        }

        /// <summary>
        /// Parses "minutes", "minutes:seconds" or "hours:minutes:seconds" into a duration.
        /// An empty value means no cooldown.
        /// </summary>
        private static bool TryParseCooldown(string? value, out TimeSpan cooldown)
        {
            cooldown = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(value)) return true;

            string[] parts = value.Trim().Split(':');
            if (parts.Length > 3) return false;

            var numbers = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return false;
            }

            cooldown = numbers.Length switch
            {
                1 => TimeSpan.FromMinutes(numbers[0]),
                2 => new TimeSpan(0, numbers[0], numbers[1]),
                _ => new TimeSpan(numbers[0], numbers[1], numbers[2])
            };
            return true;
        }

        private void OnShutdown()
        {
            OnGameWorldSave();
        }

        // Called when a player joins the server
        private void OnPlayerNowPlaying(IServerPlayer player)
        {
            if (pendingSpawnResets.Remove(player.PlayerUID))
            {
                player.SetSpawnPosition(null);
                SendSpawnLostMessage(player);
                if (config.VerboseLogging)
                {
                    sapi?.Logger.Notification(
                        "[SimpleBedSpawn] Applied pending spawn reset for {0}.", player.PlayerName
                    );
                }
            }

            RegisterMountListener(player);
        }

        private void OnPlayerDisconnect(IServerPlayer player)
        {
            UnregisterPlayer(player);
        }

        private void OnSaveGameLoaded()
        {
            byte[]? data = sapi?.WorldManager.SaveGame.GetData(SaveKey);
            if (data == null) return;

            var saved = SerializerUtil.Deserialize<BedSaveData>(data);
            if (saved == null) return;

            pendingSpawnResets = new HashSet<string>(saved.PendingResets ?? new List<string>());

            bedPosByPlayerUid.Clear();
            if (saved.Beds != null)
            {
                foreach (var beds in saved.Beds)
                    bedPosByPlayerUid[beds.Key] = new BlockPos(beds.Value[0], beds.Value[1], beds.Value[2]);
            }

            if (config.VerboseLogging)
            {
                sapi?.Logger.Notification("[SimpleBedSpawn] Loaded {0} bed position(s), {1} pending reset(s).",
                bedPosByPlayerUid.Count, pendingSpawnResets.Count);
            }
        }

        private void OnGameWorldSave()
        {
            var data = new BedSaveData();
            foreach (var bedPos in bedPosByPlayerUid)
                data.Beds[bedPos.Key] = new[] { bedPos.Value.X, bedPos.Value.Y, bedPos.Value.Z };
            data.PendingResets.AddRange(pendingSpawnResets);

            sapi?.WorldManager.SaveGame.StoreData(SaveKey, SerializerUtil.Serialize(data));

            if (config.VerboseLogging)
            {
                sapi?.Logger.Notification("[SimpleBedSpawn] Saved {0} bed(s), {1} pending reset(s).",
                data.Beds.Count, data.PendingResets.Count);
            }
        }


        private void RegisterMountListener(IServerPlayer player)
        {
            if (player?.Entity == null) return;

            // Clean up any previous listener for this player first
            UnregisterPlayer(player);

            string playerUid = player.PlayerUID;

            // Fire a delayed check whenever mountedOn changes (i.e. player mounts/unmounts a seat, including beds)
            Action listener = () =>
            {
                // Small delay to let the sleep state settle in WatchedAttributes
                sapi?.Event.RegisterCallback(_ => TrySetSpawnFromSleep(playerUid), 100);
            };

            mountListenersByPlayerUid[playerUid] = listener;
            player.Entity.WatchedAttributes.RegisterModifiedListener("mountedOn", listener);
        }

        private void UnregisterPlayer(IServerPlayer player)
        {
            if (player == null) return;
            if (!mountListenersByPlayerUid.TryGetValue(player.PlayerUID, out Action? listener)) return;

            player.Entity?.WatchedAttributes.UnregisterListener(listener);
            mountListenersByPlayerUid.Remove(player.PlayerUID);
        }

        // Harmony postfix on BlockEntity.OnBlockRemoved. Runs for every removal cause
        // (player break, explosion, world edit, other mods), unlike DidBreakBlock which only fires for players.
        private static void OnBlockEntityRemovedPostfix(BlockEntity __instance)
        {
            // The patch is process-wide, so in singleplayer it also runs for client-side block entities
            if (__instance is not IMountableSeat || __instance.Api?.Side != EnumAppSide.Server) return;

            instance?.OnBedRemoved(__instance.Pos);
        }

        private void OnBedRemoved(BlockPos removedPos)
        {
            var toRemove = new List<string>();

            foreach (var pair in bedPosByPlayerUid)
            {
                if (pair.Value.Equals(removedPos) ||
                pair.Value.Equals(removedPos.NorthCopy()) ||
                pair.Value.Equals(removedPos.SouthCopy()) ||
                pair.Value.Equals(removedPos.EastCopy()) ||
                pair.Value.Equals(removedPos.WestCopy())
                )
                {
                    toRemove.Add(pair.Key);

                    IServerPlayer? bedOwner = sapi?.World.PlayerByUid(pair.Key) as IServerPlayer;
                    // PlayerByUid also returns offline players, so check the connection state instead
                    if (bedOwner?.ConnectionState == EnumClientState.Playing)
                    {
                        bedOwner.SetSpawnPosition(null);
                        SendSpawnLostMessage(bedOwner);
                    }
                    else
                    {
                        // Reset now if the player data is available, and queue it so the message is shown on login
                        bedOwner?.SetSpawnPosition(null);
                        pendingSpawnResets.Add(pair.Key);
                    }

                    if (config.VerboseLogging)
                    {
                        sapi?.Logger.Notification("[SimpleBedSpawn] Bed belonging to {0} was destroyed, spawn reset.", pair.Key);
                    }
                }
            }

            foreach (var key in toRemove)
            {
                bedPosByPlayerUid.Remove(key);                
                // Losing their bed shouldn't also lock the player out of setting a new spawn
                lastSpawnSetByPlayerUid.Remove(key);
            }
        }

        private void TrySetSpawnFromSleep(string playerUid)
        {
            IServerPlayer? player = sapi?.World.PlayerByUid(playerUid) as IServerPlayer;
            if (player?.Entity == null) return;

            EntityPlayer entity = player.Entity;

            // Player must be mounted on something and sleeping
            if (entity.MountedOn == null || !IsSleeping(entity)) return;

            // Grab the position of the seat (the bed the player is lying in)
            EntityPos seatPos = entity.MountedOn.SeatPosition ?? entity.Pos!;

            BlockPos bedPos = new BlockPos(
                (int)Math.Floor(seatPos.X),
                (int)Math.Floor(seatPos.InternalY),
                (int)Math.Floor(seatPos.Z)
            );

            // Prefer the bed's own block entity, fall back to the block at the seat position
            Block? bedBlock = (entity.MountedOn as BlockEntity)?.Block ?? sapi?.World.BlockAccessor.GetBlock(bedPos);

            if (!IsBedAllowed(bedBlock))
            {
                if (config.VerboseLogging)
                {
                    sapi?.Logger.Notification("[SimpleBedSpawn] {0} slept in non-whitelisted bed {1}, spawn not set.",
                    player.PlayerName, bedBlock?.Code?.ToString() ?? "unknown");
                }
                SendBedNotAllowedMessage(player);
                return;
            }

            if (!config.AllowSharedBeds && IsBedOwnedByOther(bedPos, player.PlayerUID))
            {
                if (config.VerboseLogging)
                {
                    sapi?.Logger.Notification("[SimpleBedSpawn] {0} slept in a bed owned by another player, spawn not set.",
                    player.PlayerName);
                }
                SendBedTakenMessage(player);
                return;
            }

            // Sleeping again in the current spawn bed is never blocked and doesn't restart the cooldown
            bool isSameBed = bedPosByPlayerUid.TryGetValue(player.PlayerUID, out BlockPos? currentBedPos) &&
                currentBedPos.Equals(bedPos);

            if (!isSameBed)
            {
                TimeSpan remaining = GetRemainingCooldown(player.PlayerUID);
                if (remaining > TimeSpan.Zero)
                {
                    if (config.VerboseLogging)
                    {
                        sapi?.Logger.Notification("[SimpleBedSpawn] {0} is on cooldown ({1} left), spawn not set.",
                        player.PlayerName, FormatRemainingTime(remaining));
                    }
                    SendCooldownMessage(player, remaining);
                    return;
                }
            }

            if (config.VerboseLogging)
            {
                sapi?.Logger.Notification("[SimpleBedSpawn] {0}'s spawn set at ({1}, {2}, {3}).",
                player.PlayerName,
                (int)Math.Floor(seatPos.X),
                (int)Math.Floor(seatPos.InternalY),
                (int)Math.Floor(seatPos.Z)
                );
            }

            // Set the player's personal spawn point
            player.SetSpawnPosition(new PlayerSpawnPos
            {
                x = (int)Math.Floor(seatPos.X),
                y = (int)Math.Ceiling(seatPos.InternalY),
                z = (int)Math.Floor(seatPos.Z),
                yaw   = seatPos.Yaw,
                pitch = 0f,
                roll  = 0f,
                RemainingUses = -1   // -1 means unlimited uses
            });

            SendSpawnSetMessage(player);

            bedPosByPlayerUid[player.PlayerUID] = bedPos;
            if (!isSameBed) lastSpawnSetByPlayerUid[player.PlayerUID] = DateTime.UtcNow;
        }

        /// <summary>
        /// Returns true if the bed may set the spawn point. An empty whitelist allows every bed.
        /// </summary>
        private bool IsBedAllowed(Block? bedBlock)
        {
            if (config.BedWhitelist.Count == 0) return true;
            if (bedBlock?.Code == null) return false;

            foreach (string pattern in config.BedWhitelist)
            {
                if (WildcardUtil.Match(new AssetLocation(pattern), bedBlock.Code)) return true;
            }
            return false;
        }

        /// <summary>
        /// Returns the real time left before the player can set their spawn on a different bed,
        /// or zero if they can do it now.
        /// </summary>
        private TimeSpan GetRemainingCooldown(string playerUid)
        {
            if (spawnCooldown <= TimeSpan.Zero) return TimeSpan.Zero;
            if (!lastSpawnSetByPlayerUid.TryGetValue(playerUid, out DateTime lastSet)) return TimeSpan.Zero;

            TimeSpan remaining = spawnCooldown - (DateTime.UtcNow - lastSet);
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        /// <summary>
        /// Returns true if another player already has their spawn set on the bed at this position.
        /// </summary>
        private bool IsBedOwnedByOther(BlockPos bedPos, string playerUid)
        {
            foreach (var pair in bedPosByPlayerUid)
            {
                if (pair.Key != playerUid && pair.Value.Equals(bedPos)) return true;
            }
            return false;
        }

        private void SendSpawnSetMessage(IServerPlayer player)
        {
            if (config.Messages.ShowSpawnSetMessage)
                player.SendIngameError("bedspawn-set", config.Messages.SpawnSetMessage);
        }

        private void SendSpawnLostMessage(IServerPlayer player)
        {
            if (config.Messages.ShowSpawnLostMessage)
                player.SendIngameError("bedspawn-lost", config.Messages.SpawnLostMessage);
        }

        private void SendBedNotAllowedMessage(IServerPlayer player)
        {
            if (config.Messages.ShowBedNotAllowedMessage)
                player.SendIngameError("bedspawn-notallowed", config.Messages.BedNotAllowedMessage);
        }

        private void SendBedTakenMessage(IServerPlayer player)
        {
            if (config.Messages.ShowBedTakenMessage)
                player.SendIngameError("bedspawn-taken", config.Messages.BedTakenMessage);
        }

        private void SendCooldownMessage(IServerPlayer player, TimeSpan remaining)
        {
            if (!config.Messages.ShowCooldownMessage) return;

            // Replace instead of string.Format so a stray brace in the config can't throw
            player.SendIngameError("bedspawn-cooldown",
                config.Messages.CooldownMessage.Replace("{0}", FormatRemainingTime(remaining)));
        }

        /// <summary>
        /// Formats a duration with its two largest units, e.g. "18 seconds", "10 minutes 30 seconds",
        /// "1 hour 5 minutes". Rounded up to the smallest unit shown, so it never shows 0.
        /// </summary>
        private static string FormatRemainingTime(TimeSpan remaining)
        {
            int totalSeconds = (int)Math.Ceiling(remaining.TotalSeconds);
            if (totalSeconds < 3600)
                return JoinUnits(totalSeconds / 60, "minute", totalSeconds % 60, "second");

            int totalMinutes = (int)Math.Ceiling(totalSeconds / 60.0);
            return JoinUnits(totalMinutes / 60, "hour", totalMinutes % 60, "minute");
        }

        private static string JoinUnits(int major, string majorUnit, int minor, string minorUnit)
        {
            static string Unit(int n, string unit) => n == 1 ? $"1 {unit}" : $"{n} {unit}s";

            if (major == 0) return Unit(minor, minorUnit);
            if (minor == 0) return Unit(major, majorUnit);
            return $"{Unit(major, majorUnit)} {Unit(minor, minorUnit)}";
        }

        /// <summary>
        /// Returns true if the entity's tiredness attribute marks them as sleeping.
        /// </summary>
        private static bool IsSleeping(EntityPlayer entity)
        {
            ITreeAttribute? tiredness = entity.WatchedAttributes.GetTreeAttribute("tiredness");
            return tiredness != null && tiredness.GetInt("isSleeping") > 0;
        }

        public override void Dispose()
        {
            if (sapi != null)
            {
                sapi.Event.SaveGameLoaded -= OnSaveGameLoaded;
                sapi.Event.GameWorldSave -= OnGameWorldSave;

                sapi.Event.PlayerNowPlaying -= OnPlayerNowPlaying;
                sapi.Event.PlayerDisconnect -= OnPlayerDisconnect;

                // Unregister all listeners
                foreach (IPlayer p in sapi.World.AllOnlinePlayers)
                {
                    if (p is IServerPlayer sp) UnregisterPlayer(sp);
                }
            }

            harmony?.UnpatchAll(harmony.Id);
            instance = null;

            mountListenersByPlayerUid.Clear();
            bedPosByPlayerUid.Clear();
            pendingSpawnResets.Clear();
            lastSpawnSetByPlayerUid.Clear();
            base.Dispose();
        }

    }

    [ProtoContract]
    public class BedSaveData
    {
        [ProtoMember(1)] public Dictionary<string, int[]> Beds { get; set; } = new();
        [ProtoMember(2)] public List<string> PendingResets { get; set; } = new();
    }
}