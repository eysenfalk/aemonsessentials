/// <summary>
/// Main entry point for Aemon's Essentials mod. Coordinates all modules and shared functionality.
/// 
/// For beginners: This is the "master controller" that starts up all the different features of our mod.
/// Think of it as the main office that manages different departments (modules).
/// </summary>
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Client;
using AemonEssentials.StealthSystem;

[assembly: ModInfo("Aemon's Essentials",
    Description = "A comprehensive mod with stealth mechanics, handbook improvements, and advanced configuration",
    Version = "1.0.0",
    Authors = new[] { "Aemon" },
    Side = "Universal")]

namespace AemonEssentials
{
    /// <summary>
    /// Main mod system that initializes and coordinates all modules.
    /// For beginners: ModSystem is Vintage Story's way of letting mods hook into the game.
    /// </summary>
    public class AemonEssentialsMod : ModSystem
    {
        // Module systems
        private StealthModSystem? stealthSystem;

        /// <summary>
        /// Called when the mod starts loading.
        /// For beginners: This is the very first thing that runs when our mod loads.
        /// </summary>
        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            
            api.Logger.Notification("Aemon's Essentials mod is starting up...");
            
            // Initialize stealth system
            stealthSystem = new StealthModSystem();
            stealthSystem.Start(api);
            
            api.Logger.Notification("Aemon's Essentials mod started successfully!");
        }

        /// <summary>
        /// Called when starting on the server side.
        /// For beginners: Server-side handles the actual game logic and rules.
        /// </summary>
        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            
            // Start server-side module functionality
            stealthSystem?.StartServerSide(api);
        }

        /// <summary>
        /// Called when starting on the client side.
        /// For beginners: Client-side handles the visual display and user interface.
        /// </summary>
        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            
            // Start client-side module functionality if needed
            // (Stealth system is primarily server-side)
        }

        /// <summary>
        /// Called when the mod is being shut down.
        /// For beginners: This cleans up resources when the mod stops running.
        /// </summary>
        public override void Dispose()
        {
            stealthSystem?.Dispose();
            base.Dispose();
        }
    }
}