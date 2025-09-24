using HarmonyLib;
using Vintagestory.GameContent;
using Vintagestory.API.Client;

namespace AemonEssentials.HandbookMemory
{
    // Minimal patch: prevent GuiDialogHandbook from clearing its state when closed
    [HarmonyPatch]
    public static class HandbookPatches
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(GuiDialogHandbook), "OnGuiClosed")]
        public static bool KeepBrowserHistory(GuiDialogHandbook __instance)
        {
            // Replicate Guibibi behavior: unpause game if needed and clear only the search field
            try
            {
                // Access private fields on the dialog
                var overviewGui = Traverse.Create(__instance).Field("overviewGui").GetValue<GuiComposer>();
                var capi = Traverse.Create(__instance).Field("capi").GetValue<ICoreClientAPI>();

                // Preserve vanilla pause behavior
                if (capi != null && capi.IsSinglePlayer && !capi.OpenedToLan && !capi.Settings.Bool["noHandbookPause"])
                {
                    capi.PauseGame(false);
                }

                // Clear only the search input so the dialog can be reopened cleanly, but keep nav history
                if (overviewGui != null)
                {
                    var searchField = Vintagestory.API.Client.GuiComposerHelpers.GetTextInput(overviewGui, "searchField");
                    if (searchField != null)
                    {
                        ((GuiElementEditableTextBase)searchField).SetValue("", true);
                    }
                }
            }
            catch
            {
                // On any failure, still skip the original to preserve history
            }

            // Returning false prevents the original OnGuiClosed which clears history
            return false;
        }
    }
}