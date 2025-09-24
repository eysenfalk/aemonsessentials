using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using System;

namespace AemonEssentials.Stealth
{
    /// <summary>
    /// Entity behavior that modifies how entities detect players based on field of view
    /// </summary>
    public class StealthEntityBehavior : EntityBehavior
    {
        public StealthEntityBehavior(Entity entity) : base(entity) { }

        public override string PropertyName()
        {
            return "stealth";
        }

        /// <summary>
        /// Override player detection to use field of view instead of circular detection
        /// </summary>
        public bool CanDetectPlayer(EntityPlayer player)
        {
            if (entity is not EntityAgent agent) return false;

            Vec3d entityPos = entity.Pos.XYZ;
            Vec3d playerPos = player.Pos.XYZ;
            
            // Calculate distance
            float distance = (float)entityPos.DistanceTo(playerPos);
            float detectionRange = 15f; // Base detection range
            
            // Apply sneak detection modifier
            if (StealthModSystem.IsPlayerSneaking(player))
            {
                detectionRange *= (float)EntityDetectionPatches.SNEAK_DETECTION_MULTIPLIER;
            }
            
            // Check if player is within range
            if (distance > detectionRange) return false;
            
            // Check field of view (90 degrees)
            bool inFOV = EntityDetectionPatches.IsPlayerInFieldOfView(agent, entityPos, playerPos);
            if (!inFOV) return false;
            
            // Check line of sight
            bool hasLOS = StealthModSystem.HasLineOfSight(entity.World, entityPos, playerPos);
            if (!hasLOS) return false;
            
            return true;
        }
    }

    /// <summary>
    /// System to register the stealth behavior on all entities
    /// </summary>
    public class StealthBehaviorSystem : ModSystem
    {
        public override void Start(ICoreAPI api)
        {
            // Register the behavior for all entities that can have AI
            api.RegisterEntityBehaviorClass("stealth", typeof(StealthEntityBehavior));
        }
        
        public override void StartServerSide(ICoreServerAPI api)
        {
            // Add stealth behavior to all spawning entities
            api.Event.OnEntitySpawn += OnEntitySpawn;
        }

        private void OnEntitySpawn(Entity entity)
        {
            // Add stealth behavior to creatures that could detect players
            if (entity is EntityAgent && entity.Code?.Domain == "game")
            {
                // Only add to animals/creatures, not players
                if (entity is not EntityPlayer)
                {
                    entity.AddBehavior(new StealthEntityBehavior(entity));
                }
            }
        }
    }
}