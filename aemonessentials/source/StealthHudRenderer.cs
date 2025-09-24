using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using System;
using System.Text;

namespace AemonEssentials.Stealth
{
    /// <summary>
    /// Simple text-based stealth debug info overlay
    /// </summary>
    public class StealthHudRenderer : ModSystem
    {
        private ICoreClientAPI? capi;
        private long hudUpdateId = 0;
        private StringBuilder infoText = new StringBuilder();

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            
            // Register HUD toggle command - uses chat messages for simplicity
            api.ChatCommands
                .Create("stealthinfo") 
                .WithDescription("Show stealth debug info in chat")
                .HandleWith(OnStealthInfoCmd);
            
            api.Logger.Notification("Stealth Info System loaded - use .stealthinfo to get debug data");
        }

        private TextCommandResult OnStealthInfoCmd(TextCommandCallingArgs args)
        {
            if (capi?.World?.Player?.Entity == null)
            {
                return TextCommandResult.Error("Player not available");
            }

            var player = capi.World.Player.Entity;
            var playerPos = player.Pos.XYZ;
            
            infoText.Clear();
            infoText.AppendLine("=== STEALTH DEBUG INFO ===");
            infoText.AppendLine($"Position: {playerPos.X:F1}, {playerPos.Y:F1}, {playerPos.Z:F1}");
            infoText.AppendLine($"Sneaking: {StealthModSystem.IsPlayerSneaking(player)}");
            
            if (StealthModSystem.IsPlayerSneaking(player))
            {
                infoText.AppendLine($"Detection Multiplier: {EntityDetectionPatches.SNEAK_DETECTION_MULTIPLIER:F2}");
            }
            
            // Check for nearby entities
            var entities = capi.World.GetEntitiesAround(playerPos, 20f, 20f, 
                entity => entity != player && entity is EntityAgent);
            
            infoText.AppendLine($"\n=== ENTITIES ({entities.Length} nearby) ===");
            
            foreach (var entity in entities)
            {
                if (entity is EntityAgent agent)
                {
                    double distance = playerPos.DistanceTo(agent.Pos.XYZ);
                    bool playerSneaking = StealthModSystem.IsPlayerSneaking(player);
                    float normalRange = 15f; // Standard detection range
                    float stealthRange = normalRange * (float)EntityDetectionPatches.SNEAK_DETECTION_MULTIPLIER;
                    
                    bool inNormalRange = distance <= normalRange;
                    bool inStealthRange = distance <= stealthRange;
                    bool hasLOS = StealthModSystem.HasLineOfSight(capi.World, 
                        agent.Pos.XYZ.Add(0, agent.Properties.EyeHeight, 0),
                        playerPos.Add(0, player.Properties.EyeHeight, 0));
                    
                    string status = "HIDDEN";
                    if (!playerSneaking && inNormalRange && hasLOS)
                        status = "DETECTED";
                    else if (playerSneaking && inStealthRange && hasLOS)
                        status = "DETECTED (STEALTH)";
                    else if (!hasLOS)
                        status = "NO LINE OF SIGHT";
                    
                    infoText.AppendLine($"{agent.Code.Path}: {distance:F1}m - {status}");
                    
                    if (distance <= normalRange)
                    {
                        infoText.AppendLine($"  Ranges: Normal={normalRange}m, Stealth={stealthRange:F1}m, LOS={hasLOS}");
                    }
                }
            }
            
            if (entities.Length == 0)
            {
                infoText.AppendLine("No entities in range");
            }
            
            // Send as multiple chat messages (VS has character limits)
            string fullText = infoText.ToString();
            string[] lines = fullText.Split('\n');
            
            foreach (string line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    capi.ShowChatMessage(line);
                }
            }
            
            return TextCommandResult.Success("Stealth info displayed");
        }

        public override void Dispose()
        {
            if (hudUpdateId != 0)
            {
                capi?.Event.UnregisterGameTickListener(hudUpdateId);
            }
        }
    }
}