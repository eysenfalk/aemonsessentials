using System;
using GuibibiQOLS.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace GuibibiQOLS;

public class Notepad : GuiDialog
{
	private ModData _modData;

	private const string NotepadTextArea = "NotePadTextArea";

	public override string ToggleKeyCombinationCode { get; }

	public Notepad(ICoreClientAPI capi)
		: base(capi)
	{
		SetupDialog();
	}

	private void SetupDialog()
	{
		ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment((EnumDialogArea)2);
		ElementBounds textBounds = ElementBounds.Fixed(0.0, 40.0, 350.0, 430.0);
		ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
		bgBounds.BothSizing = (ElementSizing)2;
		bgBounds.WithChildren((ElementBounds[])(object)new ElementBounds[1] { textBounds });
		((GuiDialog)this).SingleComposer = GuiComposerHelpers.AddTextArea(GuiComposerHelpers.AddDialogTitleBar(GuiComposerHelpers.AddShadedDialogBG(base.capi.Gui.CreateCompo("notepadDialog", dialogBounds), bgBounds, true, 5.0, 0.75f), "Notepad", (Action)OnTitleBarCloseClicked, (CairoFont)null, (ElementBounds)null), textBounds, (Action<string>)null, CairoFont.WhiteDetailText(), "NotePadTextArea").Compose(true);
	}

	public override void OnGuiOpened()
	{
		((GuiDialog)this).OnGuiOpened();
		_modData = ((ICoreAPI)(object)base.capi).LoadOrCreateDataFile<ModData>($"{((ICoreAPICommon)base.capi).DataBasePath}/ModData/{((ICoreAPI)(object)base.capi).GetWorldId()}/GuibibiQOLS/{"guibibiQOL.json"}", ((ICoreAPI)base.capi).Logger);
		string savedContent = _modData.NotepadContent;
		((GuiElementEditableTextBase)GuiComposerHelpers.GetTextArea(((GuiDialog)this).SingleComposer, "NotePadTextArea")).SetValue(savedContent, true);
	}

	public override void OnGuiClosed()
	{
		((GuiDialog)this).OnGuiClosed();
		string content = ((GuiElementTextBase)GuiComposerHelpers.GetTextArea(((GuiDialog)this).SingleComposer, "NotePadTextArea")).GetText();
		_modData.NotepadContent = content;
		((ICoreAPI)(object)base.capi).SaveDataFile($"{((ICoreAPICommon)base.capi).DataBasePath}/ModData/{((ICoreAPI)(object)base.capi).GetWorldId()}/GuibibiQOLS/{"guibibiQOL.json"}", _modData, ((ICoreAPI)base.capi).Logger);
	}

	private void OnTitleBarCloseClicked()
	{
		((GuiDialog)this).TryClose();
	}
}
