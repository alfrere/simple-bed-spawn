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

        public MessagesConfig Messages { get; set; } = new();
    }

    public class MessagesConfig
    {
        public bool ShowSpawnSetMessage { get; set; } = true;
        public string SpawnSetMessage { get; set; } = "Spawn point set to this bed.";

        public bool ShowSpawnLostMessage { get; set; } = true;
        public string SpawnLostMessage { get; set; } = "Your bed was destroyed, spawn point reset.";
    }
}
