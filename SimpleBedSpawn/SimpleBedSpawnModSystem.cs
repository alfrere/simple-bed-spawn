using System;
using System.Collections.Generic;
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
        private ICoreServerAPI? sapi;

        // Track the registered WatchedAttribute listeners per player so we can clean up
        private readonly Dictionary<string, Action> mountListenersByPlayerUid = new();
        private Dictionary<string, BlockPos> bedPosByPlayerUid = new();
        private HashSet<string> pendingSpawnResets = new();

        private const string SaveKey = "SimpleBedSpawn_BedPositions";
        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;

            api.Event.SaveGameLoaded += OnSaveGameLoaded;
            api.Event.GameWorldSave += OnGameWorldSave;
            api.Event.ServerRunPhase(EnumServerRunPhase.Shutdown, OnShutdown);

            api.Event.PlayerNowPlaying += OnPlayerNowPlaying;
            api.Event.PlayerDisconnect += OnPlayerDisconnect;
            api.Event.DidBreakBlock += OnDidBreakBlock;
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
                player.SendIngameError("bedspawn-lost", "Your bed was destroyed, spawn point reset.");
                sapi?.Logger.Notification(
                    "[SimpleBedSpawn] Applied pending spawn reset for {0}.", player.PlayerName
                );
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
            if (saved?.Beds == null) return;

            bedPosByPlayerUid.Clear();
            foreach (var beds in saved.Beds)
                bedPosByPlayerUid[beds.Key] = new BlockPos(beds.Value[0], beds.Value[1], beds.Value[2]);
            
            sapi?.Logger.Notification("[SimpleBedSpawn] Loaded {0} bed position(s).", bedPosByPlayerUid.Count);
        }

        private void OnGameWorldSave()
        {
            var data = new BedSaveData();
            foreach (var bedPos in bedPosByPlayerUid)
                data.Beds[bedPos.Key] = new[] { bedPos.Value.X, bedPos.Value.Y, bedPos.Value.Z };

            sapi?.WorldManager.SaveGame.StoreData(SaveKey, SerializerUtil.Serialize(data));

            // Debug log to check if the data is correclty saved
            sapi?.Logger.Notification("[SimpleBedSpawn] Saved {0} bed(s), {1} pending reset(s).",
            data.Beds.Count, data.PendingResets.Count);
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

        private void OnDidBreakBlock(IServerPlayer byPlayer, int oldblockId, BlockSelection blockSel)
        {
            var toRemove = new List<string>();

            foreach (var pair in bedPosByPlayerUid)
            {
                if (pair.Value.Equals(blockSel.Position) ||
                pair.Value.Equals(blockSel.Position.NorthCopy()) ||
                pair.Value.Equals(blockSel.Position.SouthCopy()) ||
                pair.Value.Equals(blockSel.Position.EastCopy()) ||
                pair.Value.Equals(blockSel.Position.WestCopy())
                )
                {
                    toRemove.Add(pair.Key);

                    IServerPlayer? bedOwner = sapi?.World.PlayerByUid(pair.Key) as IServerPlayer;
                    if (bedOwner != null)
                    {
                        bedOwner.SetSpawnPosition(null);
                        bedOwner.SendIngameError("bedspawn-lost", "Your bed was destroyed, spawn point reset.");
                    }
                    else
                    {
                        pendingSpawnResets.Add(pair.Key);
                    }

                    sapi?.Logger.Notification("[SimpleBedSpawn] Bed belonging to {0} was destroyed, spawn reset.", pair.Key);
                }
            }

            foreach (var key in toRemove)
            {
                bedPosByPlayerUid.Remove(key);                
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

            // Show when a bed position is saved in the logs
            sapi?.Logger.Notification("[SimpleBedSpawn] {0}'s spawn set at ({1}, {2}, {3}).",
            player.PlayerName,
            (int)Math.Floor(seatPos.X),
            (int)Math.Floor(seatPos.InternalY),
            (int)Math.Floor(seatPos.Z)
            );

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

            // Notify the player
            player.SendIngameError("bedspawn-set", "Spawn point set to this bed.");

            bedPosByPlayerUid[player.PlayerUID] = new BlockPos(
                (int)Math.Floor(seatPos.X),
                (int)Math.Floor(seatPos.InternalY),
                (int)Math.Floor(seatPos.Z)
            );
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
                sapi.Event.DidBreakBlock -= OnDidBreakBlock;

                // Unregister all listeners
                foreach (IPlayer p in sapi.World.AllOnlinePlayers)
                {
                    if (p is IServerPlayer sp) UnregisterPlayer(sp);
                }
            }

            mountListenersByPlayerUid.Clear();
            bedPosByPlayerUid.Clear();
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