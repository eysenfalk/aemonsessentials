using Vintagestory.API.Common;
using HarmonyLib;

namespace AemonEssentials
{
    public class AemonEssentialsMod : ModSystem
    {
        private Harmony? harmony;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            
            api.Logger.Notification("Aemon's Essentials mod is starting up...");

            // Apply Harmony patches (single-file feature)
            harmony = new Harmony("aemonessentials.handbookmemory");
            harmony.PatchAll();

            api.Logger.Notification("Aemon's Essentials mod started successfully!");
        }

        public override void Dispose()
        {
            // Remove Harmony patches on shutdown
            harmony?.UnpatchAll("aemonessentials.handbookmemory");
            base.Dispose();
        }
    }
}