using System;
using Cairo;
using Vintagestory.API.Client;

namespace ShowCraftable;

internal sealed class GuiElementVerticalTabsWithCustomFonts : GuiElementVerticalTabs
{
	private readonly string _defaultFontName;

	private readonly string _defaultSelectedFontName;

	private readonly FontWeight _defaultFontWeight;

	private readonly FontWeight _defaultSelectedFontWeight;

	private double[] _textOffsets = Array.Empty<double>();

	public GuiElementVerticalTabsWithCustomFonts(ICoreClientAPI capi, GuiTab[] tabs, CairoFont font, CairoFont selectedFont, ElementBounds bounds, Action<int, GuiTab> onTabClicked)
		: base(capi, tabs, font, selectedFont, bounds, onTabClicked)
	{
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		_defaultFontName = ((FontConfig)font).Fontname;
		_defaultSelectedFontName = ((FontConfig)selectedFont).Fontname;
		_defaultFontWeight = ((FontConfig)font).FontWeight;
		_defaultSelectedFontWeight = ((FontConfig)selectedFont).FontWeight;
	}

	public override void ComposeTextElements(Context ctxStatic, ImageSurface surfaceStatic)
	{
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Expected O, but got Unknown
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Expected O, but got Unknown
		//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_0108: Unknown result type (might be due to invalid IL or missing references)
		//IL_010d: Unknown result type (might be due to invalid IL or missing references)
		//IL_033e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0343: Unknown result type (might be due to invalid IL or missing references)
		((GuiElement)this).Bounds.CalcWorldBounds();
		ImageSurface val = new ImageSurface((Format)0, (int)((GuiElement)this).Bounds.InnerWidth + 1, (int)((GuiElement)this).Bounds.InnerHeight + 1);
		try
		{
			Context val2 = new Context((Surface)(object)val);
			try
			{
				double num = GuiElement.scaled(1.0);
				double num2 = GuiElement.scaled(base.unscaledTabSpacing);
				double num3 = GuiElement.scaled(base.unscaledTabPadding);
				base.tabHeight = GuiElement.scaled(base.unscaledTabHeight);
				((FontConfig)((GuiElementTextBase)this).Font).Color[3] = 0.85;
				double num4 = base.tabHeight + 1.0;
				((GuiElementTextBase)this).Font.SetupContext(val2);
				FontExtents fontExtents = ((GuiElementTextBase)this).Font.GetFontExtents();
				double num5 = (num4 - ((FontExtents)(ref fontExtents)).Height) / 2.0;
				double num6 = 0.0;
				for (int i = 0; i < base.tabs.Length; i++)
				{
					ApplyRegularFontForTab(base.tabs[i]);
					((GuiElementTextBase)this).Font.SetupContext(val2);
					TextExtents val3 = val2.TextExtents(base.tabs[i].Name ?? string.Empty);
					double num7 = ((TextExtents)(ref val3)).Width + 1.0 + 2.0 * num3;
					if (num7 > num6)
					{
						num6 = num7;
					}
				}
				RestoreRegularFont();
				_textOffsets = new double[base.tabs.Length];
				double num8 = 0.0;
				for (int j = 0; j < base.tabs.Length; j++)
				{
					base.tabWidths[j] = (int)num6 + 1;
					num8 += base.tabs[j].PaddingTop;
					val2.NewPath();
					double num9;
					if (base.Right)
					{
						num9 = 1.0;
						val2.MoveTo(num9, num8 + base.tabHeight);
						val2.LineTo(num9, num8);
						val2.LineTo(num9 + (double)base.tabWidths[j] + num, num8);
						val2.ArcNegative(num9 + (double)base.tabWidths[j], num8 + num, num, 4.71238899230957, 3.1415927410125732);
						val2.ArcNegative(num9 + (double)base.tabWidths[j], num8 - num + base.tabHeight, num, 3.1415927410125732, 1.5707963705062866);
					}
					else
					{
						num9 = (int)((GuiElement)this).Bounds.InnerWidth + 1;
						val2.MoveTo(num9, num8 + base.tabHeight);
						val2.LineTo(num9, num8);
						val2.LineTo(num9 - (double)base.tabWidths[j] + num, num8);
						val2.ArcNegative(num9 - (double)base.tabWidths[j], num8 + num, num, 4.71238899230957, 3.1415927410125732);
						val2.ArcNegative(num9 - (double)base.tabWidths[j], num8 - num + base.tabHeight, num, 3.1415927410125732, 1.5707963705062866);
					}
					val2.ClosePath();
					double[] dialogDefaultBgColor = GuiStyle.DialogDefaultBgColor;
					val2.SetSourceRGBA(dialogDefaultBgColor[0], dialogDefaultBgColor[1], dialogDefaultBgColor[2], dialogDefaultBgColor[3]);
					val2.FillPreserve();
					((GuiElement)this).ShadePath(val2, 2.0);
					ApplyRegularFontForTab(base.tabs[j]);
					((GuiElementTextBase)this).Font.SetupContext(val2);
					FontExtents fontExtents2 = ((GuiElementTextBase)this).Font.GetFontExtents();
					double num10 = (num4 - ((FontExtents)(ref fontExtents2)).Height) / 2.0;
					if (double.IsNaN(num10))
					{
						num10 = num5;
					}
					_textOffsets[j] = num10;
					double num11 = num9 - (double)((!base.Right) ? base.tabWidths[j] : 0) + num3;
					((GuiElementTextBase)this).DrawTextLineAt(val2, base.tabs[j].Name ?? string.Empty, num11, num8 + num10, false);
					num8 += base.tabHeight + num2;
				}
				RestoreRegularFont();
				((FontConfig)((GuiElementTextBase)this).Font).Color[3] = 1.0;
				ComposeOverlaysWithCustomFonts();
				((GuiElement)this).generateTexture(val, ref base.baseTexture, true);
			}
			finally
			{
				((IDisposable)val2)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	private void ComposeOverlaysWithCustomFonts()
	{
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Expected O, but got Unknown
		double num = GuiElement.scaled(1.0);
		double num2 = GuiElement.scaled(base.unscaledTabPadding);
		for (int i = 0; i < base.tabs.Length; i++)
		{
			ImageSurface val = new ImageSurface((Format)0, base.tabWidths[i] + 1, (int)base.tabHeight + 1);
			try
			{
				Context val2 = ((GuiElement)this).genContext(val);
				try
				{
					double num3 = base.tabWidths[i] + 1;
					val2.SetSourceRGBA(1.0, 1.0, 1.0, 0.0);
					val2.Paint();
					val2.NewPath();
					val2.MoveTo(num3, base.tabHeight + 1.0);
					val2.LineTo(num3, 0.0);
					val2.LineTo(num, 0.0);
					val2.ArcNegative(0.0, num, num, 4.71238899230957, 3.1415927410125732);
					val2.ArcNegative(0.0, base.tabHeight - num, num, 3.1415927410125732, 1.5707963705062866);
					val2.ClosePath();
					double[] dialogDefaultBgColor = GuiStyle.DialogDefaultBgColor;
					val2.SetSourceRGBA(dialogDefaultBgColor[0], dialogDefaultBgColor[1], dialogDefaultBgColor[2], dialogDefaultBgColor[3]);
					val2.Fill();
					val2.NewPath();
					if (base.Right)
					{
						val2.LineTo(1.0, 1.0);
						val2.LineTo(num3, 1.0);
						val2.LineTo(num3, base.tabHeight);
						val2.LineTo(1.0, base.tabHeight - 1.0);
					}
					else
					{
						val2.LineTo(1.0 + num3, 1.0);
						val2.LineTo(1.0, 1.0);
						val2.LineTo(1.0, base.tabHeight - 1.0);
						val2.LineTo(1.0 + num3, base.tabHeight);
					}
					val2.SetSourceRGBA(GuiStyle.DialogLightBgColor[0] * 1.6, GuiStyle.DialogStrongBgColor[1] * 1.6, GuiStyle.DialogStrongBgColor[2] * 1.6, 1.0);
					val2.LineWidth = 3.5;
					val2.StrokePreserve();
					SurfaceTransformBlur.BlurPartial(val, 8.0, 16);
					val2.SetSourceRGBA(0.17647058823529413, 7.0 / 51.0, 11.0 / 85.0, 1.0);
					val2.LineWidth = 2.0;
					val2.Stroke();
					ApplySelectedFontForTab(base.tabs[i]);
					base.selectedFont.SetupContext(val2);
					double num4 = ((_textOffsets.Length > i) ? _textOffsets[i] : base.textOffsetY);
					((GuiElementTextBase)this).DrawTextLineAt(val2, base.tabs[i].Name ?? string.Empty, num2 + 2.0, num4, false);
					((GuiElement)this).generateTexture(val, ref base.hoverTextures[i], true);
				}
				finally
				{
					((IDisposable)val2)?.Dispose();
				}
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
		RestoreSelectedFont();
	}

	private void ApplyRegularFontForTab(GuiTab tab)
	{
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		string categoryCode = ShowCraftableSystem.TryGetCategoryCode(tab);
		string craftableTabFontName = ShowCraftableSystem.GetCraftableTabFontName(categoryCode);
		FontWeight? craftableTabFontWeight = ShowCraftableSystem.GetCraftableTabFontWeight(categoryCode);
		((GuiElementTextBase)this).Font.WithFont(craftableTabFontName ?? _defaultFontName);
		((GuiElementTextBase)this).Font.WithWeight((FontWeight)(((_003F?)craftableTabFontWeight) ?? _defaultFontWeight));
	}

	private void RestoreRegularFont()
	{
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		((GuiElementTextBase)this).Font.WithFont(_defaultFontName);
		((GuiElementTextBase)this).Font.WithWeight(_defaultFontWeight);
	}

	private void ApplySelectedFontForTab(GuiTab tab)
	{
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		string categoryCode = ShowCraftableSystem.TryGetCategoryCode(tab);
		string craftableTabFontName = ShowCraftableSystem.GetCraftableTabFontName(categoryCode);
		FontWeight? craftableTabFontWeight = ShowCraftableSystem.GetCraftableTabFontWeight(categoryCode);
		base.selectedFont.WithFont(craftableTabFontName ?? _defaultSelectedFontName);
		base.selectedFont.WithWeight((FontWeight)(((_003F?)craftableTabFontWeight) ?? _defaultSelectedFontWeight));
	}

	private void RestoreSelectedFont()
	{
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		base.selectedFont.WithFont(_defaultSelectedFontName);
		base.selectedFont.WithWeight(_defaultSelectedFontWeight);
	}
}
