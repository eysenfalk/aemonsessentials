using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using System;
using System.Reflection;
using System.Collections.Generic;

namespace AemonEssentials.Stealth
{
    // Patch entity detection methods to implement stealth mechanics
    [HarmonyPatch]
    public class EntityDetectionPatches
    {
        // Configuration values
        private const double SNEAK_DETECTION_MULTIPLIER = 0.4; // Reduce detection range to 40% when sneaking

        // We need to find the actual AI methods used for entity detection
        // Since I can't see the specific AI task methods in the API, I'll patch a broader approach

        // Patch World.GetEntitiesAround which is commonly used for detection
        [HarmonyPatch(typeof(World), "GetEntitiesAround")]
        [HarmonyPostfix]
        public static void GetEntitiesAround_Postfix(World __instance, Vec3d centerPos, float radius, float height, ref Entity[] __result, ActionConsumable<Entity> matches)
        {
            if (__result == null) return;

            List<Entity> filteredResults = new List<Entity>();

            foreach (Entity entity in __result)
            {
                if (entity is EntityPlayer player && StealthModSystem.IsPlayerSneaking(player))
                {
                    double distance = centerPos.DistanceTo(entity.ServerPos.XYZ);
                    double sneakRadius = radius * SNEAK_DETECTION_MULTIPLIER;

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
                        Vec3d eyePos = centerPos.Add(0, 1.6, 0); // Approximate eye height
                        Vec3d targetEyePos = entity.ServerPos.XYZ.Add(0, entity.Properties.EyeHeight, 0);

                        if (StealthModSystem.HasLineOfSight(__instance, eyePos, targetEyePos))
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

            __result = filteredResults.ToArray();
        }

        // Patch GetEntitiesInsideCuboid for more comprehensive coverage
        [HarmonyPatch(typeof(World), "GetEntitiesInsideCuboid")]
        [HarmonyPostfix]
        public static void GetEntitiesInsideCuboid_Postfix(World __instance, BlockPos start, BlockPos end, ref List<Entity> __result)
        {
            if (__result == null) return;

            Vec3d centerPos = start.ToVec3d().Add(end.ToVec3d()).Mul(0.5); // Approximate center

            for (int i = __result.Count - 1; i >= 0; i--)
            {
                Entity entity = __result[i];

                if (entity is EntityPlayer player)
                {
                    // Apply stealth mechanics to players
                    if (StealthModSystem.IsPlayerSneaking(player))
                    {
                        double distance = centerPos.DistanceTo(entity.ServerPos.XYZ);
                        double maxDistance = Math.Max(
                            Math.Abs(end.X - start.X), 
                            Math.Max(Math.Abs(end.Y - start.Y), Math.Abs(end.Z - start.Z))
                        );
                        double sneakDistance = maxDistance * SNEAK_DETECTION_MULTIPLIER;

                        if (distance > sneakDistance)
                        {
                            __result.RemoveAt(i);
                            continue;
                        }
                    }

                    // Check line-of-sight for all players
                    Vec3d eyePos = centerPos.Add(0, 1.6, 0);
                    Vec3d targetEyePos = entity.ServerPos.XYZ.Add(0, entity.Properties.EyeHeight, 0);

                    if (!StealthModSystem.HasLineOfSight(__instance, eyePos, targetEyePos))
                    {
                        __result.RemoveAt(i);
                    }
                }
            }
        }
    }
}
