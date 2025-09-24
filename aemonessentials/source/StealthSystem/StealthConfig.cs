/// <summary>
/// This file defines the configuration options for the Stealth System module.
/// 
/// For beginners: Configuration files let mod users change how the stealth system behaves (e.g., how much sneaking reduces detection, how long AI remembers players, etc.).
/// </summary>
namespace AemonEssentials.StealthSystem
{
    public class StealthConfig
    {
        /// <summary>
        /// Enables or disables the stealth system.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Multiplier for detection range when sneaking (e.g., 0.5 = half range).
        /// </summary>
        public float SneakDetectionMultiplier { get; set; } = 0.5f;

        /// <summary>
        /// If true, entities require line of sight to detect players.
        /// </summary>
        public bool EnableLineOfSight { get; set; } = true;

        /// <summary>
        /// How long (in seconds) AI remembers last seen player position.
        /// </summary>
        public float AIMemoryDurationSeconds { get; set; } = 5.0f;

        /// <summary>
        /// Enables debug logging for troubleshooting.
        /// </summary>
        public bool DebugLogging { get; set; } = false;

        /// <summary>
        /// Enables performance mode (disables expensive features).
        /// </summary>
        public bool PerformanceMode { get; set; } = false;
    }
}
