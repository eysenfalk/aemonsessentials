using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AemonEssentials.Stealth
{
    /// <summary>
    /// Harmony patches for implementing stealth mechanics
    /// </summary>
    [HarmonyPatch]
    public static class StealthPatches
    {
        /// <summary>
        /// Postfix patch for GetEntitiesAround to filter results based on stealth mechanics
        /// </summary>
        [HarmonyPostfix]
        public static void GetEntitiesAround_Postfix(Vec3d position, float horRange, float vertRange, ActionConsumable<Entity> matches, ref Entity[] __result)
        {
            if (__result == null || __result.Length == 0) return;

            try
            {
                // Find if there's an observer entity at the search position
                EntityAgent? observer = FindNearestEntityAgent(__result, position);
                
                // Filter results for stealth mechanics
                var filteredResults = new List<Entity>();
                
                foreach (Entity entity in __result)
                {
                    if (entity is EntityPlayer player)
                    {
                        // Apply stealth detection logic
                        if (observer != null)
                        {
                            if (EntityDetectionPatches.CanEntityDetectPlayer(entity.World, observer, position, player, horRange))
                            {
                                filteredResults.Add(entity);
                            }
                            // Skip players that can't be detected due to stealth
                        }
                        else
                        {
                            // No observer context - use basic distance/stealth filtering
                            if (ApplyBasicStealthFilter(position, player, horRange))
                            {
                                filteredResults.Add(entity);
                            }
                        }
                    }
                    else
                    {
                        // Keep all non-player entities
                        filteredResults.Add(entity);
                    }
                }
                
                __result = filteredResults.ToArray();
            }
            catch (Exception e)
            {
                // Log error but don't break the game
                Console.WriteLine($"Stealth patch error: {e.Message}");
                // Return original results if filtering fails
            }
        }

        /// <summary>
        /// Find the entity agent closest to the search position (likely the searcher)
        /// </summary>
        private static EntityAgent? FindNearestEntityAgent(Entity[] entities, Vec3d searchPos)
        {
            EntityAgent? nearestAgent = null;
            double nearestDistance = double.MaxValue;
            
            foreach (Entity entity in entities)
            {
                if (entity is EntityAgent agent)
                {
                    double distance = entity.Pos.DistanceTo(searchPos);
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearestAgent = agent;
                    }
                }
            }
            
            // If no agent in results, check world around search position
            if (nearestAgent == null && entities.Length > 0)
            {
                Entity? firstEntity = entities.FirstOrDefault();
                if (firstEntity?.World != null)
                {
                    var nearbyEntities = firstEntity.World.GetEntitiesAround(searchPos, 5f, 5f, 
                        e => e is EntityAgent);
                    
                    if (nearbyEntities.Length > 0 && nearbyEntities[0] is EntityAgent agent)
                    {
                        nearestAgent = agent;
                    }
                }
            }
            
            return nearestAgent;
        }

        /// <summary>
        /// Apply basic stealth filtering when we don't have observer context
        /// </summary>
        private static bool ApplyBasicStealthFilter(Vec3d searchPos, EntityPlayer player, float originalRange)
        {
            double distance = searchPos.DistanceTo(player.Pos.XYZ);
            
            if (StealthModSystem.IsPlayerSneaking(player))
            {
                // Reduce detection range for sneaking players
                double sneakRange = originalRange * EntityDetectionPatches.SNEAK_DETECTION_MULTIPLIER;
                return distance <= sneakRange;
            }
            
            return distance <= originalRange;
        }
    }
}