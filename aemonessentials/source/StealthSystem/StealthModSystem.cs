/// <summary>
/// Main mod system for the Stealth System module. Handles initialization and coordination.
/// 
/// For beginners: This is the entry point for the stealth features. It loads config, sets up Harmony patches, and manages the system.
/// </summary>
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using System;

namespace AemonEssentials.StealthSystem
{
    public class StealthModSystem : ModSystem
    {
        public StealthConfig Config { get; private set; }
        public AIMemoryManager MemoryManager { get; private set; }
        
        private Harmony harmonyInstance;
        private ICoreAPI api;

        /// <summary>
        /// Called when the mod system starts up.
        /// For beginners: This is where we initialize everything our stealth system needs.
        /// </summary>
        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            this.api = api;
            
            // 1. Load configuration
            Config = LoadConfiguration();
            
            // 2. Initialize memory manager
            MemoryManager = new AIMemoryManager();
            
            // 3. Set up Harmony patches if stealth is enabled
            if (Config.Enabled)
            {
                InitializeHarmonyPatches();
            }
            
            api.Logger.Notification("Stealth System initialized with config: " +
                $"Enabled={Config.Enabled}, SneakMultiplier={Config.SneakDetectionMultiplier}, " +
                $"LineOfSight={Config.EnableLineOfSight}");
        }

        /// <summary>
        /// Called on server-side startup for additional server-specific initialization.
        /// For beginners: Server-side code handles the actual game logic, client-side handles display.
        /// </summary>
        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            
            if (Config.Enabled)
            {
                // Set up memory cleanup timer (every 5 seconds)
                api.Event.RegisterGameTickListener(OnMemoryCleanupTick, 5000);
            }
        }

        /// <summary>
        /// Loads stealth system configuration from file or creates default.
        /// For beginners: This reads settings from a file, or creates default settings if no file exists.
        /// </summary>
        private StealthConfig LoadConfiguration()
        {
            try
            {
                // For now, return default config. Later this will load from file.
                // TODO: Integrate with main mod configuration system
                var config = new StealthConfig();
                
                if (config.DebugLogging)
                {
                    api.Logger.Debug("Loaded stealth configuration: " + 
                        $"SneakMultiplier={config.SneakDetectionMultiplier}, " +
                        $"MemoryDuration={config.AIMemoryDurationSeconds}s");
                }
                
                return config;
            }
            catch (Exception e)
            {
                api.Logger.Error("Failed to load stealth configuration, using defaults: " + e.Message);
                return new StealthConfig();
            }
        }

        /// <summary>
        /// Sets up Harmony patches for entity detection modification.
        /// For beginners: Harmony lets us change how the game's AI works without modifying the original code.
        /// </summary>
        private void InitializeHarmonyPatches()
        {
            try
            {
                // Create Harmony instance with unique ID
                harmonyInstance = new Harmony("aemonessentials.stealthsystem");
                
                // Initialize patches with reference to this system
                StealthPatches.Initialize(this);
                
                // Apply all patches in the StealthPatches class
                harmonyInstance.PatchAll(typeof(StealthPatches));
                
                api.Logger.Notification("Stealth System Harmony patches applied successfully");
            }
            catch (Exception e)
            {
                api.Logger.Error("Failed to apply Stealth System Harmony patches: " + e.Message);
                Config.Enabled = false; // Disable system if patches fail
            }
        }

        /// <summary>
        /// Periodic cleanup of old AI memories to prevent memory leaks.
        /// For beginners: This removes old memories so the game doesn't slow down over time.
        /// </summary>
        private void OnMemoryCleanupTick(float deltaTime)
        {
            if (!Config.Enabled || MemoryManager == null) return;

            try
            {
                double currentTime = api.World.Calendar.TotalHours * 3600; // Convert to seconds
                MemoryManager.Cleanup(currentTime, Config.AIMemoryDurationSeconds);
                
                if (Config.DebugLogging)
                {
                    api.Logger.Debug("AI memory cleanup completed");
                }
            }
            catch (Exception e)
            {
                api.Logger.Error("Error during AI memory cleanup: " + e.Message);
            }
        }

        /// <summary>
        /// Called when the mod is disposed/unloaded.
        /// For beginners: This cleans up resources when the mod shuts down.
        /// </summary>
        public override void Dispose()
        {
            // Remove Harmony patches
            harmonyInstance?.UnpatchAll("aemonessentials.stealthsystem");
            
            base.Dispose();
        }
    }
}
