using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using System;
using System.Collections.Generic;

namespace AemonEssentials.Stealth
{
    /// <summary>
    /// Utility class for stealth-related entity detection logic
    /// Note: Harmony patches removed due to interface method patching issues
    /// </summary>
    public class EntityDetectionPatches
    {
        // Configuration values
        public const double SNEAK_DETECTION_MULTIPLIER = 0.4; // Reduce detection range to 40% when sneaking

        /// <summary>
        /// Filters entity results based on stealth mechanics with FOV detection
        /// This can be called by other systems that detect entities
        /// </summary>
        public static Entity[] FilterEntitiesForStealth(IWorldAccessor world, Vec3d observerPos, Entity[]? entities, float originalRange, EntityAgent? observerEntity = null)
        {
            if (entities == null) return new Entity[0];

            List<Entity> filteredResults = new List<Entity>();

            foreach (Entity entity in entities)
            {
                if (entity is EntityPlayer player)
                {
                    // Check if player can be detected
                    if (CanEntityDetectPlayer(world, observerEntity, observerPos, player, originalRange))
                    {
                        filteredResults.Add(entity);
                    }
                }
                else
                {
                    filteredResults.Add(entity); // Keep all non-player entities
                }
            }

            return filteredResults.ToArray();
        }
        
        /// <summary>
        /// Determines if an entity can detect a player based on distance, FOV, line-of-sight, and stealth
        /// </summary>
        public static bool CanEntityDetectPlayer(IWorldAccessor world, EntityAgent? observer, Vec3d observerPos, EntityPlayer player, float baseRange)
        {
            Vec3d playerPos = player.ServerPos.XYZ;
            double distance = observerPos.DistanceTo(playerPos);
            
            // Check distance first (with stealth modifier)
            float detectionRange = baseRange;
            if (StealthModSystem.IsPlayerSneaking(player))
            {
                detectionRange *= (float)SNEAK_DETECTION_MULTIPLIER;
            }
            
            if (distance > detectionRange)
            {
                return false; // Too far away
            }
            
            // Check FOV if we have observer entity information
            if (observer != null)
            {
                if (!IsPlayerInFieldOfView(observer, observerPos, playerPos))
                {
                    return false; // Player is behind or outside FOV
                }
            }
            
            // Check line-of-sight
            Vec3d eyePos = observerPos.Add(0, 1.6, 0); // Observer eye height
            Vec3d targetEyePos = playerPos.Add(0, player.Properties.EyeHeight, 0);
            
            if (!StealthModSystem.HasLineOfSight(world, eyePos, targetEyePos))
            {
                return false; // No line of sight
            }
            
            return true; // Player can be detected
        }
        
        /// <summary>
        /// Check if player is within the entity's 90-degree field of view
        /// </summary>
        public static bool IsPlayerInFieldOfView(EntityAgent observer, Vec3d observerPos, Vec3d playerPos)
        {
            // Calculate direction from observer to player
            Vec3d toPlayer = playerPos.Sub(observerPos).Normalize();
            
            // Get observer's facing direction
            float observerYaw = observer.BodyYaw;
            Vec3d observerFacing = new Vec3d(Math.Sin(observerYaw), 0, Math.Cos(observerYaw)).Normalize();
            
            // Calculate angle between facing direction and direction to player
            double dotProduct = observerFacing.Dot(toPlayer);
            double angleRadians = Math.Acos(Math.Max(-1.0, Math.Min(1.0, dotProduct)));
            
            // 90-degree FOV = 45 degrees each side = π/4 radians
            double fovHalfAngle = Math.PI / 4.0; // 45 degrees in radians
            
            return angleRadians <= fovHalfAngle;
        }

        /// <summary>
        /// Filters a list of entities for stealth mechanics
        /// </summary>
        public static void FilterEntityListForStealth(IWorldAccessor world, Vec3d observerPos, List<Entity> entities, float originalRange)
        {
            if (entities == null) return;

            for (int i = entities.Count - 1; i >= 0; i--)
            {
                Entity entity = entities[i];

                if (entity is EntityPlayer player)
                {
                    // Apply stealth mechanics to players
                    if (StealthModSystem.IsPlayerSneaking(player))
                    {
                        double distance = observerPos.DistanceTo(entity.ServerPos.XYZ);
                        double sneakDistance = originalRange * SNEAK_DETECTION_MULTIPLIER;

                        if (distance > sneakDistance)
                        {
                            entities.RemoveAt(i);
                            continue;
                        }
                    }

                    // Check line-of-sight for all players
                    Vec3d eyePos = observerPos.Add(0, 1.6, 0);
                    Vec3d targetEyePos = entity.ServerPos.XYZ.Add(0, entity.Properties.EyeHeight, 0);

                    if (!StealthModSystem.HasLineOfSight(world, eyePos, targetEyePos))
                    {
                        entities.RemoveAt(i);
                    }
                }
            }
        }
    }
}
