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
        /// Filters entity results based on stealth mechanics
        /// This can be called by other systems that detect entities
        /// </summary>
        public static Entity[] FilterEntitiesForStealth(IWorldAccessor world, Vec3d observerPos, Entity[]? entities, float originalRange)
        {
            if (entities == null) return new Entity[0];

            List<Entity> filteredResults = new List<Entity>();

            foreach (Entity entity in entities)
            {
                if (entity is EntityPlayer player && StealthModSystem.IsPlayerSneaking(player))
                {
                    double distance = observerPos.DistanceTo(entity.ServerPos.XYZ);
                    double sneakRadius = originalRange * SNEAK_DETECTION_MULTIPLIER;

                    // Only include sneaking players if they're within reduced range
                    if (distance <= sneakRadius)
                    {
                        filteredResults.Add(entity);
                    }
                    // Skip sneaking players beyond sneak range
                }
                else
                {
                    // Check line-of-sight for non-sneaking players and all other entities
                    if (entity is EntityPlayer targetPlayer)
                    {
                        Vec3d eyePos = observerPos.Add(0, 1.6, 0); // Approximate eye height
                        Vec3d targetEyePos = entity.ServerPos.XYZ.Add(0, entity.Properties.EyeHeight, 0);

                        if (StealthModSystem.HasLineOfSight(world, eyePos, targetEyePos))
                        {
                            filteredResults.Add(entity);
                        }
                        // Skip players without line of sight
                    }
                    else
                    {
                        filteredResults.Add(entity); // Keep all non-player entities
                    }
                }
            }

            return filteredResults.ToArray();
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
