using System.Collections.Generic;

namespace SimpleBedSpawn
{
    public class SimpleBedSpawnConfig
    {
        /// <summary>
        /// Master switch. When false, the mod loads but does nothing.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// When true, logs extra detail (bed positions, per-sleep events, save/load counts)
        /// to the server log. Leave off for normal play.
        /// </summary>
        public bool VerboseLogging { get; set; } = false;

        /// <summary>
        /// Block codes of beds allowed to set the spawn point. Wildcards are supported,
        /// e.g. "game:bed-woodaged-*" or "game:bed-hay-*". Leave empty to allow every bed.
        /// </summary>
        public List<string> BedWhitelist { get; set; } = new();

        /// <summary>
        /// When false, a bed can be the spawn point of one player only. Other players sleeping
        /// in it don't get their spawn set, and the owner keeps it.
        /// </summary>
        public bool AllowSharedBeds { get; set; } = true;

        public MessagesConfig Messages { get; set; } = new();
    }

    public class MessagesConfig
    {
        public bool ShowSpawnSetMessage { get; set; } = true;
        public string SpawnSetMessage { get; set; } = "Spawn point set to this bed.";

        public bool ShowSpawnLostMessage { get; set; } = true;
        public string SpawnLostMessage { get; set; } = "Your bed was destroyed, spawn point reset.";

        public bool ShowBedNotAllowedMessage { get; set; } = true;
        public string BedNotAllowedMessage { get; set; } = "This bed can't be used as a spawn point.";

        public bool ShowBedTakenMessage { get; set; } = true;
        public string BedTakenMessage { get; set; } = "This bed is already someone else's spawn point.";
    }
}
