using System;
using Cairo;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ShowCraftable;

public abstract class ButtonFetch : RichTextComponentBase
{
	private class GlobalBounds : ElementBounds
	{
		public override double bgDrawX => base.absFixedX;

		public override double bgDrawY => base.absFixedY;

		public override double renderX => base.absFixedX + base.renderOffsetX;

		public override double renderY => base.absFixedY + base.renderOffsetY;

		public override double absX => base.absFixedX;

		public override double absY => base.absFixedY;

		public GlobalBounds(double x, double y, double width, double height)
		{
			//IL_003d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0047: Expected O, but got Unknown
			base.absFixedX = x;
			base.absFixedY = y;
			base.absInnerWidth = (base.fixedWidth = width);
			base.absInnerHeight = (base.fixedHeight = height);
			((ElementBounds)this).BothSizing = (ElementSizing)0;
			base.ParentBounds = new ElementBounds();
		}
	}

	private const double UnscaledSize = 24.0;

	private const double Margin = 2.0;

	private readonly int index;

	private readonly string label;

	private readonly string tooltip;

	private readonly double offX;

	private readonly double offY;

	private double timeInside;

	private readonly GuiElementTextButton button;

	private readonly GuiElementHoverText hover;

	private readonly ElementBounds bounds;

	public ButtonFetch(ICoreClientAPI api, int index, string label, string key, double offX, double offY)
		: base(api)
	{
		((RichTextComponentBase)this).Float = (EnumFloat)1;
		((RichTextComponentBase)this).VerticalAlign = (EnumVerticalAlign)3;
		this.index = index;
		this.label = label;
		this.offX = offX;
		this.offY = offY;
		tooltip = Lang.Get(key, Array.Empty<object>());
		double num = Math.Ceiling(GuiElement.scaled(24.0));
		bounds = (ElementBounds)(object)new GlobalBounds(0.0, 0.0, num, num);
		button = CreateButton(bounds);
		hover = CreateHover(bounds);
	}

	private GuiElementTextButton CreateButton(ElementBounds bounds)
	{
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Expected O, but got Unknown
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Expected O, but got Unknown
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Expected O, but got Unknown
		CairoFont val = CairoFont.ButtonText();
		CairoFont val2 = CairoFont.ButtonPressedText();
		((FontConfig)val).UnscaledFontsize = (((FontConfig)val2).UnscaledFontsize = GuiElement.scaled(22.0));
		GuiElementTextButton val3 = new GuiElementTextButton(base.api, label, val, val2, new ActionConsumable(Click), bounds, (EnumButtonStyle)3)
		{
			PlaySound = false
		};
		Traverse val4 = Traverse.Create((object)val3);
		AdjustOffsets(val4.Field<GuiElementStaticText>("normalText").Value);
		AdjustOffsets(val4.Field<GuiElementStaticText>("pressedText").Value);
		((GuiElement)val3).ComposeElements((Context)null, (ImageSurface)null);
		return val3;
	}

	private GuiElementHoverText CreateHover(ElementBounds bounds)
	{
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Expected O, but got Unknown
		GuiElementHoverText val = new GuiElementHoverText(base.api, tooltip, CairoFont.WhiteSmallText(), 200, bounds, (TextBackground)null);
		val.SetAutoDisplay(false);
		return val;
	}

	protected abstract void OnClick();

	private bool Click()
	{
		OnClick();
		return true;
	}

	public override EnumCalcBoundsResult CalcBounds(TextFlowPath[] flowPath, double currentLineHeight, double offX, double lineY, out double nextOffsetX)
	{
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Expected O, but got Unknown
		double num = offX - GuiElement.scaled(3.0);
		double num2 = lineY + GuiElement.scaled(0.0 - (double)index * 26.0);
		double num3 = GuiElement.scaled(24.0);
		((RichTextComponentBase)this).BoundsPerLine = (LineRectangled[])(object)new LineRectangled[1]
		{
			new LineRectangled(num, num2, num3, num3)
		};
		bounds.fixedWidth = (bounds.fixedHeight = num3);
		nextOffsetX = offX;
		return (EnumCalcBoundsResult)0;
	}

	private void AdjustOffsets(GuiElementStaticText elem)
	{
		elem.offsetX = GuiElement.scaled(GuiElement.scaled(offX));
		elem.offsetY = GuiElement.scaled(GuiElement.scaled(offY));
	}

	public override void RenderInteractiveElements(float deltaTime, double renderX, double renderY, double renderZ)
	{
		SetBounds(renderX, renderY);
		((GuiElement)button).RenderInteractiveElements(deltaTime);
		hover.SetVisible(MouseOverFor(1.0, deltaTime));
		((GuiElement)hover).RenderInteractiveElements(deltaTime);
	}

	private bool MouseOverFor(double time, double delta)
	{
		if (bounds.PointInside(base.api.Input.MouseX, base.api.Input.MouseY))
		{
			timeInside += delta;
		}
		else
		{
			timeInside = 0.0;
		}
		return timeInside > time;
	}

	private void SetBounds(double xOffset = 0.0, double yOffset = 0.0)
	{
		LineRectangled val = ((RichTextComponentBase)this).BoundsPerLine[0];
		bounds.absInnerWidth = ((Rectangled)val).Width;
		bounds.absInnerHeight = ((Rectangled)val).Height;
		bounds.absFixedX = xOffset + ((Rectangled)val).X;
		bounds.absFixedY = yOffset + ((Rectangled)val).Y;
	}

	public override void OnMouseDown(MouseEvent args)
	{
		SetBounds();
		((GuiElement)button).OnMouseDown(base.api, args);
	}

	public override void OnMouseUp(MouseEvent args)
	{
		SetBounds();
		((GuiElement)button).OnMouseUp(base.api, args);
	}

	public override void OnMouseMove(MouseEvent args)
	{
		button.PlaySound = true;
		((GuiElement)button).OnMouseMove(base.api, args);
		button.PlaySound = false;
	}

	public override void Dispose()
	{
		((GuiElement)button).Dispose();
		((GuiElement)hover).Dispose();
	}
}
