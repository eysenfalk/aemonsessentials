using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using System;
using System.Reflection;

namespace AemonEssentials.Stealth
{
    /// <summary>
    /// Simple and direct approach: Override entity detection at the task level
    /// </summary>
    public static class DirectStealthPatches
    {
        /// <summary>
        /// Patch any method that searches for players near entities
        /// We'll intercept at the point where entities decide to flee
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Entity), nameof(Entity.GetNearestEntity))]
        public static bool GetNearestEntity_Prefix(Entity __instance, Vec3d position, float horRange, float vertRange, ActionConsumable<Entity> matches, ref Entity __result)
        {
            // Only intercept if this is an animal looking for players
            if (__instance is not EntityAgent observer) return true;
            
            Console.WriteLine($"[DIRECT STEALTH] GetNearestEntity called by {__instance.Code?.Path}");
            
            // Get all entities in range using original method
            Entity[] entities = __instance.World.GetEntitiesAround(position, horRange, vertRange, matches);
            
            Entity nearestValidEntity = null;
            double nearestDistance = double.MaxValue;
            
            foreach (Entity entity in entities)
            {
                if (entity is EntityPlayer player)
                {
                    // Apply FOV-based stealth filtering
                    if (EntityDetectionPatches.CanEntityDetectPlayer(__instance.World, observer, position, player, horRange))
                    {
                        double distance = position.DistanceTo(entity.Pos.XYZ);
                        if (distance < nearestDistance)
                        {
                            nearestDistance = distance;
                            nearestValidEntity = entity;
                        }
                        
                        Console.WriteLine($"[DIRECT STEALTH] {observer.Code?.Path} CAN detect player (FOV check passed)");
                    }
                    else
                    {
                        Console.WriteLine($"[DIRECT STEALTH] {observer.Code?.Path} CANNOT detect player (FOV/stealth check failed)");
                    }
                }
                else
                {
                    // Keep non-player entities as-is
                    double distance = position.DistanceTo(entity.Pos.XYZ);
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearestValidEntity = entity;
                    }
                }
            }
            
            __result = nearestValidEntity;
            return false; // Skip original method
        }
        
        /// <summary>
        /// Also patch the array version to be thorough
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(IWorldAccessor), nameof(IWorldAccessor.GetEntitiesAround))]
        public static void GetEntitiesAround_Postfix(IWorldAccessor __instance, Vec3d position, float horRange, float vertRange, ActionConsumable<Entity> matches, ref Entity[] __result)
        {
            if (__result == null || __result.Length == 0) return;
            
            // Find if there's a nearby observer (entity that might be looking for players)
            EntityAgent? observer = null;
            foreach (Entity entity in __result)
            {
                if (entity is EntityAgent agent && entity.Pos.DistanceTo(position) < 2.0) // Very close to search position
                {
                    observer = agent;
                    break;
                }
            }
            
            if (observer == null) return; // No observer context
            
            Console.WriteLine($"[DIRECT STEALTH] Filtering GetEntitiesAround results for {observer.Code?.Path}");
            
            var filteredResults = new System.Collections.Generic.List<Entity>();
            
            foreach (Entity entity in __result)
            {
                if (entity is EntityPlayer player)
                {
                    // Apply stealth filtering
                    if (EntityDetectionPatches.CanEntityDetectPlayer(__instance, observer, position, player, horRange))
                    {
                        filteredResults.Add(entity);
                        Console.WriteLine($"[DIRECT STEALTH] Player visible to {observer.Code?.Path}");
                    }
                    else
                    {
                        Console.WriteLine($"[DIRECT STEALTH] Player hidden from {observer.Code?.Path} (stealth/FOV)");
                    }
                }
                else
                {
                    filteredResults.Add(entity); // Keep non-player entities
                }
            }
            
            __result = filteredResults.ToArray();
        }
    }
}