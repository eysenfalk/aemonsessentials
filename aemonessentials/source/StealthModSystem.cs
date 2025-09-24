using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.API.Common.Entities;
using System;
using System.Reflection;

namespace AemonEssentials.Stealth
{
    public class StealthModSystem : ModSystem
    {
        private Harmony? harmony;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            // Initialize Harmony patches
            harmony = new Harmony("aemonessentials.stealth");
            
            // Patch IWorldAccessor.GetEntitiesAround to filter results for stealth
            PatchGetEntitiesAround();
            
            api.Logger.Notification("Aemon's Stealth System loaded successfully with Harmony patches");
        }

        private void PatchGetEntitiesAround()
        {
            try
            {
                // Let Harmony auto-detect and patch all implementations
                // This is more reliable than trying to find specific types
                harmony?.PatchAll(Assembly.GetExecutingAssembly());
                
                Console.WriteLine("[STEALTH] Harmony patches applied successfully");
            }
            catch (Exception e)
            {
                // If direct patching fails, log and continue
                Console.WriteLine($"Stealth: Could not apply Harmony patches: {e.Message}");
            }
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll("aemonessentials.stealth");
        }

        // Utility method to check if player is sneaking
        public static bool IsPlayerSneaking(EntityPlayer player)
        {
            return player?.Controls?.Sneak == true;
        }

        // Utility method for line-of-sight checking
        public static bool HasLineOfSight(IWorldAccessor world, Vec3d fromPos, Vec3d toPos)
        {
            // Simple raycast to check if solid blocks obstruct vision
            Vec3d direction = (toPos - fromPos).Normalize();
            double distance = fromPos.DistanceTo(toPos);

            // Check every 0.5 blocks along the ray
            for (double d = 0.5; d < distance; d += 0.5)
            {
                Vec3d checkPos = fromPos + direction * d;
                BlockPos blockPos = new BlockPos((int)checkPos.X, (int)checkPos.Y, (int)checkPos.Z);

                Block block = world.BlockAccessor.GetBlock(blockPos);
                if (block != null && block.BlockMaterial != EnumBlockMaterial.Air)
                {
                    // Check if block is solid and blocks vision
                    if (block.SideSolid[BlockFacing.UP.Index])
                    {
                        return false; // Line of sight blocked
                    }
                }
            }

            return true; // Clear line of sight
        }
    }
}
