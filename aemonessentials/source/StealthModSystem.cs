using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using System;

namespace AemonEssentials.Stealth
{
    public class StealthModSystem : ModSystem
    {

        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            // Note: Harmony patching removed due to abstract interface method issues
            // Stealth mechanics will be applied through utility methods instead
            
            api.Logger.Notification("Aemon's Stealth System loaded successfully (without Harmony patches)");
        }

        public override void Dispose()
        {
            // No Harmony patches to clean up
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
