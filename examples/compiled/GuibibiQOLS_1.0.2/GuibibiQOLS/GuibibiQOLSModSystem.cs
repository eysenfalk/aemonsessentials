using System;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace GuibibiQOLS;

[HarmonyPatch]
public class GuibibiQOLSModSystem : ModSystem
{
	public ICoreAPI api;

	public ICoreClientAPI capi;

	private GuiDialog dialog;

	public Harmony harmony;

	public override void Start(ICoreAPI api)
	{
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Expected O, but got Unknown
		((ModSystem)this).Start(api);
		this.api = api;
		api.Logger.Notification("Loaded Guibibi's QOL");
		harmony = new Harmony("GuibibiQOLS");
		harmony.PatchAll();
	}

	public override void StartServerSide(ICoreServerAPI api)
	{
		((ICoreAPI)api).Logger.Notification("Hello from template mod server side: " + Lang.Get("GuibibiQOLS:hello", Array.Empty<object>()));
	}

	public override void StartClientSide(ICoreClientAPI api)
	{
		((ModSystem)this).StartClientSide(api);
		capi = api;
		dialog = (GuiDialog)(object)new Notepad(api);
		capi.Input.RegisterHotKey("notepad", "The Notepad", (GlKeys)96, (HotkeyType)2, false, false, false);
		capi.Input.SetHotKeyHandler("notepad", (ActionConsumable<KeyCombination>)ToggleGui);
	}

	private bool ToggleGui(KeyCombination comb)
	{
		if (dialog.IsOpened())
		{
			dialog.TryClose();
		}
		else
		{
			dialog.TryOpen();
		}
		return true;
	}

	public override void Dispose()
	{
		((ModSystem)this).Dispose();
		harmony.UnpatchAll("GuibibiQOLS");
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(GuiDialogHandbook), "OnGuiClosed")]
	public static bool KeepBrowserHistory(GuiDialogHandbook __instance)
	{
		GuiComposer value = Traverse.Create((object)__instance).Field("overviewGui").GetValue<GuiComposer>();
		ICoreClientAPI capi = Traverse.Create((object)__instance).Field("capi").GetValue<ICoreClientAPI>();
		if (capi.IsSinglePlayer && !capi.OpenedToLan && !capi.Settings.Bool["noHandbookPause"])
		{
			capi.PauseGame(false);
		}
		((GuiElementEditableTextBase)GuiComposerHelpers.GetTextInput(value, "searchField")).SetValue("", true);
		return false;
	}
}
