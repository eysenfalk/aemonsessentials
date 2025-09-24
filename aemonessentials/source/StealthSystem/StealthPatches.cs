/// <summary>
/// Harmony patches for entity detection logic to implement stealth mechanics.
/// 
/// For beginners: Harmony lets us "patch" (modify) game code at runtime to add new features or change behavior.
/// </summary>
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using System;

namespace AemonEssentials.StealthSystem
{
    [HarmonyPatch]
    public static class StealthPatches
    {
        private static StealthModSystem? stealthSystem;

        /// <summary>
        /// Initialize patches with reference to the stealth system.
        /// For beginners: This lets our patches access configuration and memory management.
        /// </summary>
        public static void Initialize(StealthModSystem system)
        {
            stealthSystem = system;
        }

        /// <summary>
        /// Note: Removed specific AI behavior patching as those classes may not be publicly accessible.
        /// For beginners: We'll focus on the entity detection method which is more reliable.
        /// </summary>

        /// <summary>
        /// Patches the entity seek target method to implement line-of-sight and sneak detection.
        /// For beginners: This changes how entities find and target players based on stealth mechanics.
        /// </summary>
        [HarmonyPatch(typeof(Entity), "GetNearestEntity")]
        [HarmonyPostfix]
        public static void Patch_Entity_GetNearestEntity(Entity __instance, ref Entity __result, 
            Vec3d position, float horRange, float vertRange, ActionConsumable<Entity> matches)
        {
            // For beginners: This runs after the original method to modify the result
            if (stealthSystem?.Config?.Enabled != true) return;
            if (__result?.Api?.Side != EnumAppSide.Server) return;
            
            // Check if the found entity is a player
            if (__result is EntityPlayer player)
            {
                // Apply stealth mechanics
                if (!CanEntityDetectPlayer(__instance, player, horRange))
                {
                    __result = null!; // Hide player from entity
                }
            }
        }

        /// <summary>
        /// Core stealth detection logic that combines sneak detection and line-of-sight.
        /// For beginners: This is where all our stealth rules come together.
        /// </summary>
        private static bool CanEntityDetectPlayer(Entity entity, EntityPlayer player, float baseRange)
        {
            if (stealthSystem?.Config == null) return true;

            var config = stealthSystem.Config;
            var world = entity.World;
            
            // 1. Calculate distance between entity and player
            double distance = entity.Pos.DistanceTo(player.Pos);
            
            // 2. Check if player is sneaking and apply sneak multiplier
            bool isSneaking = StealthUtils.IsPlayerSneaking(player.Player);
            double effectiveRange = StealthUtils.CalculateSneakDetectionRange(baseRange, config.SneakDetectionMultiplier, isSneaking);
            
            // 3. If player is outside effective range, no detection
            if (distance > effectiveRange)
            {
                UpdateAIMemory(entity, player, false);
                return false;
            }
            
            // 4. Check line of sight if enabled
            if (config.EnableLineOfSight)
            {
                Vec3d entityEyePos = entity.Pos.XYZ.AddCopy(0, entity.LocalEyePos.Y, 0);
                Vec3d playerEyePos = player.Pos.XYZ.AddCopy(0, player.LocalEyePos.Y, 0);
                
                bool hasLineOfSight = StealthUtils.HasLineOfSight(world, entityEyePos, playerEyePos);
                
                if (!hasLineOfSight)
                {
                    // No line of sight - check AI memory
                    return HandleMemoryBasedDetection(entity, player);
                }
            }
            
            // 5. Player detected - update memory
            UpdateAIMemory(entity, player, true);
            return true;
        }

        /// <summary>
        /// Handles detection when line of sight is blocked but entity has memory.
        /// For beginners: This makes entities remember where they last saw a player.
        /// </summary>
        private static bool HandleMemoryBasedDetection(Entity entity, EntityPlayer player)
        {
            if (stealthSystem == null) return false;
            var memory = stealthSystem.MemoryManager.GetMemory(entity.EntityId);
            double currentTime = entity.World.Calendar.TotalHours * 3600; // Convert to seconds
            
            if (memory != null)
            {
                double timeSinceLastSeen = currentTime - memory.LastSeenTime;
                if (timeSinceLastSeen <= stealthSystem.Config.AIMemoryDurationSeconds)
                {
                    // Entity still remembers player
                    if (stealthSystem.Config.DebugLogging)
                    {
                        entity.World.Logger.Debug($"Entity {entity.EntityId} remembers player at {memory.LastKnownPosition}");
                    }
                    return true;
                }
            }
            
            return false; // No memory or memory expired
        }

        /// <summary>
        /// Updates AI memory when a player is seen or lost.
        /// For beginners: This records where and when the entity last saw a player.
        /// </summary>
        private static void UpdateAIMemory(Entity entity, EntityPlayer player, bool canSeePlayer)
        {
            if (stealthSystem?.Config?.EnableLineOfSight != true) return;
            
            double currentTime = entity.World.Calendar.TotalHours * 3600; // Convert to seconds
            
            if (canSeePlayer)
            {
                // Update memory with current player position
                var memory = new PlayerMemory
                {
                    LastKnownPosition = player.Pos.XYZ.Clone(),
                    LastSeenTime = currentTime
                };
                stealthSystem.MemoryManager.RememberPlayer(entity.EntityId, memory);
            }
        }
    }
}
