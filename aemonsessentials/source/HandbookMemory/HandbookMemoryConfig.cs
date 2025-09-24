using System;

namespace AemonsEssentials.HandbookMemory
{
    /// <summary>
    /// Configuration settings for the Smart Handbook Memory system.
    /// 
    /// For beginners: This class holds all the settings that control how the 
    /// handbook memory feature works. Think of it as a settings panel that 
    /// lets you turn features on/off and adjust how they behave.
    /// </summary>
    public class HandbookMemoryConfig
    {
        /// <summary>
        /// Master toggle for the entire handbook memory feature.
        /// When false, the handbook behaves exactly like vanilla Vintage Story.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Whether to remember and restore the last manually opened handbook page.
        /// 
        /// For beginners: "Manual" means the player pressed H or clicked the handbook
        /// button, not when they hovered over an item to see its handbook page.
        /// </summary>
        public bool RememberLastPage { get; set; } = true;

        /// <summary>
        /// Maximum number of pages to store in memory per player.
        /// Prevents excessive memory usage in multiplayer scenarios.
        /// 
        /// Performance Note: Higher values use more memory but provide better
        /// user experience for players who frequently switch between pages.
        /// </summary>
        public int MaxStoredPages { get; set; } = 10;

        /// <summary>
        /// Whether to show a "Recently Viewed" category in the handbook UI.
        /// This adds a button/tab that shows the last opened pages for quick access.
        /// </summary>
        public bool ShowRecentlyViewedCategory { get; set; } = true;

        /// <summary>
        /// Number of recent pages to display in the Recently Viewed category.
        /// Should be less than or equal to MaxStoredPages for best results.
        /// </summary>
        public int RecentlyViewedCount { get; set; } = 5;

        /// <summary>
        /// Validates configuration values to prevent crashes or poor performance.
        /// 
        /// For beginners: This method checks that all settings are within safe
        /// ranges. For example, negative page counts don't make sense and could
        /// cause problems.
        /// </summary>
        public bool IsValid()
        {
            // For beginners: We check each setting to make sure it makes sense
            if (MaxStoredPages < 1 || MaxStoredPages > 100)
                return false;

            if (RecentlyViewedCount < 1 || RecentlyViewedCount > MaxStoredPages)
                return false;

            return true;
        }

        /// <summary>
        /// Applies safe defaults for any invalid configuration values.
        /// Called automatically when loading configuration fails validation.
        /// </summary>
        public void ApplyDefaults()
        {
            if (MaxStoredPages < 1 || MaxStoredPages > 100)
                MaxStoredPages = 10;

            if (RecentlyViewedCount < 1 || RecentlyViewedCount > MaxStoredPages)
                RecentlyViewedCount = Math.Min(5, MaxStoredPages);
        }
    }
}