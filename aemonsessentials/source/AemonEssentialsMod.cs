using Vintagestory.API.Common;
using HarmonyLib;

namespace AemonsEssentials
{
    public class AemonsEssentialsMod : ModSystem
    {
        private Harmony? harmony;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            
            api.Logger.Notification("Aemon's Essentials mod is starting up...");

            // Apply Harmony patches (single-file feature)
            harmony = new Harmony("aemonsessentials.handbookmemory");
            harmony.PatchAll();

            api.Logger.Notification("Aemon's Essentials mod started successfully!");
        }

        public override void Dispose()
        {
            // Remove Harmony patches on shutdown
            harmony?.UnpatchAll("aemonsessentials.handbookmemory");
            base.Dispose();
        }
    }
}