using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using System;
using System.Collections.Generic;

namespace AemonEssentials.Stealth
{
    /// <summary>
    /// Simple block highlight approach for debugging stealth ranges
    /// </summary>
    public class StealthBlockHighlighter : ModSystem
    {
        private ICoreClientAPI? capi;
        private bool debugMode = false;
        private long updateId = 0;

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;

            // Register debug toggle command
            api.ChatCommands
                .Create("stealthblocks")
                .WithDescription("Highlight blocks around entities for stealth debugging")
                .WithArgs(api.ChatCommands.Parsers.OptionalWord("mode"))
                .HandleWith(OnStealthBlocksCmd);

            // Add FOV test command
            api.ChatCommands
                .Create("testfov")
                .WithDescription("Test FOV detection for nearby entities")
                .HandleWith(OnTestFovCmd);            // Update highlights more frequently to see movement
            updateId = api.Event.RegisterGameTickListener(UpdateHighlights, 250); // 4 times per second

            api.Logger.Notification("Stealth Block Highlighter loaded");
        }

        private TextCommandResult OnStealthBlocksCmd(TextCommandCallingArgs args)
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

            if (!debugMode)
            {
                // Clear all highlights
                capi?.World.HighlightBlocks(capi.World.Player, 0, new List<BlockPos>(), EnumHighlightBlocksMode.Absolute);
            }

            string message = $"Stealth block highlighting: {(debugMode ? "ON - Pink blocks show detection range!" : "OFF")}";
            capi?.ShowChatMessage(message);

            return TextCommandResult.Success(message);
        }

        private TextCommandResult OnTestFovCmd(TextCommandCallingArgs args)
        {
            if (capi?.World?.Player?.Entity == null) return TextCommandResult.Error("Player not available");

            var player = capi.World.Player.Entity;
            var playerPos = player.Pos.XYZ;

            // Find nearby entities
            var entities = capi.World.GetEntitiesAround(playerPos, 30f, 30f,
                entity => entity != player && entity is EntityAgent);

            if (entities.Length == 0)
            {
                capi.ShowChatMessage("No entities found nearby for FOV testing");
                return TextCommandResult.Success("No entities nearby");
            }

            foreach (var entity in entities)
            {
                if (entity is EntityAgent agent)
                {
                    // Test FOV detection with correct parameters
                    Vec3d entityPos = entity.Pos.XYZ;
                    bool inFOV = EntityDetectionPatches.IsPlayerInFieldOfView(agent, entityPos, playerPos);
                    float distance = (float)entityPos.DistanceTo(playerPos);
                    float yaw = entity.ServerPos.Yaw * 180f / (float)Math.PI; // Convert to degrees

                    string entityName = entity.Code?.Path ?? "unknown";
                    string fovStatus = inFOV ? "CAN SEE" : "CANNOT SEE";

                    capi.ShowChatMessage($"Entity {entityName}: {fovStatus} (dist: {distance:F1}, yaw: {yaw:F0}°)");
                    
                    // Additional debug info
                    capi.ShowChatMessage($"  Entity pos: {entityPos.X:F1}, {entityPos.Y:F1}, {entityPos.Z:F1}");
                    capi.ShowChatMessage($"  Player pos: {playerPos.X:F1}, {playerPos.Y:F1}, {playerPos.Z:F1}");
                }
            }

            return TextCommandResult.Success($"Tested FOV for {entities.Length} entities");
        }

        private void UpdateHighlights(float deltaTime)
        {
            if (!debugMode || capi?.World?.Player?.Entity == null) return;

            var player = capi.World.Player.Entity;
            var playerPos = player.Pos.XYZ;
            var blocksToHighlight = new List<BlockPos>();

            // Get all entities around player
            var entities = capi.World.GetEntitiesAround(playerPos, 30f, 30f,
                entity => entity != player);

            foreach (var entity in entities)
            {
                var entityPos = entity.Pos.XYZ;
                float visionRange = 15f; // Vision range

                if (StealthModSystem.IsPlayerSneaking(player))
                {
                    visionRange *= (float)EntityDetectionPatches.SNEAK_DETECTION_MULTIPLIER;
                }

                // ONLY show vision cone - no circular detection
                if (entity is EntityAgent agent)
                {
                    var centerBlock = new BlockPos((int)entityPos.X, (int)entityPos.Y, (int)entityPos.Z);
                    AddVisionCone(blocksToHighlight, agent, centerBlock, visionRange);
                }
            }

            // If no entities, create a test pattern around player
            if (entities.Length == 0)
            {
                var playerBlock = new BlockPos((int)playerPos.X, (int)playerPos.Y, (int)playerPos.Z);

                // Create a small test circle 5 blocks away
                var testCenter = playerBlock.AddCopy(5, 0, 0);
                for (int angle = 0; angle < 360; angle += 20)
                {
                    double rad = angle * Math.PI / 180;
                    int x = (int)(Math.Cos(rad) * 3);
                    int z = (int)(Math.Sin(rad) * 3);
                    blocksToHighlight.Add(testCenter.AddCopy(x, 0, z));
                }
            }

            // Apply highlights - should show as pink/red outlined blocks
            if (blocksToHighlight.Count > 0)
            {
                capi.World.HighlightBlocks(capi.World.Player, 1, blocksToHighlight, EnumHighlightBlocksMode.Absolute,
                    EnumHighlightShape.Arbitrary);
            }
        }

        private void AddVisionCone(List<BlockPos> blocksToHighlight, EntityAgent entity, BlockPos centerBlock, float maxRange)
        {
            // Try different yaw sources - ServerPos.Yaw updates with movement
            float headYaw = entity.ServerPos.Yaw; // This should update when entity moves/turns

            // Debug: Show yaw values in chat occasionally
            if (capi != null && DateTime.Now.Millisecond < 50) // Show once per second roughly
            {
                capi.ShowChatMessage($"Entity Yaw - ServerPos: {entity.ServerPos.Yaw:F2}, BodyYaw: {entity.BodyYaw:F2}");
            }

            // DEBUG: Add a test block above entity with direction indicator
            blocksToHighlight.Add(centerBlock.AddCopy(0, 1, 0)); // Block above entity

            // Add direction indicator - a block in the direction the entity is facing
            Vec3d directionIndicator = new Vec3d(Math.Sin(headYaw), 0, Math.Cos(headYaw));
            Vec3d indicatorPos = new Vec3d(centerBlock.X, centerBlock.Y + 1, centerBlock.Z).Add(directionIndicator.Mul(2));
            BlockPos indicatorBlock = new BlockPos((int)indicatorPos.X, (int)indicatorPos.Y, (int)indicatorPos.Z);
            blocksToHighlight.Add(indicatorBlock);

            // 90-degree FOV (45 degrees each side) - realistic vision cone
            float fovAngle = 90f * (float)Math.PI / 180f; // Convert to radians
            float halfFov = fovAngle * 0.5f;

            // Draw the vision cone
            int rayCount = 5; // Number of rays in cone

            for (int i = 0; i <= rayCount; i++)
            {
                // Calculate angle for this ray
                float rayAngle = headYaw - halfFov + (fovAngle * i / rayCount);

                // Draw ray from entity center outward
                Vec3d rayDirection = new Vec3d(Math.Sin(rayAngle), 0, Math.Cos(rayAngle));

                // Draw blocks along the ray - every 2 blocks
                for (float distance = 2f; distance <= maxRange; distance += 2f)
                {
                    Vec3d rayPos = new Vec3d(centerBlock.X, centerBlock.Y, centerBlock.Z).Add(rayDirection.Mul(distance));
                    BlockPos rayBlock = new BlockPos((int)rayPos.X, (int)rayPos.Y, (int)rayPos.Z);
                    blocksToHighlight.Add(rayBlock);
                }
            }

            // Add center line showing exact facing direction
            Vec3d forward = new Vec3d(Math.Sin(headYaw), 0, Math.Cos(headYaw));
            for (int i = 1; i <= 5; i++)
            {
                Vec3d frontPos = new Vec3d(centerBlock.X, centerBlock.Y, centerBlock.Z).Add(forward.Mul(i * 2));
                BlockPos frontBlock = new BlockPos((int)frontPos.X, (int)frontPos.Y, (int)frontPos.Z);
                blocksToHighlight.Add(frontBlock);
            }
        }

        public override void Dispose()
        {
            if (updateId != 0)
            {
                capi?.Event.UnregisterGameTickListener(updateId);
            }
        }
    }
}