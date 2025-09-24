/// <summary>
/// Manages AI memory for all entities in the world.
/// 
/// For beginners: This class keeps track of which entities remember which players, and cleans up old memories.
/// </summary>
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace AemonEssentials.StealthSystem
{
    public class AIMemoryManager
    {
        // For beginners: Dictionary lets us look up memory for each entity
        private readonly Dictionary<long, PlayerMemory> entityMemories = new();

        /// <summary>
        /// Updates or sets the memory for an entity.
        /// </summary>
        public void RememberPlayer(long entityId, PlayerMemory memory)
        {
            entityMemories[entityId] = memory;
        }

        /// <summary>
        /// Gets the memory for an entity, or null if none exists.
        /// </summary>
        public PlayerMemory GetMemory(long entityId)
        {
            entityMemories.TryGetValue(entityId, out var memory);
            return memory;
        }

        /// <summary>
        /// Removes memories older than the specified duration.
        /// </summary>
        public void Cleanup(double currentTime, double memoryDuration)
        {
            var toRemove = new List<long>();
            foreach (var kvp in entityMemories)
            {
                if (currentTime - kvp.Value.LastSeenTime > memoryDuration)
                    toRemove.Add(kvp.Key);
            }
            foreach (var id in toRemove)
                entityMemories.Remove(id);
        }
    }
}
