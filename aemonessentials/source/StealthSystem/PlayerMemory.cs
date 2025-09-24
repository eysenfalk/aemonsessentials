/// <summary>
/// Data structure for tracking AI memory of player positions.
/// 
/// For beginners: This class lets each entity "remember" where it last saw a player and for how long.
/// </summary>
using Vintagestory.API.MathTools;
using System;

namespace AemonEssentials.StealthSystem
{
    public class PlayerMemory
    {
        /// <summary>
        /// Last known position of the player.
        /// </summary>
        public Vec3d LastKnownPosition { get; set; }

        /// <summary>
        /// Time (in seconds) when the player was last seen.
        /// </summary>
        public double LastSeenTime { get; set; }
    }
}
