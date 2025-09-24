/// <summary>
/// Utility methods for the Stealth System (e.g., line-of-sight checks, distance calculations).
/// 
/// For beginners: Utility classes are helpers that provide common functions used by other parts of the mod.
/// </summary>
using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace AemonEssentials.StealthSystem
{
    public static class StealthUtils
    {
        /// <summary>
        /// Checks if there is a clear line of sight between two positions.
        /// For beginners: This method uses raycasting to see if anything blocks the view between two points.
        /// </summary>
        public static bool HasLineOfSight(IWorldAccessor world, Vec3d from, Vec3d to)
        {
            // For beginners: Raycasting means checking along a straight line for obstacles.
            // 1. Calculate direction vector
            Vec3d direction = (to - from).Normalize();
            double distance = from.DistanceTo(to);
            
            // 2. Step along the line in small increments (0.5 blocks for performance)
            double stepSize = 0.5;
            Vec3d stepVector = direction * stepSize;
            Vec3d currentPos = from.Clone();
            
            // 3. Check each step along the ray for solid blocks
            for (double traveled = 0; traveled < distance; traveled += stepSize)
            {
                // Get the block at current position
                BlockPos blockPos = new BlockPos((int)Math.Floor(currentPos.X), 
                                               (int)Math.Floor(currentPos.Y), 
                                               (int)Math.Floor(currentPos.Z));
                
                Block block = world.BlockAccessor.GetBlock(blockPos);
                
                // If we hit a solid block (not air, not transparent), vision is blocked
                if (block != null && !block.IsLiquid() && block.Id != 0 && 
                    block.BlockMaterial != EnumBlockMaterial.Air)
                {
                    // For beginners: We found a solid block blocking the view
                    return false;
                }
                
                // Move to next position
                currentPos.Add(stepVector);
            }
            
            // 4. If we reached the end without hitting solid blocks, line of sight is clear
            return true;
        }

        /// <summary>
        /// Calculates the effective detection range for an entity when the player is sneaking.
        /// For beginners: This reduces how far away entities can detect a sneaking player.
        /// </summary>
        public static double CalculateSneakDetectionRange(double baseRange, float sneakMultiplier, bool playerIsSneaking)
        {
            return playerIsSneaking ? baseRange * sneakMultiplier : baseRange;
        }

        /// <summary>
        /// Checks if a player is currently sneaking/crouching.
        /// For beginners: This looks at the player's current movement state.
        /// </summary>
        public static bool IsPlayerSneaking(IPlayer player)
        {
            // Check if player entity is in sneaking pose
            return player?.Entity?.Controls?.Sneak == true;
        }
    }
}
