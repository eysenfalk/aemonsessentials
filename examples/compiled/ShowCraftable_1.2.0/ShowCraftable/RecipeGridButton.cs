using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ShowCraftable;

public class RecipeGridButton : ButtonFetch
{
	private const double YManualOffset = 0.0;

	private readonly SlideshowGridRecipeTextComponent slideshow;

	public RecipeGridButton(ICoreClientAPI api, SlideshowGridRecipeTextComponent slideshow)
		: base(api, 0, "#", "Fetch all the ingredients", -1.0, -5.0)
	{
		this.slideshow = slideshow;
		((RichTextComponentBase)this).Float = (EnumFloat)1;
		((RichTextComponentBase)this).VerticalAlign = (EnumVerticalAlign)3;
	}

	protected override void OnClick()
	{
		((RichTextComponentBase)this).api.Gui.PlaySound("menubutton_press", false, 1f);
		try
		{
			if (slideshow == null)
			{
				if (ShowCraftableSystem.DebugEnabled)
				{
					((RichTextComponentBase)this).api.ShowChatMessage("[ShowCraftable] RecipeGridButton.OnClick: open a handbook recipe with a crafting grid first.");
				}
				return;
			}
			List<(GridRecipe, Dictionary<int, ItemStack[]>)> list = GetVariants(slideshow).ToList();
			if (list.Count == 0)
			{
				if (ShowCraftableSystem.DebugEnabled)
				{
					((RichTextComponentBase)this).api.ShowChatMessage("[ShowCraftable] RecipeGridButton.OnClick: no recipe variants found for this grid.");
				}
				return;
			}
			HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
			List<string> list2 = new List<string>();
			for (int i = 0; i < list.Count; i++)
			{
				(GridRecipe, Dictionary<int, ItemStack[]>) tuple = list[i];
				GridRecipe item = tuple.Item1;
				Dictionary<int, ItemStack[]> item2 = tuple.Item2;
				string text = SummarizeRecipe(item, item2);
				if (!string.IsNullOrWhiteSpace(text) && hashSet.Add(text))
				{
					if (list.Count > 1)
					{
						list2.Add($"[ShowCraftable] Required (variant {list2.Count + 1}/{list.Count}): {text}");
					}
					else
					{
						list2.Add("[ShowCraftable] Required: " + text);
					}
				}
			}
			if (list2.Count == 0)
			{
				if (ShowCraftableSystem.DebugEnabled)
				{
					((RichTextComponentBase)this).api.ShowChatMessage("[ShowCraftable] RecipeGridButton.OnClick: could not summarize ingredients for this recipe.");
				}
				return;
			}
			if (ShowCraftableSystem.DebugEnabled)
			{
				foreach (string item3 in list2)
				{
					((RichTextComponentBase)this).api.ShowChatMessage(item3);
				}
			}
			bool flag = false;
			bool flag2 = false;
			bool flag3 = false;
			try
			{
				if (!ShowCraftableSystem.TryBeginFetch())
				{
					if (ShowCraftableSystem.DebugEnabled)
					{
						bool num = ShowCraftableSystem.IsFetchInProgress();
						bool flag4 = ShowCraftableSystem.IsScanInProgress();
						string text2 = (num ? "fetch ignored, another fetch is in progress." : (flag4 ? "fetch ignored, another scan is in progress." : "fetch ignored."));
						((RichTextComponentBase)this).api.ShowChatMessage("[ShowCraftable] RecipeGridButton.OnClick: " + text2);
					}
					return;
				}
				flag3 = true;
				ShowCraftableSystem.AcquireHandbookPauseGuard(((RichTextComponentBase)this).api);
				flag = true;
				List<CraftIngredientList> list3 = BuildIngredientLists(list);
				if (list3.Count > 0)
				{
					CraftScanRequest craftScanRequest = new CraftScanRequest
					{
						Radius = ShowCraftableSystem.ConfiguredSearchRadius,
						CollectItems = true,
						Variants = list3
					};
					((RichTextComponentBase)this).api.Network.GetChannel("showcraftablescan").SendPacket<CraftScanRequest>(craftScanRequest);
					flag2 = true;
				}
			}
			catch (Exception ex)
			{
				if (flag)
				{
					ShowCraftableSystem.ReleaseHandbookPauseGuard(((RichTextComponentBase)this).api);
					flag = false;
				}
				if (flag3)
				{
					ShowCraftableSystem.EndFetch();
					flag3 = false;
				}
				if (ShowCraftableSystem.DebugEnabled)
				{
					((RichTextComponentBase)this).api.ShowChatMessage("[ShowCraftable] RecipeGridButton.OnClick: fetch request failed: " + ex.Message);
				}
			}
			finally
			{
				if (flag && !flag2)
				{
					ShowCraftableSystem.ReleaseHandbookPauseGuard(((RichTextComponentBase)this).api);
				}
				if (flag3 && !flag2)
				{
					ShowCraftableSystem.EndFetch();
				}
			}
		}
		catch (Exception ex2)
		{
			if (ShowCraftableSystem.DebugEnabled)
			{
				((RichTextComponentBase)this).api.ShowChatMessage("[ShowCraftable] RecipeGridButton.OnClick: ingredient listing failed: " + ex2.Message);
			}
		}
	}

	public override EnumCalcBoundsResult CalcBounds(TextFlowPath[] flowPath, double currentLineHeight, double offsetX, double lineY, out double nextOffsetX)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		EnumCalcBoundsResult result = base.CalcBounds(flowPath, currentLineHeight, offsetX, lineY, out nextOffsetX);
		LineRectangled val = ((RichTextComponentBase)this).BoundsPerLine[0];
		((Rectangled)val).Y = ((Rectangled)val).Y + GuiElement.scaled(0.0);
		((RichTextComponentBase)this).BoundsPerLine[0] = val;
		return result;
	}

	private static IEnumerable<string> FlattenAllowed(object allowedObj)
	{
		if (allowedObj == null)
		{
			yield break;
		}
		if (allowedObj is IEnumerable<string> enumerable)
		{
			foreach (string item in enumerable)
			{
				if (!string.IsNullOrEmpty(item))
				{
					yield return item;
				}
			}
			yield break;
		}
		Type type = allowedObj.GetType();
		PropertyInfo property = type.GetProperty("Keys");
		PropertyInfo indexer = type.GetProperty("Item");
		if (property == null || indexer == null || !(property.GetValue(allowedObj) is IEnumerable enumerable2))
		{
			yield break;
		}
		foreach (object item2 in enumerable2)
		{
			if (!(indexer.GetValue(allowedObj, new object[1] { item2 }) is string[] array))
			{
				continue;
			}
			string[] array2 = array;
			foreach (string text in array2)
			{
				if (!string.IsNullOrEmpty(text))
				{
					yield return text;
				}
			}
		}
	}

	private static IEnumerable<(GridRecipe recipe, Dictionary<int, ItemStack[]> unnamed)> GetVariants(SlideshowGridRecipeTextComponent slide)
	{
		if (!(((object)slide).GetType().GetField("GridRecipesAndUnIn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(slide) is Array arr))
		{
			yield break;
		}
		for (int i = 0; i < arr.Length; i++)
		{
			object value = arr.GetValue(i);
			if (value != null)
			{
				Type type = value.GetType();
				object member = GetMember(type, value, "Recipe");
				GridRecipe item = (GridRecipe)((member is GridRecipe) ? member : null);
				object member2 = GetMember(type, value, "unnamedIngredients");
				yield return (recipe: item, unnamed: ConvertUnnamedDict(member2));
			}
		}
	}

	private static object GetMember(Type t, object obj, string name)
	{
		if (t == null || obj == null)
		{
			return null;
		}
		PropertyInfo property = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (property != null)
		{
			return property.GetValue(obj);
		}
		FieldInfo field = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (field != null)
		{
			return field.GetValue(obj);
		}
		return null;
	}

	private static Dictionary<int, ItemStack[]> ConvertUnnamedDict(object genericDict)
	{
		Dictionary<int, ItemStack[]> dictionary = new Dictionary<int, ItemStack[]>();
		if (genericDict == null)
		{
			return dictionary;
		}
		Type type = genericDict.GetType();
		PropertyInfo property = type.GetProperty("Keys");
		PropertyInfo property2 = type.GetProperty("Item");
		if (property == null || property2 == null)
		{
			return dictionary;
		}
		if (property.GetValue(genericDict) is IEnumerable enumerable)
		{
			foreach (object item in enumerable)
			{
				int key = ((item is int num) ? num : Convert.ToInt32(item));
				if (property2.GetValue(genericDict, new object[1] { item }) is ItemStack[] value)
				{
					dictionary[key] = value;
				}
			}
		}
		return dictionary;
	}

	private static List<CraftIngredientList> BuildIngredientLists(List<(GridRecipe recipe, Dictionary<int, ItemStack[]> unnamed)> variants)
	{
		//IL_0216: Unknown result type (might be due to invalid IL or missing references)
		//IL_021b: Unknown result type (might be due to invalid IL or missing references)
		//IL_021f: Unknown result type (might be due to invalid IL or missing references)
		List<CraftIngredientList> list = new List<CraftIngredientList>();
		foreach (var (val, dictionary) in variants)
		{
			if (val?.resolvedIngredients == null || val.resolvedIngredients.Length == 0)
			{
				continue;
			}
			CraftIngredientList craftIngredientList = new CraftIngredientList();
			for (int i = 0; i < val.resolvedIngredients.Length; i++)
			{
				GridRecipeIngredient val2 = val.resolvedIngredients[i];
				if (val2 == null)
				{
					continue;
				}
				bool flag = TryGetBool(val2, "IsWildCard");
				int num;
				if (!flag)
				{
					ItemStack obj = TryGetStack(val2, "ResolvedItemstack");
					num = Math.Max(1, (obj == null) ? 1 : obj.StackSize);
				}
				else
				{
					num = TryGetInt(val2, "Quantity", 1);
				}
				int quantity = num;
				CraftIngredient craftIngredient = new CraftIngredient
				{
					IsWildcard = flag,
					Quantity = quantity
				};
				if (dictionary != null && dictionary.TryGetValue(i, out var value) && value != null)
				{
					ItemStack[] array = value;
					foreach (ItemStack obj2 in array)
					{
						string text = ((obj2 == null) ? null : ((object)((RegistryObject)(obj2.Collectible?)).Code)?.ToString());
						if (!string.IsNullOrEmpty(text) && !craftIngredient.Codes.Contains(text))
						{
							craftIngredient.Codes.Add(text);
						}
					}
				}
				else
				{
					ItemStack obj3 = TryGetStack(val2, "ResolvedItemstack");
					string text2 = ((obj3 == null) ? null : ((object)((RegistryObject)(obj3.Collectible?)).Code)?.ToString());
					if (!string.IsNullOrEmpty(text2) && !craftIngredient.Codes.Contains(text2))
					{
						craftIngredient.Codes.Add(text2);
					}
				}
				if (flag)
				{
					AssetLocation val3 = TryGetAssetLocation(val2, "Code");
					if (val3 != (AssetLocation)null)
					{
						craftIngredient.PatternCode = ((object)val3).ToString();
					}
					Type type = ((object)val2).GetType();
					HashSet<string> hashSet = new HashSet<string>(FlattenAllowed(GetMember(type, val2, "AllowedVariants")), StringComparer.OrdinalIgnoreCase);
					if (hashSet.Count > 0)
					{
						craftIngredient.Allowed.AddRange(hashSet);
					}
					if (GetMember(type, val2, "Type") is EnumItemClass type2)
					{
						craftIngredient.Type = type2;
						craftIngredient.HasType = true;
					}
				}
				craftIngredientList.Ingredients.Add(craftIngredient);
			}
			if (craftIngredientList.Ingredients.Count > 0)
			{
				list.Add(craftIngredientList);
			}
		}
		return list;
	}

	private string SummarizeRecipe(GridRecipe recipe, Dictionary<int, ItemStack[]> unnamed)
	{
		if (recipe == null || recipe.resolvedIngredients == null || recipe.resolvedIngredients.Length == 0)
		{
			return null;
		}
		List<(string, int)> list = new List<(string, int)>();
		for (int i = 0; i < recipe.resolvedIngredients.Length; i++)
		{
			GridRecipeIngredient val = recipe.resolvedIngredients[i];
			if (val != null)
			{
				bool num = TryGetBool(val, "IsTool");
				bool num2 = TryGetBool(val, "IsWildCard");
				int num3;
				if (!num2)
				{
					ItemStack obj = TryGetStack(val, "ResolvedItemstack");
					num3 = Math.Max(1, (obj == null) ? 1 : obj.StackSize);
				}
				else
				{
					num3 = TryGetInt(val, "Quantity", 1);
				}
				int num4 = num3;
				string text;
				if (!num2)
				{
					ItemStack st = TryGetStack(val, "ResolvedItemstack");
					text = LabelFromStack(st, includeVariant: true, num4 > 1);
				}
				else
				{
					AssetLocation val2 = TryGetAssetLocation(val, "Code");
					text = ((val2 != (AssetLocation)null) ? WildcardLabel(BaseNameFromPattern(val2), num4) : ((unnamed == null || !unnamed.TryGetValue(i, out var value) || value == null || value.Length == 0) ? WildcardLabel(null, num4) : WildcardLabel(BaseNameFromStack(value[0]), num4)));
				}
				if (num)
				{
					text += " (tool)";
				}
				list.Add((text, num4));
			}
		}
		Dictionary<string, int> dictionary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		foreach (var (text2, num5) in list)
		{
			if (!string.IsNullOrWhiteSpace(text2))
			{
				dictionary[text2] = (dictionary.TryGetValue(text2, out var value2) ? (value2 + num5) : num5);
			}
		}
		return JoinWithCommasAndAnd(dictionary.Select((KeyValuePair<string, int> kv) => $"{kv.Value} {kv.Key}").ToList());
	}

	private static bool TryGetBool(object obj, string name)
	{
		Type type = obj.GetType();
		PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (property?.PropertyType == typeof(bool))
		{
			return (bool)property.GetValue(obj);
		}
		FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (field?.FieldType == typeof(bool))
		{
			return (bool)field.GetValue(obj);
		}
		return false;
	}

	private static int TryGetInt(object obj, string name, int def = 0)
	{
		Type type = obj.GetType();
		PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (property?.PropertyType == typeof(int))
		{
			return (int)property.GetValue(obj);
		}
		FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (field?.FieldType == typeof(int))
		{
			return (int)field.GetValue(obj);
		}
		return def;
	}

	private static ItemStack TryGetStack(object obj, string name)
	{
		Type type = obj.GetType();
		PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (property != null)
		{
			object? value = property.GetValue(obj);
			return (ItemStack)((value is ItemStack) ? value : null);
		}
		FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (field != null)
		{
			object? value2 = field.GetValue(obj);
			return (ItemStack)((value2 is ItemStack) ? value2 : null);
		}
		return null;
	}

	private static AssetLocation TryGetAssetLocation(object obj, string name)
	{
		Type type = obj.GetType();
		PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (property != null)
		{
			object? value = property.GetValue(obj);
			return (AssetLocation)((value is AssetLocation) ? value : null);
		}
		FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (field != null)
		{
			object? value2 = field.GetValue(obj);
			return (AssetLocation)((value2 is AssetLocation) ? value2 : null);
		}
		return null;
	}

	private string LabelFromStack(ItemStack st, bool includeVariant, bool pluralize)
	{
		if (((st == null) ? null : ((RegistryObject)(st.Collectible?)).Code) == (AssetLocation)null)
		{
			return "item";
		}
		var (text, text2) = SplitBaseAndVariant(((RegistryObject)st.Collectible).Code.Path);
		if (pluralize)
		{
			text = Pluralize(text);
		}
		if (includeVariant && !string.IsNullOrEmpty(text2))
		{
			return text + " (" + text2 + ")";
		}
		return text;
	}

	private static string BaseNameFromStack(ItemStack st)
	{
		if (((st == null) ? null : ((RegistryObject)(st.Collectible?)).Code) == (AssetLocation)null)
		{
			return "item";
		}
		return SplitBaseAndVariant(((RegistryObject)st.Collectible).Code.Path).Base;
	}

	private static string BaseNameFromPattern(AssetLocation pattern)
	{
		if (pattern == (AssetLocation)null)
		{
			return "item";
		}
		string text = pattern.Path ?? "";
		text = text.Replace("*", "");
		while (true)
		{
			int num = text.IndexOf('{');
			int num2 = text.IndexOf('}');
			if (num < 0 || num2 < 0 || num2 <= num)
			{
				break;
			}
			text = text.Remove(num, num2 - num + 1);
		}
		string item = SplitBaseAndVariant(text).Base;
		if (!string.IsNullOrWhiteSpace(item))
		{
			return item;
		}
		return "item";
	}

	private static (string Base, string Variant) SplitBaseAndVariant(string codePath)
	{
		if (string.IsNullOrEmpty(codePath))
		{
			return (Base: "item", Variant: null);
		}
		string text = codePath.Replace("/", " ").Replace("_", " ").Replace(".", " ");
		int num = text.LastIndexOf('-');
		string s = null;
		string s2;
		if (num > 0 && num < text.Length - 1)
		{
			s2 = text.Substring(0, num);
			s = text.Substring(num + 1);
		}
		else
		{
			s2 = text;
		}
		s2 = CleanupWords(s2);
		s = CleanupWords(s);
		return (Base: string.IsNullOrWhiteSpace(s2) ? "item" : s2, Variant: string.IsNullOrWhiteSpace(s) ? null : s);
	}

	private static string CleanupWords(string s)
	{
		if (string.IsNullOrWhiteSpace(s))
		{
			return s;
		}
		s = s.Replace("-", " ");
		while (s.Contains("  "))
		{
			s = s.Replace("  ", " ");
		}
		return s.Trim();
	}

	private static string Pluralize(string noun)
	{
		if (string.IsNullOrWhiteSpace(noun))
		{
			return noun;
		}
		noun = noun.Trim();
		if (noun.EndsWith("s", StringComparison.OrdinalIgnoreCase))
		{
			return noun;
		}
		if (noun.EndsWith("y", StringComparison.OrdinalIgnoreCase) && noun.Length > 1 && !"aeiou".Contains(char.ToLowerInvariant(noun[noun.Length - 2])))
		{
			return noun.Substring(0, noun.Length - 1) + "ies";
		}
		return noun + "s";
	}

	private static string WildcardLabel(string baseName, int qty)
	{
		if (baseName == null)
		{
			baseName = "item";
		}
		if (baseName.Contains(' '))
		{
			return baseName + " (any)";
		}
		return "any " + ((qty > 1) ? Pluralize(baseName) : baseName);
	}

	private static string JoinWithCommasAndAnd(IList<string> parts)
	{
		if (parts == null || parts.Count == 0)
		{
			return "";
		}
		if (parts.Count == 1)
		{
			return parts[0];
		}
		if (parts.Count == 2)
		{
			return parts[0] + " and " + parts[1];
		}
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < parts.Count; i++)
		{
			if (i > 0)
			{
				stringBuilder.Append((i == parts.Count - 1) ? ", and " : ", ");
			}
			stringBuilder.Append(parts[i]);
		}
		return stringBuilder.ToString();
	}
}
