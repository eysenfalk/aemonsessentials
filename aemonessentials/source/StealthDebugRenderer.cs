using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using System;
using System.Collections.Generic;

namespace AemonEssentials.Stealth
{
    public class StealthDebugRenderer : ModSystem
    {
        private ICoreClientAPI? capi;
        private bool debugMode = false;
        private long rendererId = 0;

        // Debug settings
        private const float DETECTION_RANGE = 15f; // Default detection range for visualization
        private const float VIEW_CONE_ANGLE = 90f; // Field of view angle in degrees
        private const int VIEW_CONE_SEGMENTS = 16; // Number of segments for smooth cone
        
        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            
            // Register debug toggle command using new ChatCommands API
            api.ChatCommands
                .Create("stealthdebug")
                .WithDescription("Toggle stealth debug visualization")
                .WithArgs(api.ChatCommands.Parsers.OptionalWord("mode"))
                .HandleWith(OnStealthDebugCmd);
            
            // Register renderer
            rendererId = api.Event.RegisterGameTickListener(OnRenderDebug, 50); // 20 FPS for debug overlay
            
            api.Logger.Notification("Stealth Debug Renderer loaded");
        }

        private TextCommandResult OnStealthDebugCmd(TextCommandCallingArgs args)
        {
            string? mode = args.Parsers[0].GetValue() as string;
            
            if (string.IsNullOrEmpty(mode))
            {
                debugMode = !debugMode;
            }
            else
            {
                debugMode = mode.ToLower() == "on" || mode.ToLower() == "true" || mode == "1";
            }
            
            string message = $"Stealth debug mode: {(debugMode ? "ON" : "OFF")}";
            capi?.ShowChatMessage(message);
            
            return TextCommandResult.Success(message);
        }

        private void OnRenderDebug(float deltaTime)
        {
            if (!debugMode || capi?.World?.Player?.Entity == null) return;

            var player = capi.World.Player.Entity;
            var playerPos = player.Pos.XYZ;
            
            // Get all entities around player for debugging
            var entities = capi.World.GetEntitiesAround(playerPos, DETECTION_RANGE * 2, DETECTION_RANGE * 2, 
                entity => entity != player && entity is EntityAgent);

            foreach (var entity in entities)
            {
                if (entity is EntityAgent agent)
                {
                    RenderEntityDetectionDebug(agent, playerPos);
                }
            }
            
            // Render player stealth status
            RenderPlayerStealthStatus(player);
        }

        private void RenderEntityDetectionDebug(EntityAgent entity, Vec3d playerPos)
        {
            var entityPos = entity.Pos.XYZ;
            var eyePos = entityPos.Add(0, entity.Properties.EyeHeight, 0);
            
            // Calculate if player is sneaking
            bool playerSneaking = StealthModSystem.IsPlayerSneaking(capi!.World.Player.Entity);
            float actualRange = playerSneaking ? 
                DETECTION_RANGE * (float)EntityDetectionPatches.SNEAK_DETECTION_MULTIPLIER : 
                DETECTION_RANGE;
            
            // Render detection sphere
            RenderDetectionSphere(entityPos, actualRange, playerSneaking);
            
            // Render view cone
            RenderViewCone(entity, eyePos);
            
            // Render line of sight to player
            RenderLineOfSight(eyePos, playerPos.Add(0, capi.World.Player.Entity.Properties.EyeHeight, 0));
            
            // Render entity info
            RenderEntityInfo(entity, entityPos, playerPos, actualRange);
        }

        private void RenderDetectionSphere(Vec3d center, float radius, bool stealthMode)
        {
            int color = stealthMode ? ColorUtil.ToRgba(100, 0, 255, 0) : ColorUtil.ToRgba(100, 255, 0, 0); // Green normal, Blue stealth
            
            // Render horizontal circle
            RenderCircle(center, radius, color, true);
            
            // Render vertical circle
            RenderCircle(center, radius, color, false);
        }

        private void RenderCircle(Vec3d center, float radius, int color, bool horizontal)
        {
            var points = new List<Vec3d>();
            int segments = 32;
            
            for (int i = 0; i <= segments; i++)
            {
                float angle = (float)(i * 2 * Math.PI / segments);
                Vec3d point;
                
                if (horizontal)
                {
                    point = center.Add(Math.Cos(angle) * radius, 0, Math.Sin(angle) * radius);
                }
                else
                {
                    point = center.Add(Math.Cos(angle) * radius, Math.Sin(angle) * radius, 0);
                }
                
                points.Add(point);
            }
            
            // Render lines between consecutive points
            if (capi?.World?.Player?.Entity != null)
            {
                var origin = new BlockPos((int)capi.World.Player.Entity.Pos.X, (int)capi.World.Player.Entity.Pos.Y, (int)capi.World.Player.Entity.Pos.Z);
                
                for (int i = 0; i < points.Count - 1; i++)
                {
                    var pos1 = points[i];
                    var pos2 = points[i + 1];
                    
                    capi.Render.RenderLine(origin, 
                        (float)pos1.X, (float)pos1.Y, (float)pos1.Z,
                        (float)pos2.X, (float)pos2.Y, (float)pos2.Z, 
                        color);
                }
            }
        }

        private void RenderViewCone(EntityAgent entity, Vec3d eyePos)
        {
            // Get entity's look direction
            float yaw = entity.BodyYaw;
            Vec3d forward = new Vec3d(Math.Sin(yaw), 0, Math.Cos(yaw));
            
            float halfAngle = VIEW_CONE_ANGLE * GameMath.DEG2RAD * 0.5f;
            float coneRange = DETECTION_RANGE * 1.5f;
            
            int color = ColorUtil.ToRgba(80, 255, 255, 0); // Yellow cone
            
            // Render cone edges
            if (capi?.World?.Player?.Entity != null)
            {
                var origin = new BlockPos((int)capi.World.Player.Entity.Pos.X, (int)capi.World.Player.Entity.Pos.Y, (int)capi.World.Player.Entity.Pos.Z);
                
                for (int i = 0; i <= VIEW_CONE_SEGMENTS; i++)
                {
                    float segmentAngle = yaw - halfAngle + (halfAngle * 2 * i / VIEW_CONE_SEGMENTS);
                    Vec3d rayDir = new Vec3d(Math.Sin(segmentAngle), 0, Math.Cos(segmentAngle));
                    Vec3d endPos = eyePos.Add(rayDir.Mul(coneRange));
                    
                    capi.Render.RenderLine(origin,
                        (float)eyePos.X, (float)eyePos.Y, (float)eyePos.Z,
                        (float)endPos.X, (float)endPos.Y, (float)endPos.Z, 
                        color);
                    
                    // Connect cone edges
                    if (i > 0)
                    {
                        float prevAngle = yaw - halfAngle + (halfAngle * 2 * (i - 1) / VIEW_CONE_SEGMENTS);
                        Vec3d prevRayDir = new Vec3d(Math.Sin(prevAngle), 0, Math.Cos(prevAngle));
                        Vec3d prevEndPos = eyePos.Add(prevRayDir.Mul(coneRange));
                        
                        capi.Render.RenderLine(origin,
                            (float)endPos.X, (float)endPos.Y, (float)endPos.Z,
                            (float)prevEndPos.X, (float)prevEndPos.Y, (float)prevEndPos.Z,
                            color);
                    }
                }
            }
        }

        private void RenderLineOfSight(Vec3d fromPos, Vec3d toPos)
        {
            if (capi?.World?.Player?.Entity == null) return;
            
            bool hasLOS = StealthModSystem.HasLineOfSight(capi.World, fromPos, toPos);
            int color = hasLOS ? 
                ColorUtil.ToRgba(255, 0, 255, 0) :  // Green = clear LOS
                ColorUtil.ToRgba(255, 255, 0, 0);   // Red = blocked LOS
                
            var origin = new BlockPos((int)capi.World.Player.Entity.Pos.X, (int)capi.World.Player.Entity.Pos.Y, (int)capi.World.Player.Entity.Pos.Z);
            capi.Render.RenderLine(origin,
                (float)fromPos.X, (float)fromPos.Y, (float)fromPos.Z,
                (float)toPos.X, (float)toPos.Y, (float)toPos.Z,
                color);
        }

        private void RenderPlayerStealthStatus(EntityPlayer player)
        {
            var playerPos = player.Pos.XYZ;
            bool sneaking = StealthModSystem.IsPlayerSneaking(player);
            
            if (sneaking)
            {
                // Render stealth indicator above player
                var indicatorPos = playerPos.Add(0, player.Properties.EyeHeight + 1, 0);
                int stealthColor = ColorUtil.ToRgba(200, 100, 100, 255); // Purple stealth indicator
                
                // Render a small circle above the player when sneaking
                RenderCircle(indicatorPos, 0.5f, stealthColor, true);
            }
        }

        private void RenderEntityInfo(EntityAgent entity, Vec3d entityPos, Vec3d playerPos, float detectionRange)
        {
            double distance = entityPos.DistanceTo(playerPos);
            bool inRange = distance <= detectionRange;
            bool playerSneaking = StealthModSystem.IsPlayerSneaking(capi!.World.Player.Entity);
            
            // Simple text rendering would require more complex setup
            // For now, use color-coded lines to indicate status
            Vec3d infoPos = entityPos.Add(0, entity.Properties.EyeHeight + 0.5, 0);
            Vec3d infoEndPos = infoPos.Add(0, 0.5, 0);
            
            int statusColor;
            if (playerSneaking && !inRange)
            {
                statusColor = ColorUtil.ToRgba(255, 0, 255, 0); // Green - not detected due to stealth
            }
            else if (inRange)
            {
                statusColor = ColorUtil.ToRgba(255, 255, 100, 0); // Orange - in detection range
            }
            else
            {
                statusColor = ColorUtil.ToRgba(255, 128, 128, 128); // Gray - out of range
            }
            
            if (capi?.World?.Player?.Entity != null)
            {
                var origin = new BlockPos((int)capi.World.Player.Entity.Pos.X, (int)capi.World.Player.Entity.Pos.Y, (int)capi.World.Player.Entity.Pos.Z);
                capi.Render.RenderLine(origin,
                    (float)infoPos.X, (float)infoPos.Y, (float)infoPos.Z,
                    (float)infoEndPos.X, (float)infoEndPos.Y, (float)infoEndPos.Z,
                    statusColor);
            }
        }

        public override void Dispose()
        {
            if (rendererId != 0)
            {
                capi?.Event.UnregisterGameTickListener(rendererId);
            }
        }
    }
}