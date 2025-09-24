using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cairo;
using HarmonyLib;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ShowCraftable;

public class ShowCraftableSystem : ModSystem
{
	[ProtoContract]
	private class CachedIngredient
	{
		[ProtoMember(1)]
		public bool IsTool;

		[ProtoMember(2)]
		public bool IsWild;

		[ProtoMember(3)]
		public int QuantityRequired;

		[ProtoMember(4)]
		public List<byte[]> Options = new List<byte[]>();

		[ProtoMember(5)]
		public string PatternCode;

		[ProtoMember(6)]
		public string[] Allowed;

		[ProtoMember(7)]
		public EnumItemClass Type;
	}

	[ProtoContract]
	private class CachedRecipe
	{
		[ProtoMember(1)]
		public List<CachedIngredient> Ingredients = new List<CachedIngredient>();

		[ProtoMember(2)]
		public List<byte[]> Outputs = new List<byte[]>();

		[ProtoMember(3)]
		public Dictionary<string, int> Needs = new Dictionary<string, int>();
	}

	[ProtoContract]
	private class CodeRecipeRef
	{
		[ProtoMember(1)]
		public int Recipe;

		[ProtoMember(2)]
		public string GroupKey;
	}

	[ProtoContract]
	private class RecipeIndexCache
	{
		[ProtoMember(1)]
		public List<CachedRecipe> Recipes { get; set; } = new List<CachedRecipe>();

		[ProtoMember(2)]
		public Dictionary<string, List<CodeRecipeRef>> CodeToRecipes { get; set; } = new Dictionary<string, List<CodeRecipeRef>>();

		[ProtoMember(3)]
		public Dictionary<string, List<string>> CodeToGkeys { get; set; } = new Dictionary<string, List<string>>();
	}

	private struct ScanRequestInfo
	{
		public bool IncludeAll;

		public bool ModsOnly;

		public bool WoodOnly;

		public bool StoneOnly;

		public string TabKey;

		public ScanRequestInfo(bool includeAll, bool modsOnly, bool woodOnly, bool stoneOnly, string tabKey)
		{
			IncludeAll = includeAll;
			ModsOnly = modsOnly;
			WoodOnly = woodOnly;
			StoneOnly = stoneOnly;
			TabKey = tabKey;
		}
	}

	private sealed class WildGroup
	{
		public GridRecipeShim Recipe;

		public string GroupKey;

		public EnumItemClass Type;

		public AssetLocation Pattern;

		public string[] Allowed;
	}

	private static class HandbookPauseGuard
	{
		private static int _refCount;

		private static bool _savedNoHandbookPause;

		public static void Acquire(ICoreClientAPI capi)
		{
			if (capi == null || !capi.IsSinglePlayer || capi.OpenedToLan || Interlocked.Increment(ref _refCount) != 1)
			{
				return;
			}
			try
			{
				_savedNoHandbookPause = capi.Settings.Bool["noHandbookPause"];
				capi.Settings.Bool["noHandbookPause"] = true;
				capi.PauseGame(false);
				SyncToggleVisual(capi);
			}
			catch
			{
			}
		}

		public static void Release(ICoreClientAPI capi)
		{
			if (capi == null || !capi.IsSinglePlayer || capi.OpenedToLan || Interlocked.Decrement(ref _refCount) != 0)
			{
				return;
			}
			try
			{
				capi.Settings.Bool["noHandbookPause"] = _savedNoHandbookPause;
				if (IsHandbookOpen(capi) && !_savedNoHandbookPause)
				{
					capi.PauseGame(true);
				}
				SyncToggleVisual(capi);
			}
			catch
			{
			}
		}

		private static bool IsHandbookOpen(ICoreClientAPI capi)
		{
			try
			{
				IEnumerable<object> openedGuis = capi.OpenedGuis;
				return openedGuis != null && openedGuis.OfType<GuiDialogHandbook>()?.Any() == true;
			}
			catch
			{
				return false;
			}
		}

		private static void SyncToggleVisual(ICoreClientAPI capi)
		{
			try
			{
				GuiDialogHandbook val = capi.OpenedGuis?.OfType<GuiDialogHandbook>()?.FirstOrDefault();
				if (val == null)
				{
					return;
				}
				bool value = !capi.Settings.Bool["noHandbookPause"];
				Type? typeFromHandle = typeof(GuiDialogHandbook);
				object? obj = typeFromHandle.GetField("overviewGui", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(val);
				GuiComposer val2 = (GuiComposer)((obj is GuiComposer) ? obj : null);
				object? obj2 = typeFromHandle.GetField("detailViewGui", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(val);
				object? obj3 = ((obj2 is GuiComposer) ? obj2 : null);
				if (val2 != null)
				{
					GuiElementToggleButton toggleButton = GuiComposerHelpers.GetToggleButton(val2, "pausegame");
					if (toggleButton != null)
					{
						toggleButton.SetValue(value);
					}
				}
				if (obj3 != null)
				{
					GuiElementToggleButton toggleButton2 = GuiComposerHelpers.GetToggleButton((GuiComposer)obj3, "pausegame");
					if (toggleButton2 != null)
					{
						toggleButton2.SetValue(value);
					}
				}
			}
			catch
			{
			}
		}
	}

	private readonly record struct StackKey(string Code, string Material, string Type);

	private sealed class RecipeIndexData
	{
		public Dictionary<string, List<(GridRecipeShim Recipe, string GroupKey)>> CodeToRecipeGroups { get; } = new Dictionary<string, List<(GridRecipeShim, string)>>(StringComparer.Ordinal);

		public Dictionary<string, HashSet<string>> CodeToGkeys { get; } = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

		public Dictionary<GridRecipeShim, Dictionary<string, int>> RecipeGroupNeeds { get; } = new Dictionary<GridRecipeShim, Dictionary<string, int>>();

		public List<WildGroup> WildcardGroups { get; } = new List<WildGroup>();

		public Dictionary<string, List<(GridRecipeShim Recipe, string GroupKey)>> WildMatchCache { get; } = new Dictionary<string, List<(GridRecipeShim, string)>>(StringComparer.Ordinal);

		public Dictionary<StackKey, List<GridRecipeShim>> OutputsIndex { get; set; } = new Dictionary<StackKey, List<GridRecipeShim>>();

		public int RecipesFetched { get; set; }

		public int RecipesUsable { get; set; }
	}

	private struct Key : IEquatable<Key>
	{
		public string Code;

		public bool Equals(Key other)
		{
			return Code == other.Code;
		}

		public override bool Equals(object obj)
		{
			if (obj is Key other)
			{
				return Equals(other);
			}
			return false;
		}

		public override int GetHashCode()
		{
			return Code?.GetHashCode() ?? 0;
		}
	}

	private sealed class ResourcePool
	{
		public readonly Dictionary<Key, int> Counts = new Dictionary<Key, int>();

		public readonly Dictionary<Key, EnumItemClass> Classes = new Dictionary<Key, EnumItemClass>();

		public void Add(ItemStack stack)
		{
			//IL_0084: Unknown result type (might be due to invalid IL or missing references)
			//IL_0089: Unknown result type (might be due to invalid IL or missing references)
			//IL_008b: Unknown result type (might be due to invalid IL or missing references)
			//IL_008e: Invalid comparison between Unknown and I4
			//IL_00a2: Unknown result type (might be due to invalid IL or missing references)
			//IL_0099: Unknown result type (might be due to invalid IL or missing references)
			if (stack == null)
			{
				return;
			}
			CollectibleObject collectible = stack.Collectible;
			if (collectible == null || ((RegistryObject)collectible).Code == (AssetLocation)null)
			{
				return;
			}
			Key key = new Key
			{
				Code = ((object)((RegistryObject)collectible).Code).ToString()
			};
			int num = Math.Max(1, stack.StackSize);
			if (Counts.TryGetValue(key, out var value))
			{
				Counts[key] = value + num;
			}
			else
			{
				Counts[key] = num;
			}
			if (!Classes.ContainsKey(key))
			{
				EnumItemClass val = stack.Class;
				if ((int)val == 1 && stack.Block != null)
				{
					val = (EnumItemClass)0;
				}
				Classes[key] = val;
			}
		}

		public string GetSignature()
		{
			//IL_0058: Unknown result type (might be due to invalid IL or missing references)
			//IL_005a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0087: Unknown result type (might be due to invalid IL or missing references)
			//IL_008d: Expected I4, but got Unknown
			StringBuilder stringBuilder = new StringBuilder();
			foreach (KeyValuePair<Key, int> item in Counts.OrderBy((KeyValuePair<Key, int> k) => k.Key.Code))
			{
				EnumItemClass value;
				EnumItemClass val = (EnumItemClass)(Classes.TryGetValue(item.Key, out value) ? ((int)value) : 0);
				stringBuilder.Append(item.Key.Code).Append(':').Append(item.Value)
					.Append(':')
					.Append((int)val)
					.Append('|');
			}
			return stringBuilder.ToString();
		}

		public bool TryConsumeAny(IEnumerable<ItemStack> options, int quantity, bool consume)
		{
			if (options == null)
			{
				return false;
			}
			foreach (ItemStack option in options)
			{
				string text = ((option == null) ? null : ((object)((RegistryObject)(option.Collectible?)).Code)?.ToString());
				if (string.IsNullOrEmpty(text))
				{
					continue;
				}
				Key key = new Key
				{
					Code = text
				};
				if (!Counts.TryGetValue(key, out var value) || value < quantity)
				{
					continue;
				}
				if (consume)
				{
					value -= quantity;
					if (value <= 0)
					{
						Counts.Remove(key);
						Classes.Remove(key);
					}
					else
					{
						Counts[key] = value;
					}
				}
				return true;
			}
			return false;
		}

		public bool HasAny(IEnumerable<ItemStack> options)
		{
			if (options == null)
			{
				return false;
			}
			foreach (ItemStack option in options)
			{
				string text = ((option == null) ? null : ((object)((RegistryObject)(option.Collectible?)).Code)?.ToString());
				if (!string.IsNullOrEmpty(text) && Counts.ContainsKey(new Key
				{
					Code = text
				}))
				{
					return true;
				}
			}
			return false;
		}

		public bool TryConsumeWildcard(EnumItemClass type, AssetLocation pattern, string[] allowed, int quantity, bool consume)
		{
			//IL_003e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0040: Unknown result type (might be due to invalid IL or missing references)
			//IL_0049: Unknown result type (might be due to invalid IL or missing references)
			//IL_0050: Expected O, but got Unknown
			if (pattern == (AssetLocation)null)
			{
				return false;
			}
			KeyValuePair<Key, int>[] array = Counts.ToArray();
			for (int i = 0; i < array.Length; i++)
			{
				KeyValuePair<Key, int> keyValuePair = array[i];
				Key key = keyValuePair.Key;
				if (!Classes.TryGetValue(key, out var value) || value != type)
				{
					continue;
				}
				AssetLocation val = new AssetLocation(key.Code);
				if (!WildcardUtil.Match(pattern, val, allowed) || keyValuePair.Value < quantity)
				{
					continue;
				}
				if (consume)
				{
					int num = keyValuePair.Value - quantity;
					if (num <= 0)
					{
						Counts.Remove(key);
						Classes.Remove(key);
					}
					else
					{
						Counts[key] = num;
					}
				}
				return true;
			}
			return false;
		}
	}

	private sealed class GridRecipeShim
	{
		public object Raw;

		public List<GridIngredientShim> Ingredients = new List<GridIngredientShim>();

		public List<ItemStack> Outputs = new List<ItemStack>();

		public bool IsMod;
	}

	private sealed class GridIngredientShim
	{
		public bool IsTool;

		public bool IsWild;

		public int QuantityRequired;

		public List<ItemStack> Options = new List<ItemStack>();

		public AssetLocation PatternCode;

		public string[] Allowed;

		public EnumItemClass Type;
	}

	private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
	{
		public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

		public new bool Equals(object x, object y)
		{
			return x == y;
		}

		public int GetHashCode(object obj)
		{
			return RuntimeHelpers.GetHashCode(obj);
		}
	}

	private Harmony _harmony;

	private ICoreClientAPI _capi;

	public const string HarmonyId = "showcraftable.core";

	internal static bool DebugEnabled = false;

	private const string ConfigFileName = "ShowCraftable.json";

	private const string AllTabKeyName = "allTab";

	private const string CraftableTabKeyName = "craftableTab";

	private const string ModTabKeyName = "modTab";

	private const string StoneTabKeyName = "stoneTab";

	private const string WoodTabKeyName = "woodTab";

	private const string CraftableAllTabDisplayName = "Craftable";

	private const string BaseItemsTabDisplayName = "● Base Items";

	private const string WoodTypesTabDisplayName = "● Wood Types";

	private const string StoneTypesTabDisplayName = "● Stone Types";

	private const string ModItemsTabDisplayName = "● Mod Items";

	private const string ArialFontName = "Arial";

	private const string ArialBlackFontName = "Arial Black";

	private const int SlowStackLogThresholdMs = 175;

	private static bool ScanQueueCheckScheduled;

	private static Dictionary<GridRecipeShim, Dictionary<string, int>> recipeGroupNeeds = new Dictionary<GridRecipeShim, Dictionary<string, int>>();

	private static Dictionary<StackKey, List<GridRecipeShim>> outputsIndex = new Dictionary<StackKey, List<GridRecipeShim>>();

	private static Dictionary<StackKey, string> AllStacksPageCodeMap = new Dictionary<StackKey, string>();

	private static Dictionary<string, HashSet<string>> codeToGkeys = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

	private static Dictionary<string, List<(GridRecipeShim Recipe, string GroupKey)>> codeToRecipeGroups = new Dictionary<string, List<(GridRecipeShim, string)>>();

	private static Dictionary<string, List<(GridRecipeShim Recipe, string GroupKey)>> wildMatchCache = new Dictionary<string, List<(GridRecipeShim, string)>>(StringComparer.Ordinal);

	private static ICoreClientAPI _staticCapi;

	private static int LastDialogPageCount;

	private static int NearbyRadius = 20;

	private static int recipesFetched;

	private static int recipesUsable;

	private static int ScanSeq;

	private static int _pendingScanId;

	private static ItemStack[] AllStacksPageCodeMapSource;

	private static List<string> CachedPageCodes = new List<string>();

	private static List<string> AllTabCache = new List<string>();

	private static List<string> CraftableTabCache = new List<string>();

	private static List<string> ModTabCache = new List<string>();

	private static List<string> s_EmptyPages;

	private static List<string> StoneTypeTabCache = new List<string>();

	private static List<string> WoodTypeTabCache = new List<string>();

	private static List<WildGroup> wildcardGroups = new List<WildGroup>();

	private static readonly Dictionary<int, ScanRequestInfo> InflightById = new Dictionary<int, ScanRequestInfo>();

	private static readonly Dictionary<string, bool> TabReadyToUpdateUi = new Dictionary<string, bool>(StringComparer.Ordinal);

	private static readonly Dictionary<string, Dictionary<string, int>> WildTokenCountsMemo = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

	private static readonly Dictionary<string, RecipeIndexData> recipeIndexByVariant = new Dictionary<string, RecipeIndexData>(StringComparer.Ordinal);

	private static readonly Dictionary<string, ulong> TabPoolDNA = new Dictionary<string, ulong>(StringComparer.Ordinal);

	private static readonly object CacheLock = new object();

	private static readonly object DnaLock = new object();

	private static readonly object InflightMapLock = new object();

	private static readonly object LogFileLock = new object();

	private static readonly object PageCodeMapLock = new object();

	private static readonly object PendingScanLock = new object();

	private static readonly object ScanQueueLock = new object();

	private static readonly object TabUiStateLock = new object();

	private static ScanRequestInfo? QueuedScanRequest;

	private static ShowCraftableConfig Config = new ShowCraftableConfig();

	private static string PendingScanTabKey;

	private static string PendingScanVariantKey;

	private static Task recipeIndexBuildTask;

	private static int recipeIndexBuildToken;

	private static volatile bool recipeIndexBuildStarted = false;

	private static volatile bool CraftableAllTabActive;

	private static volatile bool CraftableModsTabActive;

	private static volatile bool CraftableStoneTabActive;

	private static volatile bool CraftableTabActive;

	private static volatile bool CraftableWoodTabActive;

	private static volatile bool recipeIndexBuilt = false;

	private static volatile bool recipeIndexForMods = false;

	private static volatile bool recipeIndexForStoneOnly;

	private static volatile bool recipeIndexForWoodOnly;

	private static volatile bool ScanInProgress = false;

	private static int FetchInProgressFlag = 0;

	private static volatile int recipeIndexBuildProgress;

	private static volatile int recipeIndexBuildTotal;

	public const string ChannelName = "showcraftablescan";

	public const string CraftableAllCategoryCode = "craftableall";

	public const string CraftableCategoryCode = "craftable";

	public const string CraftableModsCategoryCode = "craftablemods";

	public const string CraftableStoneCategoryCode = "craftablestonetypes";

	public const string CraftableWoodCategoryCode = "craftablewoodtypes";

	private static readonly (string VariantKey, bool IncludeAll, bool ModsOnly, bool WoodOnly, bool StoneOnly)[] RecipeIndexVariants = new(string, bool, bool, bool, bool)[5]
	{
		("van", false, false, false, false),
		("mods", false, true, false, false),
		("wood", false, false, true, false),
		("stone", false, false, false, true),
		("all", true, false, false, false)
	};

	private static readonly string[] WoodSpecies = new string[13]
	{
		"birch", "maple", "pine", "acacia", "kapok", "baldcypress", "larch", "redwood", "ebony", "walnut",
		"purpleheart", "oak", "aged"
	};

	private static readonly string[] StoneSpecies = new string[19]
	{
		"andesite", "basalt", "bauxite", "chalk", "chert", "claystone", "conglomerate", "granite", "kimberlite", "limestone",
		"whitemarble", "redmarble", "greenmarble", "peridotite", "phyllite", "sandstone", "shale", "slate", "suevite"
	};

	private static DateTime _lastScanAt = DateTime.MinValue;

	internal static int ConfiguredSearchRadius => Math.Max(0, Config?.SearchDistanceItems ?? 20);

	internal static int ConfiguredAllStacksPartitions => Math.Max(-1, Config?.AllStacksPartitions ?? (-1));

	internal static bool UseDefaultFont => Config?.UseDefaultFont ?? false;

	internal static bool IsCraftableCategoryCode(string categoryCode)
	{
		if (string.IsNullOrEmpty(categoryCode))
		{
			return false;
		}
		if (!string.Equals(categoryCode, "craftableall", StringComparison.OrdinalIgnoreCase) && !string.Equals(categoryCode, "craftable", StringComparison.OrdinalIgnoreCase) && !string.Equals(categoryCode, "craftablemods", StringComparison.OrdinalIgnoreCase) && !string.Equals(categoryCode, "craftablewoodtypes", StringComparison.OrdinalIgnoreCase))
		{
			return string.Equals(categoryCode, "craftablestonetypes", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	internal static string GetCraftableTabFontName(string categoryCode)
	{
		if (UseDefaultFont)
		{
			return null;
		}
		if (string.Equals(categoryCode, "craftableall", StringComparison.OrdinalIgnoreCase))
		{
			return "Arial Black";
		}
		if (string.Equals(categoryCode, "craftable", StringComparison.OrdinalIgnoreCase) || string.Equals(categoryCode, "craftablemods", StringComparison.OrdinalIgnoreCase) || string.Equals(categoryCode, "craftablewoodtypes", StringComparison.OrdinalIgnoreCase) || string.Equals(categoryCode, "craftablestonetypes", StringComparison.OrdinalIgnoreCase))
		{
			return "Arial";
		}
		return null;
	}

	internal static FontWeight? GetCraftableTabFontWeight(string categoryCode)
	{
		if (UseDefaultFont)
		{
			return null;
		}
		if (string.Equals(categoryCode, "craftable", StringComparison.OrdinalIgnoreCase) || string.Equals(categoryCode, "craftablemods", StringComparison.OrdinalIgnoreCase) || string.Equals(categoryCode, "craftablewoodtypes", StringComparison.OrdinalIgnoreCase) || string.Equals(categoryCode, "craftablestonetypes", StringComparison.OrdinalIgnoreCase))
		{
			return (FontWeight)1;
		}
		return null;
	}

	private static ulong ComputeResourcePoolDNA(ResourcePool pool)
	{
		//IL_0061: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Expected I4, but got Unknown
		if (pool == null)
		{
			return 0uL;
		}
		List<(string, int, int)> list = new List<(string, int, int)>();
		foreach (KeyValuePair<Key, int> count in pool.Counts)
		{
			string item = count.Key.Code ?? "";
			int item2 = Math.Max(0, count.Value);
			int item3 = 0;
			try
			{
				if (pool.Classes.TryGetValue(count.Key, out var value))
				{
					item3 = (int)value;
				}
			}
			catch
			{
			}
			list.Add((item, item2, item3));
		}
		list.Sort(((string Code, int Qty, int Cls) a, (string Code, int Qty, int Cls) b) => string.CompareOrdinal(a.Code, b.Code));
		ulong num = 14695981039346656037uL;
		foreach (var item7 in list)
		{
			string item4 = item7.Item1;
			int item5 = item7.Item2;
			int item6 = item7.Item3;
			num = FnvAddStr(num, item4 ?? "");
			num = FnvAddChar(num, '|');
			num = FnvAddInt(num, item6);
			num = FnvAddChar(num, '|');
			num = FnvAddInt(num, item5);
		}
		return num;
		static ulong FnvAddChar(ulong h, char c)
		{
			h ^= (byte)c;
			h *= 1099511628211L;
			return h;
		}
		static ulong FnvAddInt(ulong h, int v)
		{
			h ^= (byte)v;
			h *= 1099511628211L;
			h ^= (byte)(v >> 8);
			h *= 1099511628211L;
			h ^= (byte)(v >> 16);
			h *= 1099511628211L;
			h ^= (byte)(v >> 24);
			h *= 1099511628211L;
			return h;
		}
		static ulong FnvAddStr(ulong h, string s)
		{
			if (s == null)
			{
				return h;
			}
			for (int i = 0; i < s.Length; i++)
			{
				h ^= (byte)s[i];
				h *= 1099511628211L;
			}
			return h;
		}
	}

	internal static void AcquireHandbookPauseGuard(ICoreClientAPI capi)
	{
		HandbookPauseGuard.Acquire(capi);
	}

	internal static void ReleaseHandbookPauseGuard(ICoreClientAPI capi)
	{
		HandbookPauseGuard.Release(capi);
	}

	internal static bool TryBeginFetch()
	{
		if (ScanInProgress)
		{
			return false;
		}
		return Interlocked.CompareExchange(ref FetchInProgressFlag, 1, 0) == 0;
	}

	internal static void EndFetch()
	{
		Interlocked.Exchange(ref FetchInProgressFlag, 0);
	}

	internal static bool IsFetchInProgress()
	{
		return Volatile.Read(in FetchInProgressFlag) != 0;
	}

	internal static bool IsScanInProgress()
	{
		return ScanInProgress;
	}

	private static string GetVariantKey(bool includeAll, bool modsOnly, bool woodOnly, bool stoneOnly)
	{
		if (includeAll)
		{
			return "all";
		}
		if (modsOnly)
		{
			return "mods";
		}
		if (woodOnly)
		{
			return "wood";
		}
		if (stoneOnly)
		{
			return "stone";
		}
		return "van";
	}

	private static string GetTabKey(bool includeAll, bool modsOnly, bool woodOnly, bool stoneOnly)
	{
		if (includeAll)
		{
			return "allTab";
		}
		if (modsOnly)
		{
			return "modTab";
		}
		if (woodOnly)
		{
			return "woodTab";
		}
		if (stoneOnly)
		{
			return "stoneTab";
		}
		return "craftableTab";
	}

	private static string TabKeyFromVariant(string variantKey)
	{
		return variantKey switch
		{
			"all" => "allTab", 
			"mods" => "modTab", 
			"wood" => "woodTab", 
			"stone" => "stoneTab", 
			"van" => "craftableTab", 
			_ => "craftableTab", 
		};
	}

	private static string VariantKeyFromTabKey(string tabKey)
	{
		if (string.Equals(tabKey, "allTab", StringComparison.Ordinal))
		{
			return "all";
		}
		if (string.Equals(tabKey, "modTab", StringComparison.Ordinal))
		{
			return "mods";
		}
		if (string.Equals(tabKey, "woodTab", StringComparison.Ordinal))
		{
			return "wood";
		}
		if (string.Equals(tabKey, "stoneTab", StringComparison.Ordinal))
		{
			return "stone";
		}
		return "van";
	}

	private static string GetActiveTabKey()
	{
		if (CraftableAllTabActive)
		{
			return "allTab";
		}
		if (CraftableModsTabActive)
		{
			return "modTab";
		}
		if (CraftableWoodTabActive)
		{
			return "woodTab";
		}
		if (CraftableStoneTabActive)
		{
			return "stoneTab";
		}
		_ = CraftableTabActive;
		return "craftableTab";
	}

	private static bool IsTabActive(string tabKey)
	{
		if (string.Equals(tabKey, "allTab", StringComparison.Ordinal))
		{
			return CraftableAllTabActive;
		}
		if (string.Equals(tabKey, "modTab", StringComparison.Ordinal))
		{
			return CraftableModsTabActive;
		}
		if (string.Equals(tabKey, "woodTab", StringComparison.Ordinal))
		{
			return CraftableWoodTabActive;
		}
		if (string.Equals(tabKey, "stoneTab", StringComparison.Ordinal))
		{
			return CraftableStoneTabActive;
		}
		if (string.Equals(tabKey, "craftableTab", StringComparison.Ordinal))
		{
			return CraftableTabActive;
		}
		return false;
	}

	private static bool IsKnownTabKey(string tabKey)
	{
		if (!string.Equals(tabKey, "allTab", StringComparison.Ordinal) && !string.Equals(tabKey, "modTab", StringComparison.Ordinal) && !string.Equals(tabKey, "woodTab", StringComparison.Ordinal) && !string.Equals(tabKey, "stoneTab", StringComparison.Ordinal))
		{
			return string.Equals(tabKey, "craftableTab", StringComparison.Ordinal);
		}
		return true;
	}

	private static List<string> GetTabCache(string tabKey)
	{
		if (string.Equals(tabKey, "allTab", StringComparison.Ordinal))
		{
			return AllTabCache;
		}
		if (string.Equals(tabKey, "modTab", StringComparison.Ordinal))
		{
			return ModTabCache;
		}
		if (string.Equals(tabKey, "woodTab", StringComparison.Ordinal))
		{
			return WoodTypeTabCache;
		}
		if (string.Equals(tabKey, "stoneTab", StringComparison.Ordinal))
		{
			return StoneTypeTabCache;
		}
		if (string.Equals(tabKey, "craftableTab", StringComparison.Ordinal))
		{
			return CraftableTabCache;
		}
		return s_EmptyPages ?? (s_EmptyPages = new List<string>());
	}

	private static void SetTabCache(string tabKey, IEnumerable<string> pages)
	{
		if (!IsKnownTabKey(tabKey))
		{
			LogEverywhere(_staticCapi, "[Cache] Ignoring SetTabCache for unknown tabKey='" + tabKey + "'", toChat: true, "SetTabCache");
			return;
		}
		List<string> list = ((pages != null) ? new List<string>(pages) : new List<string>());
		if (string.Equals(tabKey, "allTab", StringComparison.Ordinal))
		{
			AllTabCache = list;
		}
		else if (string.Equals(tabKey, "modTab", StringComparison.Ordinal))
		{
			ModTabCache = list;
		}
		else if (string.Equals(tabKey, "woodTab", StringComparison.Ordinal))
		{
			WoodTypeTabCache = list;
		}
		else if (string.Equals(tabKey, "stoneTab", StringComparison.Ordinal))
		{
			StoneTypeTabCache = list;
		}
		else
		{
			CraftableTabCache = list;
		}
	}

	private static List<string> GetTabCacheSnapshot(string tabKey)
	{
		List<string> tabCache = GetTabCache(tabKey);
		if (tabCache == null)
		{
			return new List<string>();
		}
		return new List<string>(tabCache);
	}

	private static void SetTabReadyToUpdateUI(string tabKey, bool ready)
	{
		if (string.IsNullOrEmpty(tabKey))
		{
			return;
		}
		lock (TabUiStateLock)
		{
			TabReadyToUpdateUi[tabKey] = ready;
		}
	}

	private static bool TryAcquireTabUpdateTicket(string tabKey, bool requireReadyFlag)
	{
		if (string.IsNullOrEmpty(tabKey))
		{
			return false;
		}
		lock (TabUiStateLock)
		{
			if (!IsTabActive(tabKey))
			{
				return false;
			}
			if (TabReadyToUpdateUi.TryGetValue(tabKey, out var value) && value)
			{
				TabReadyToUpdateUi[tabKey] = false;
				return true;
			}
			if (!requireReadyFlag)
			{
				TabReadyToUpdateUi[tabKey] = false;
				return true;
			}
			return false;
		}
	}

	private static string FormatTabScanState(string tabKey)
	{
		if (string.IsNullOrEmpty(tabKey))
		{
			return "dna=<none>, cachePages=0";
		}
		bool flag;
		ulong value;
		lock (DnaLock)
		{
			flag = TabPoolDNA.TryGetValue(tabKey, out value);
		}
		int value2;
		lock (CacheLock)
		{
			value2 = GetTabCache(tabKey)?.Count ?? 0;
		}
		string value3 = (flag ? $"0x{value:X16}" : "<none>");
		return $"dna={value3}, cachePages={value2}";
	}

	private static string GetAttrStringSafe(ItemStack st, string key)
	{
		try
		{
			object result;
			if (st == null)
			{
				result = null;
			}
			else
			{
				ITreeAttribute attributes = st.Attributes;
				result = ((attributes != null) ? attributes.GetString(key, (string)null) : null);
			}
			return (string)result;
		}
		catch
		{
			return null;
		}
	}

	private static StackKey KeyFor(ItemStack st)
	{
		string? code = ((st == null) ? null : ((object)((RegistryObject)(st.Collectible?)).Code)?.ToString()) ?? "";
		string material = GetAttrStringSafe(st, "material") ?? "";
		string type = GetAttrStringSafe(st, "type") ?? "";
		return new StackKey(code, material, type);
	}

	private static ItemStack MakeStackFromCodeAndAttrs(ICoreClientAPI capi, string code, string material, string type)
	{
		ItemStack val = MakeStackFromCode(capi, code);
		if (val == null)
		{
			return null;
		}
		try
		{
			if (!string.IsNullOrEmpty(material))
			{
				val.Attributes.SetString("material", material);
			}
			if (!string.IsNullOrEmpty(type))
			{
				val.Attributes.SetString("type", type);
			}
		}
		catch
		{
		}
		return val;
	}

	private static ItemStack KeyToItemStack(ICoreClientAPI capi, StackKey key)
	{
		return MakeStackFromCodeAndAttrs(capi, key.Code, key.Material, key.Type);
	}

	private static string ExtractTokenFromPath(string patternPath, string codePath)
	{
		if (string.IsNullOrEmpty(patternPath) || string.IsNullOrEmpty(codePath))
		{
			return null;
		}
		int num = patternPath.IndexOf('*');
		if (num < 0)
		{
			return null;
		}
		string text = patternPath.Substring(0, num);
		string text2 = patternPath.Substring(num + 1);
		if (!codePath.StartsWith(text) || !codePath.EndsWith(text2))
		{
			return null;
		}
		return codePath.Substring(text.Length, codePath.Length - text.Length - text2.Length);
	}

	private static string PathPart(string domainCode)
	{
		int num = domainCode?.IndexOf(':') ?? (-1);
		string text;
		if (num < 0)
		{
			text = domainCode;
			if (text == null)
			{
				return "";
			}
		}
		else
		{
			text = domainCode.Substring(num + 1);
		}
		return text;
	}

	private static Dictionary<string, int> GetWildcardTokenCounts(ResourcePool pool, AssetLocation pattern, string[] allowed)
	{
		//IL_00fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_0105: Expected O, but got Unknown
		Dictionary<string, int> dictionary = new Dictionary<string, int>(StringComparer.Ordinal);
		if (pool == null || pattern == (AssetLocation)null)
		{
			return dictionary;
		}
		string text = ((allowed != null && allowed.Length != 0) ? string.Join(",", allowed.OrderBy((string x) => x)) : "");
		string key = pool.GetSignature() + "||" + ((object)pattern).ToString() + "||" + text;
		lock (WildTokenCountsMemo)
		{
			if (WildTokenCountsMemo.TryGetValue(key, out var value))
			{
				return value;
			}
		}
		string patternPath = pattern.Path ?? ((object)pattern).ToString();
		foreach (KeyValuePair<Key, int> count in pool.Counts)
		{
			string code = count.Key.Code;
			try
			{
				AssetLocation val = new AssetLocation(code);
				if (WildcardUtil.Match(pattern, val, (allowed != null && allowed.Length != 0) ? allowed : null))
				{
					string text2 = ExtractTokenFromPath(patternPath, PathPart(val.Path ?? ((object)val).ToString()));
					if (!string.IsNullOrEmpty(text2))
					{
						dictionary[text2] = (dictionary.TryGetValue(text2, out var value2) ? (value2 + count.Value) : count.Value);
					}
				}
			}
			catch
			{
			}
		}
		lock (WildTokenCountsMemo)
		{
			WildTokenCountsMemo[key] = dictionary;
			return dictionary;
		}
	}

	private static void ExpandOutputsForRecipe(ICoreClientAPI capi, ResourcePool pool, GridRecipeShim recipe, HashSet<StackKey> dest, Dictionary<GridRecipeShim, Dictionary<string, int>> originalNeeds)
	{
		//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
		if (recipe?.Outputs == null || recipe.Outputs.Count == 0)
		{
			return;
		}
		GridIngredientShim gridIngredientShim = recipe.Ingredients?.FirstOrDefault((GridIngredientShim i) => i != null && i.IsWild && i.PatternCode != (AssetLocation)null);
		string[] array = gridIngredientShim?.Allowed;
		int value = 0;
		if (gridIngredientShim != null && originalNeeds != null && originalNeeds.TryGetValue(recipe, out var value2) && value2 != null)
		{
			string key = $"wild:{gridIngredientShim.PatternCode}|{string.Join(",", (array ?? Array.Empty<string>()).OrderBy((string x) => x))}|T:{gridIngredientShim.Type}";
			if (!value2.TryGetValue(key, out value))
			{
				value = Math.Max(1, gridIngredientShim.QuantityRequired);
			}
		}
		Dictionary<string, int> dictionary = null;
		if (gridIngredientShim != null)
		{
			dictionary = GetWildcardTokenCounts(pool, gridIngredientShim.PatternCode, array);
		}
		foreach (ItemStack output in recipe.Outputs)
		{
			string text = ((output == null) ? null : ((object)((RegistryObject)(output.Collectible?)).Code)?.ToString());
			if (string.IsNullOrEmpty(text))
			{
				continue;
			}
			string attrStringSafe = GetAttrStringSafe(output, "type");
			string attrStringSafe2 = GetAttrStringSafe(output, "material");
			if (gridIngredientShim != null && dictionary != null && dictionary.Count > 0)
			{
				foreach (KeyValuePair<string, int> item in dictionary)
				{
					string key2 = item.Key;
					int value3 = item.Value;
					if (value > 0 && value3 < value)
					{
						continue;
					}
					string text2 = text;
					if (!string.IsNullOrEmpty(attrStringSafe2) && text2.Contains(attrStringSafe2))
					{
						text2 = text2.Replace(attrStringSafe2, key2);
					}
					text2 = text2.Replace("{wood}", key2).Replace("{rock}", key2);
					string text3 = attrStringSafe2;
					if (!string.IsNullOrEmpty(text3))
					{
						text3 = text3.Replace("{wood}", key2).Replace("{rock}", key2);
					}
					string text4 = attrStringSafe;
					if (!string.IsNullOrEmpty(text4))
					{
						text4 = text4.Replace("{wood}", key2).Replace("{rock}", key2);
					}
					if (DebugEnabled)
					{
						bool num = !string.Equals(text2, text, StringComparison.Ordinal);
						bool flag = (text != null && text.Contains("{wood}")) || (text != null && text.Contains("{rock}")) || (attrStringSafe2 != null && attrStringSafe2.Contains("{wood}")) || (attrStringSafe2?.Contains("{rock}") ?? false);
						if (num || flag)
						{
							LogEverywhere(capi, $"Expanded: {text} -> {text2} (token={key2}, material={text3 ?? attrStringSafe2 ?? "<null>"}, type={text4 ?? attrStringSafe ?? "<null>"})", toChat: false, "BuildRecipeIndex");
						}
					}
					dest.Add(new StackKey(text2, text3 ?? key2, text4 ?? ""));
				}
			}
			else
			{
				dest.Add(new StackKey(text, attrStringSafe2 ?? "", attrStringSafe ?? ""));
			}
		}
	}

	private static bool IsSep(char c)
	{
		if (c != '-' && c != '_' && c != '/')
		{
			return c == '.';
		}
		return true;
	}

	private static bool ContainsWoodMatInCode(string code)
	{
		if (string.IsNullOrEmpty(code))
		{
			return false;
		}
		string[] woodSpecies = WoodSpecies;
		foreach (string text in woodSpecies)
		{
			for (int num = code.IndexOf(text, StringComparison.OrdinalIgnoreCase); num >= 0; num = code.IndexOf(text, num + text.Length, StringComparison.OrdinalIgnoreCase))
			{
				bool num2 = num == 0 || IsSep(code[num - 1]);
				int num3 = num + text.Length;
				bool flag = num3 >= code.Length || IsSep(code[num3]);
				if (num2 && flag)
				{
					return true;
				}
			}
		}
		return false;
	}

	private static bool ContainsWoodMatInAttributes(string s)
	{
		if (string.IsNullOrEmpty(s))
		{
			return false;
		}
		char[] array = new char[s.Length];
		int length = 0;
		foreach (char c in s)
		{
			if (!char.IsWhiteSpace(c))
			{
				array[length++] = char.ToLowerInvariant(c);
			}
		}
		string text = new string(array, 0, length);
		string text2 = ExtractValue(text, "material");
		string[] woodSpecies;
		if (text2 != null)
		{
			if (text2 == "{wood}")
			{
				return true;
			}
			woodSpecies = WoodSpecies;
			foreach (string text3 in woodSpecies)
			{
				if (text2 == text3)
				{
					return true;
				}
			}
		}
		string text4 = ExtractValue(text, "type");
		if (text4 != null)
		{
			if (text4 == "wood-{wood}")
			{
				return true;
			}
			woodSpecies = WoodSpecies;
			foreach (string text5 in woodSpecies)
			{
				if (text4 == "wood-" + text5)
				{
					return true;
				}
			}
		}
		woodSpecies = WoodSpecies;
		foreach (string text6 in woodSpecies)
		{
			if (text.IndexOf("wood-" + text6, StringComparison.Ordinal) >= 0)
			{
				return true;
			}
		}
		return false;
		static string ExtractValue(string text7, string key)
		{
			int num = text7.IndexOf(key + ":", StringComparison.Ordinal);
			if (num >= 0)
			{
				num += key.Length + 1;
			}
			else
			{
				num = text7.IndexOf("\"" + key + "\":", StringComparison.Ordinal);
				if (num < 0)
				{
					num = text7.IndexOf("'" + key + "':", StringComparison.Ordinal);
				}
				if (num < 0)
				{
					return null;
				}
				num += key.Length + 3;
			}
			if (num >= text7.Length)
			{
				return null;
			}
			char c2 = ((text7[num] == '"' || text7[num] == '\'') ? text7[num++] : '\0');
			int num2 = num;
			for (; num < text7.Length; num++)
			{
				char c3 = text7[num];
				if ((c2 != 0 && c3 == c2) || (c2 == '\0' && (c3 == ',' || c3 == '}' || c3 == ']')))
				{
					break;
				}
			}
			return text7.Substring(num2, num - num2);
		}
	}

	private static bool ContainsStoneMatInCode(string code)
	{
		if (string.IsNullOrEmpty(code))
		{
			return false;
		}
		string[] stoneSpecies = StoneSpecies;
		foreach (string text in stoneSpecies)
		{
			for (int num = code.IndexOf(text, StringComparison.OrdinalIgnoreCase); num >= 0; num = code.IndexOf(text, num + text.Length, StringComparison.OrdinalIgnoreCase))
			{
				bool num2 = num == 0 || IsSep(code[num - 1]);
				int num3 = num + text.Length;
				bool flag = num3 >= code.Length || IsSep(code[num3]);
				if (num2 && flag)
				{
					return true;
				}
			}
		}
		return false;
	}

	private static bool ContainsStoneMatInAttributes(string s)
	{
		if (string.IsNullOrEmpty(s))
		{
			return false;
		}
		char[] array = new char[s.Length];
		int length = 0;
		foreach (char c in s)
		{
			if (!char.IsWhiteSpace(c))
			{
				array[length++] = char.ToLowerInvariant(c);
			}
		}
		string text = new string(array, 0, length);
		string text2 = ExtractValue(text, "material");
		string[] stoneSpecies;
		if (text2 != null)
		{
			stoneSpecies = StoneSpecies;
			foreach (string text3 in stoneSpecies)
			{
				if (text2 == text3)
				{
					return true;
				}
			}
		}
		string text4 = ExtractValue(text, "type");
		if (text4 != null)
		{
			stoneSpecies = StoneSpecies;
			foreach (string text5 in stoneSpecies)
			{
				if (text4 == "stone-" + text5)
				{
					return true;
				}
			}
		}
		stoneSpecies = StoneSpecies;
		foreach (string text6 in stoneSpecies)
		{
			if (text.IndexOf("stone-" + text6, StringComparison.Ordinal) >= 0)
			{
				return true;
			}
		}
		return false;
		static string ExtractValue(string text7, string key)
		{
			int num = text7.IndexOf(key + ":", StringComparison.Ordinal);
			if (num >= 0)
			{
				num += key.Length + 1;
			}
			else
			{
				num = text7.IndexOf("\"" + key + "\":", StringComparison.Ordinal);
				if (num < 0)
				{
					num = text7.IndexOf("'" + key + "':", StringComparison.Ordinal);
				}
				if (num < 0)
				{
					return null;
				}
				num += key.Length + 3;
			}
			if (num >= text7.Length)
			{
				return null;
			}
			char c2 = ((text7[num] == '"' || text7[num] == '\'') ? text7[num++] : '\0');
			int num2 = num;
			for (; num < text7.Length; num++)
			{
				char c3 = text7[num];
				if ((c2 != 0 && c3 == c2) || (c2 == '\0' && (c3 == ',' || c3 == '}' || c3 == ']')))
				{
					break;
				}
			}
			return text7.Substring(num2, num - num2);
		}
	}

	private static bool IsStoneRecipe(object recipeOrOutput)
	{
		if (recipeOrOutput == null)
		{
			return false;
		}
		Type type = recipeOrOutput.GetType();
		if (CheckOne(TryGetMember(type, recipeOrOutput, "Output")))
		{
			return true;
		}
		if (TryGetMember(type, recipeOrOutput, "Outputs") is IEnumerable enumerable)
		{
			foreach (object item in enumerable)
			{
				if (CheckOne(item))
				{
					return true;
				}
			}
		}
		return CheckOne(recipeOrOutput);
		static bool CheckOne(object o)
		{
			if (o == null)
			{
				return false;
			}
			string text = null;
			string text2 = null;
			ItemStack val = (ItemStack)((o is ItemStack) ? o : null);
			if (val != null)
			{
				text = ((object)((RegistryObject)(val.Collectible?)).Code)?.ToString();
				ITreeAttribute attributes = val.Attributes;
				text2 = ((attributes == null) ? null : ((IAttribute)attributes).ToJsonToken()?.ToString());
			}
			else
			{
				Type type2 = o.GetType();
				object obj = TryGetMember(type2, o, "Code");
				text = ((obj is AssetLocation) ? obj : null)?.ToString() ?? (TryGetMember(type2, o, "code") as string);
				object obj2 = TryGetMember(type2, o, "Attributes");
				if (obj2 == null)
				{
					object obj3 = TryGetMember(type2, o, "ResolvedItemstack");
					ItemStack val2 = (ItemStack)((obj3 is ItemStack) ? obj3 : null);
					if (val2 != null)
					{
						if (text == null)
						{
							text = ((object)((RegistryObject)(val2.Collectible?)).Code)?.ToString();
						}
						obj2 = val2.Attributes;
					}
				}
				IAttribute val3 = (IAttribute)((obj2 is IAttribute) ? obj2 : null);
				text2 = ((val3 == null) ? obj2?.ToString() : val3.ToJsonToken());
			}
			if (!string.IsNullOrEmpty(text) && ContainsStoneMatInCode(text))
			{
				return true;
			}
			if (!string.IsNullOrEmpty(text2) && ContainsStoneMatInAttributes(text2))
			{
				return true;
			}
			return false;
		}
	}

	private static void LogEverywhere(ICoreClientAPI capi, string msg, bool toChat = false, [CallerMemberName] string caller = null)
	{
		if (!DebugEnabled)
		{
			return;
		}
		string text = "[Craftable] " + caller + ": " + msg;
		try
		{
			ILogger logger = ((ICoreAPI)capi).Logger;
			if (logger != null)
			{
				logger.Notification(text);
			}
		}
		catch
		{
		}
		try
		{
			IClientWorldAccessor world = capi.World;
			if (world != null)
			{
				ILogger logger2 = ((IWorldAccessor)world).Logger;
				if (logger2 != null)
				{
					logger2.Notification(text);
				}
			}
		}
		catch
		{
		}
		if (toChat)
		{
			try
			{
				capi.ShowChatMessage(text);
			}
			catch
			{
			}
		}
		try
		{
			string text2 = null;
			MethodInfo method = typeof(ICoreAPI).GetMethod("GetOrCreateDataPath", BindingFlags.Instance | BindingFlags.Public);
			if (method != null)
			{
				text2 = (string)method.Invoke(capi, new object[1] { "ShowCraftable" });
			}
			if (string.IsNullOrEmpty(text2))
			{
				text2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShowCraftable");
			}
			Directory.CreateDirectory(text2);
			string path = Path.Combine(text2, "craftable.log");
			lock (LogFileLock)
			{
				File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [Craftable] {caller}: {msg}\n");
			}
		}
		catch
		{
		}
	}

	internal static IInventory TryGetInventoryFromBE(BlockEntity be)
	{
		if (be == null)
		{
			return null;
		}
		IBlockEntityContainer val = (IBlockEntityContainer)(object)((be is IBlockEntityContainer) ? be : null);
		if (val != null && val.Inventory != null)
		{
			return val.Inventory;
		}
		Type type = ((object)be).GetType();
		object obj = type.GetProperty("Inventory", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(be);
		if (obj == null)
		{
			obj = type.GetField("Inventory", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(be);
		}
		if (obj == null)
		{
			obj = type.GetField("inventory", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(be);
		}
		return (IInventory)((obj is IInventory) ? obj : null);
	}

	private static void RequestServerScan(ICoreClientAPI capi, int radius, bool includeAll, bool modsOnly, bool woodOnly, bool stoneOnly, string tabKey, bool allowQueue = true)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		try
		{
			if (capi == null)
			{
				return;
			}
			DateTime utcNow = DateTime.UtcNow;
			if ((utcNow - _lastScanAt).TotalMilliseconds < 400.0 && allowQueue)
			{
				EnqueueScanRequest(capi, includeAll, modsOnly, woodOnly, stoneOnly, tabKey);
				return;
			}
			_lastScanAt = utcNow;
			if (ScanInProgress)
			{
				if (allowQueue)
				{
					EnqueueScanRequest(capi, includeAll, modsOnly, woodOnly, stoneOnly, tabKey);
				}
				return;
			}
			ScanInProgress = true;
			HandbookPauseGuard.Acquire(capi);
			string variantKey = GetVariantKey(includeAll, modsOnly, woodOnly, stoneOnly);
			lock (PendingScanLock)
			{
				PendingScanVariantKey = variantKey;
				PendingScanTabKey = tabKey;
			}
			int num = Interlocked.Increment(ref ScanSeq);
			lock (InflightMapLock)
			{
				InflightById[num] = new ScanRequestInfo(includeAll, modsOnly, woodOnly, stoneOnly, tabKey);
			}
			try
			{
				capi.Network.GetChannel("showcraftablescan").SendPacket<CraftScanRequest>(new CraftScanRequest
				{
					Radius = radius,
					CollectItems = true,
					Variants = new List<CraftIngredientList>(),
					ScanId = num,
					TabKey = tabKey
				});
				LogEverywhere(capi, $"[Scan] → sent ScanId={num} (radius={radius}, variant={variantKey}, tab={tabKey})", toChat: false, "RequestServerScan");
			}
			catch (Exception value)
			{
				lock (PendingScanLock)
				{
					PendingScanVariantKey = null;
					PendingScanTabKey = null;
				}
				lock (InflightMapLock)
				{
					InflightById.Remove(num);
				}
				FinishScan(capi);
				LogEverywhere(capi, $"Failed to send scan request: {value}", toChat: true, "RequestServerScan");
				((IEventAPI)capi.Event).EnqueueMainThreadTask((Action)delegate
				{
					TryProcessQueuedScan(capi);
				}, "SCProcessScanQueueFail");
			}
		}
		finally
		{
			stopwatch.Stop();
			LogEverywhere(capi, $"RequestServerScan completed in {stopwatch.ElapsedMilliseconds}ms", toChat: false, "RequestServerScan");
		}
	}

	private static void EnqueueScanRequest(ICoreClientAPI capi, bool includeAll, bool modsOnly, bool woodOnly, bool stoneOnly, string tabKey)
	{
		lock (ScanQueueLock)
		{
			QueuedScanRequest = new ScanRequestInfo(includeAll, modsOnly, woodOnly, stoneOnly, tabKey);
		}
		EnsureQueueCheckScheduled(capi);
	}

	private static void EnsureQueueCheckScheduled(ICoreClientAPI capi)
	{
		if (capi == null)
		{
			return;
		}
		bool flag = false;
		lock (ScanQueueLock)
		{
			if (QueuedScanRequest.HasValue && !ScanQueueCheckScheduled)
			{
				ScanQueueCheckScheduled = true;
				flag = true;
			}
		}
		if (!flag)
		{
			return;
		}
		((IEventAPI)capi.Event).RegisterCallback((Action<float>)delegate
		{
			lock (ScanQueueLock)
			{
				ScanQueueCheckScheduled = false;
			}
			TryProcessQueuedScan(capi);
		}, 300, true);
	}

	private static void TryProcessQueuedScan(ICoreClientAPI capi)
	{
		if (capi == null)
		{
			return;
		}
		ScanRequestInfo? scanRequestInfo = null;
		lock (ScanQueueLock)
		{
			if (!ScanInProgress && QueuedScanRequest.HasValue)
			{
				scanRequestInfo = QueuedScanRequest;
				QueuedScanRequest = null;
			}
		}
		if (scanRequestInfo.HasValue)
		{
			RequestServerScan(capi, NearbyRadius, scanRequestInfo.Value.IncludeAll, scanRequestInfo.Value.ModsOnly, scanRequestInfo.Value.WoodOnly, scanRequestInfo.Value.StoneOnly, scanRequestInfo.Value.TabKey, allowQueue: false);
		}
		else
		{
			EnsureQueueCheckScheduled(capi);
		}
	}

	private static void FinishScan(ICoreClientAPI capi)
	{
		ScanInProgress = false;
		EndFetch();
		HandbookPauseGuard.Release(capi);
	}

	private static void FinishScanAndProcessQueue(ICoreClientAPI capi)
	{
		FinishScan(capi);
		TryProcessQueuedScan(capi);
	}

	public override void Start(ICoreAPI api)
	{
		((ModSystem)this).Start(api);
		LoadConfig(api);
	}

	private static void LoadConfig(ICoreAPI api)
	{
		if (api == null)
		{
			return;
		}
		ShowCraftableConfig showCraftableConfig = null;
		try
		{
			showCraftableConfig = ((ICoreAPICommon)api).LoadModConfig<ShowCraftableConfig>("ShowCraftable.json");
		}
		catch (Exception ex)
		{
			ILogger logger = api.Logger;
			if (logger != null)
			{
				logger.Warning("[ShowCraftable] Failed to load config {0}: {1}", new object[2] { "ShowCraftable.json", ex });
			}
		}
		if (showCraftableConfig == null)
		{
			showCraftableConfig = new ShowCraftableConfig();
		}
		showCraftableConfig.Normalize();
		Config = showCraftableConfig;
		NearbyRadius = ConfiguredSearchRadius;
		try
		{
			((ICoreAPICommon)api).StoreModConfig<ShowCraftableConfig>(Config, "ShowCraftable.json");
		}
		catch (Exception ex2)
		{
			ILogger logger2 = api.Logger;
			if (logger2 != null)
			{
				logger2.Warning("[ShowCraftable] Failed to save config {0}: {1}", new object[2] { "ShowCraftable.json", ex2 });
			}
		}
	}

	public override void StartClientSide(ICoreClientAPI capi)
	{
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Expected O, but got Unknown
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_006f: Expected O, but got Unknown
		//IL_00e4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f0: Expected O, but got Unknown
		//IL_0120: Unknown result type (might be due to invalid IL or missing references)
		//IL_012d: Expected O, but got Unknown
		//IL_0156: Unknown result type (might be due to invalid IL or missing references)
		//IL_0162: Expected O, but got Unknown
		//IL_018a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0196: Expected O, but got Unknown
		//IL_01c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d4: Expected O, but got Unknown
		_capi = capi;
		_staticCapi = capi;
		_harmony = new Harmony("showcraftable.core");
		MethodInfo methodInfo = AccessTools.Method(typeof(GuiComposerHelpers), "AddVerticalTabs", (Type[])null, (Type[])null);
		_harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(ShowCraftableSystem), "AddVerticalTabs_Prefix", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		capi.Network.RegisterChannel("showcraftablescan").RegisterMessageType(typeof(CraftScanRequest)).RegisterMessageType(typeof(CraftScanReply))
			.SetMessageHandler<CraftScanReply>((NetworkServerMessageHandler<CraftScanReply>)OnServerScanReply);
		MethodInfo methodInfo2 = AccessTools.Method(AccessTools.TypeByName("Vintagestory.GameContent.GuiDialogSurvivalHandbook"), "genTabs", (Type[])null, (Type[])null);
		_harmony.Patch((MethodBase)methodInfo2, (HarmonyMethod)null, new HarmonyMethod(typeof(ShowCraftableSystem), "GenTabs_Postfix", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
		Type type = AccessTools.TypeByName("Vintagestory.GameContent.GuiDialogHandbook");
		MethodInfo methodInfo3 = AccessTools.Method(type, "FilterItems", (Type[])null, (Type[])null);
		_harmony.Patch((MethodBase)methodInfo3, new HarmonyMethod(typeof(ShowCraftableSystem), "FilterItems_Prefix", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		MethodInfo methodInfo4 = AccessTools.Method(type, "selectTab", (Type[])null, (Type[])null);
		_harmony.Patch((MethodBase)methodInfo4, (HarmonyMethod)null, new HarmonyMethod(typeof(ShowCraftableSystem), "SelectTab_Postfix", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
		MethodInfo methodInfo5 = AccessTools.Method(type, "LoadPages_Async", (Type[])null, (Type[])null);
		_harmony.Patch((MethodBase)methodInfo5, (HarmonyMethod)null, new HarmonyMethod(typeof(ShowCraftableSystem), "AfterPagesLoaded_Postfix", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
		MethodInfo methodInfo6 = AccessTools.Method(AccessTools.TypeByName("Vintagestory.GameContent.CollectibleBehaviorHandbookTextAndExtraInfo"), "addCreatedByInfo", (Type[])null, (Type[])null);
		_harmony.Patch((MethodBase)methodInfo6, (HarmonyMethod)null, new HarmonyMethod(typeof(ShowCraftableSystem), "AddRecipeButton_Postfix", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
		capi.Event.LevelFinalize += delegate
		{
			lock (ScanQueueLock)
			{
				QueuedScanRequest = null;
				ScanQueueCheckScheduled = false;
			}
			lock (PendingScanLock)
			{
				PendingScanVariantKey = null;
				PendingScanTabKey = null;
			}
			lock (InflightMapLock)
			{
				InflightById.Clear();
			}
			ScanInProgress = false;
			InvalidatePageCodeMapCache();
			StartRecipeIndexBuild(capi);
		};
		capi.Event.LeaveWorld += OnLeaveWorld;
	}

	private void OnLeaveWorld()
	{
		ClearAllCaches();
		_capi = null;
	}

	public override void Dispose()
	{
		Harmony harmony = _harmony;
		if (harmony != null)
		{
			harmony.UnpatchAll("showcraftable.core");
		}
	}

	public static bool AddVerticalTabs_Prefix(GuiComposer composer, GuiTab[] tabs, ElementBounds bounds, Action<int, GuiTab> onTabClicked, string key, ref GuiComposer __result)
	{
		if (composer == null || composer.Composed)
		{
			return true;
		}
		if (!ShouldApplyCraftableFonts(tabs))
		{
			return true;
		}
		CairoFont font = CairoFont.WhiteDetailText().WithFontSize(17f);
		CairoFont selectedFont = CairoFont.WhiteDetailText().WithFontSize(17f).WithColor(GuiStyle.ActiveButtonTextColor);
		composer.AddInteractiveElement((GuiElement)(object)new GuiElementVerticalTabsWithCustomFonts(composer.Api, tabs, font, selectedFont, bounds, onTabClicked), key);
		__result = composer;
		return false;
	}

	private static bool ShouldApplyCraftableFonts(GuiTab[] tabs)
	{
		if (tabs == null || tabs.Length == 0)
		{
			return false;
		}
		if (UseDefaultFont)
		{
			return false;
		}
		for (int i = 0; i < tabs.Length; i++)
		{
			if (GetCraftableTabFontName(TryGetCategoryCode(tabs[i])) != null)
			{
				return true;
			}
		}
		return false;
	}

	internal static string TryGetCategoryCode(GuiTab tab)
	{
		if (tab == null)
		{
			return null;
		}
		HandbookTab val = (HandbookTab)(object)((tab is HandbookTab) ? tab : null);
		if (val != null)
		{
			return val.CategoryCode;
		}
		Type type = ((object)tab).GetType();
		PropertyInfo property = type.GetProperty("CategoryCode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (property != null)
		{
			return property.GetValue(tab) as string;
		}
		return type.GetField("CategoryCode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tab) as string;
	}

	public static void GenTabs_Postfix(object __instance, ref object __result, ref int curTab)
	{
		try
		{
			List<object> list = ((Array)__result)?.Cast<object>().ToList() ?? new List<object>();
			if (list.Count == 0)
			{
				return;
			}
			Type type = AccessTools.TypeByName("Vintagestory.GameContent.HandbookTab") ?? AccessTools.TypeByName("Vintagestory.GameContent.GuiTab");
			if (type == null)
			{
				return;
			}
			bool flag = false;
			bool flag2 = false;
			bool flag3 = false;
			bool flag4 = false;
			bool flag5 = false;
			foreach (object item in list)
			{
				string a = GetPF(type, item, "CategoryCode") as string;
				if (string.Equals(a, "craftableall", StringComparison.OrdinalIgnoreCase))
				{
					flag = true;
					SetPF(type, item, "Name", "Craftable");
				}
				if (string.Equals(a, "craftable", StringComparison.OrdinalIgnoreCase))
				{
					flag2 = true;
					SetPF(type, item, "Name", "● Base Items");
				}
				if (string.Equals(a, "craftablemods", StringComparison.OrdinalIgnoreCase))
				{
					flag3 = true;
					SetPF(type, item, "Name", "● Mod Items");
				}
				if (string.Equals(a, "craftablewoodtypes", StringComparison.OrdinalIgnoreCase))
				{
					flag4 = true;
					SetPF(type, item, "Name", "● Wood Types");
				}
				if (string.Equals(a, "craftablestonetypes", StringComparison.OrdinalIgnoreCase))
				{
					flag5 = true;
					SetPF(type, item, "Name", "● Stone Types");
				}
			}
			int num = list.Count;
			if (!flag)
			{
				object obj = Activator.CreateInstance(type);
				SetPF(type, obj, "Name", "Craftable");
				SetPF(type, obj, "CategoryCode", "craftableall");
				SetPF(type, obj, "DataInt", list.Count);
				SetPF(type, obj, "PaddingTop", 20.0);
				list.Insert(num, obj);
				num++;
				flag = true;
			}
			if (!flag2)
			{
				object obj2 = Activator.CreateInstance(type);
				SetPF(type, obj2, "Name", "● Base Items");
				SetPF(type, obj2, "CategoryCode", "craftable");
				SetPF(type, obj2, "DataInt", list.Count);
				SetPF(type, obj2, "PaddingTop", flag ? 5.0 : 20.0);
				list.Insert(num, obj2);
				num++;
			}
			if (!flag4)
			{
				object obj3 = Activator.CreateInstance(type);
				SetPF(type, obj3, "Name", "● Wood Types");
				SetPF(type, obj3, "CategoryCode", "craftablewoodtypes");
				SetPF(type, obj3, "DataInt", list.Count);
				SetPF(type, obj3, "PaddingTop", 5.0);
				list.Insert(num, obj3);
				num++;
			}
			if (!flag5)
			{
				object obj4 = Activator.CreateInstance(type);
				SetPF(type, obj4, "Name", "● Stone Types");
				SetPF(type, obj4, "CategoryCode", "craftablestonetypes");
				SetPF(type, obj4, "DataInt", list.Count);
				SetPF(type, obj4, "PaddingTop", 5.0);
				list.Insert(num, obj4);
				num++;
			}
			if (!flag3)
			{
				object obj5 = Activator.CreateInstance(type);
				SetPF(type, obj5, "Name", "● Mod Items");
				SetPF(type, obj5, "CategoryCode", "craftablemods");
				SetPF(type, obj5, "DataInt", list.Count);
				SetPF(type, obj5, "PaddingTop", 5.0);
				list.Insert(num, obj5);
				num++;
			}
			__result = ToTypedArray(type, list);
		}
		catch
		{
		}
		static object GetPF(Type t, object o, string name)
		{
			PropertyInfo propertyInfo = AccessTools.Property(t, name);
			if (propertyInfo != null)
			{
				return propertyInfo.GetValue(o);
			}
			return AccessTools.Field(t, name)?.GetValue(o);
		}
		static void SetPF(Type t, object o, string name, object val)
		{
			PropertyInfo propertyInfo = AccessTools.Property(t, name);
			if (propertyInfo != null && propertyInfo.CanWrite)
			{
				propertyInfo.SetValue(o, val);
			}
			else
			{
				FieldInfo fieldInfo = AccessTools.Field(t, name);
				if (fieldInfo != null)
				{
					fieldInfo.SetValue(o, val);
				}
			}
		}
	}

	private static Array ToTypedArray(Type elementType, List<object> list)
	{
		Array array = Array.CreateInstance(elementType, list.Count);
		for (int i = 0; i < list.Count; i++)
		{
			array.SetValue(list[i], i);
		}
		return array;
	}

	private static bool DialogIsOpen(object inst)
	{
		GuiDialog val = (GuiDialog)((inst is GuiDialog) ? inst : null);
		if (val != null)
		{
			return val.IsOpened();
		}
		MethodInfo method = inst.GetType().GetMethod("IsOpened", BindingFlags.Instance | BindingFlags.Public);
		if (method != null && method.ReturnType == typeof(bool))
		{
			return (bool)method.Invoke(inst, Array.Empty<object>());
		}
		return false;
	}

	public static void SelectTab_Postfix(object __instance, string code)
	{
		ICoreClientAPI capi = null;
		Stopwatch stopwatch = Stopwatch.StartNew();
		try
		{
			CraftableAllTabActive = string.Equals(code, "craftableall", StringComparison.Ordinal);
			CraftableTabActive = string.Equals(code, "craftable", StringComparison.Ordinal);
			CraftableModsTabActive = string.Equals(code, "craftablemods", StringComparison.Ordinal);
			CraftableWoodTabActive = string.Equals(code, "craftablewoodtypes", StringComparison.Ordinal);
			CraftableStoneTabActive = string.Equals(code, "craftablestonetypes", StringComparison.Ordinal);
			bool includeAll = CraftableAllTabActive;
			bool modsOnly = CraftableModsTabActive;
			bool woodOnly = CraftableWoodTabActive;
			bool stoneOnly = CraftableStoneTabActive;
			bool flag = CraftableAllTabActive || CraftableTabActive || CraftableModsTabActive || CraftableWoodTabActive || CraftableStoneTabActive;
			string tabKey = GetTabKey(includeAll, modsOnly, woodOnly, stoneOnly);
			string variantKey = GetVariantKey(includeAll, modsOnly, woodOnly, stoneOnly);
			if (!DialogIsOpen(__instance))
			{
				_pendingScanId++;
				return;
			}
			ref ICoreClientAPI reference = ref capi;
			object? obj = AccessTools.Field(__instance.GetType(), "capi")?.GetValue(__instance);
			reference = (ICoreClientAPI)(((obj is ICoreClientAPI) ? obj : null) ?? _staticCapi);
			string text = (flag ? (" (" + FormatTabScanState(tabKey) + ")") : string.Empty);
			if (CraftableAllTabActive)
			{
				LogEverywhere(capi, "Craftable tab selected by user" + text, toChat: false, "SelectTab_Postfix");
			}
			else if (CraftableTabActive)
			{
				LogEverywhere(capi, "Base Items tab selected by user" + text, toChat: false, "SelectTab_Postfix");
			}
			else if (CraftableModsTabActive)
			{
				LogEverywhere(capi, "Mod Items tab selected by user" + text, toChat: false, "SelectTab_Postfix");
			}
			else if (CraftableWoodTabActive)
			{
				LogEverywhere(capi, "Wood Types tab selected by user" + text, toChat: false, "SelectTab_Postfix");
			}
			else if (CraftableStoneTabActive)
			{
				LogEverywhere(capi, "Stone Types tab selected by user" + text, toChat: false, "SelectTab_Postfix");
			}
			object? obj2 = AccessTools.Field(__instance.GetType(), "overviewGui")?.GetValue(__instance);
			GuiComposer val = (GuiComposer)((obj2 is GuiComposer) ? obj2 : null);
			PropertyInfo propertyInfo = AccessTools.Property(__instance.GetType(), "SingleComposer") ?? AccessTools.Property(__instance.GetType().BaseType, "SingleComposer");
			try
			{
				propertyInfo?.SetValue(__instance, val);
			}
			catch
			{
			}
			if (!flag)
			{
				_pendingScanId++;
				return;
			}
			object obj4 = (((object)val)?.GetType().GetMethod("GetTextInput"))?.Invoke(val, new object[1] { "searchField" });
			obj4?.GetType().GetMethod("SetValue")?.Invoke(obj4, new object[2] { "", true });
			AccessTools.Field(__instance.GetType(), "currentSearchText")?.SetValue(__instance, null);
			bool flag2;
			lock (recipeIndexByVariant)
			{
				flag2 = recipeIndexByVariant.ContainsKey(variantKey);
			}
			if (flag2)
			{
				ApplyRecipeIndexVariant(variantKey);
			}
			else
			{
				StartRecipeIndexBuild(capi);
			}
			lock (CacheLock)
			{
				CachedPageCodes = GetTabCacheSnapshot(tabKey);
			}
			LastDialogPageCount = -1;
			TryRefreshOpenDialog(capi);
			TryUpdateActiveTabFromCache(capi, tabKey, requireReadyFlag: false);
			int myScanId = ++_pendingScanId;
			((IEventAPI)capi.Event).EnqueueMainThreadTask((Action)delegate
			{
				if (myScanId == _pendingScanId && DialogIsOpen(__instance))
				{
					RequestServerScan(capi, NearbyRadius, includeAll, modsOnly, woodOnly, stoneOnly, tabKey);
				}
			}, "SCScanOnTabSelect");
		}
		catch (Exception value)
		{
			LogEverywhere(capi ?? _staticCapi, $"Error in SelectTab_Postfix: {value}", toChat: false, "SelectTab_Postfix");
		}
		finally
		{
			stopwatch.Stop();
			ICoreClientAPI val2 = capi ?? _staticCapi;
			if (val2 != null)
			{
				LogEverywhere(val2, $"SelectTab_Postfix completed in {stopwatch.ElapsedMilliseconds}ms", toChat: false, "SelectTab_Postfix");
			}
		}
	}

	private static void SetUpdatingText(ICoreClientAPI capi, bool show)
	{
		try
		{
			ICoreClientAPI obj = capi;
			if (obj == null)
			{
				return;
			}
			IClientEventAPI obj2 = obj.Event;
			if (obj2 == null)
			{
				return;
			}
			((IEventAPI)obj2).RegisterCallback((Action<float>)delegate
			{
				//IL_0144: Unknown result type (might be due to invalid IL or missing references)
				//IL_014b: Expected O, but got Unknown
				bool flag = false;
				try
				{
					if (!show)
					{
						ICoreClientAPI obj4 = capi;
						if (obj4 != null && obj4.IsGamePaused)
						{
							HandbookPauseGuard.Acquire(capi);
							flag = true;
						}
					}
					Type type = AccessTools.TypeByName("Vintagestory.GameContent.ModSystemSurvivalHandbook");
					object obj5 = ((type != null) ? GetModSystemByType(capi, type) : null);
					object obj6 = ((!(type != null)) ? null : AccessTools.Field(type, "dialog")?.GetValue(obj5));
					GuiComposer val = (GuiComposer)((obj6 != null) ? /*isinst with value type is only supported in some contexts*/: null);
					if (val != null)
					{
						GuiElementDynamicText dynamicText = GuiElementDynamicTextHelper.GetDynamicText(val, "scUpdating");
						bool flag2 = false;
						if (show)
						{
							if (dynamicText == null)
							{
								GuiElementTextInput textInput = GuiComposerHelpers.GetTextInput(val, "searchField");
								if (textInput == null)
								{
									return;
								}
								ElementBounds bounds = ((GuiElement)textInput).Bounds;
								ElementBounds val2 = ElementBounds.Fixed(0.0, bounds.fixedY, 120.0, bounds.fixedHeight);
								val2.ParentBounds = bounds.ParentBounds;
								val2.FixedRightOf(bounds, 10.0);
								dynamicText = new GuiElementDynamicText(capi, "Updating...", CairoFont.WhiteSmallishText(), val2);
								val.AddInteractiveElement((GuiElement)(object)dynamicText, "scUpdating");
								flag2 = true;
							}
							else
							{
								dynamicText.SetNewText("Updating...", false, false, false);
							}
						}
						else if (dynamicText != null)
						{
							dynamicText.SetNewText("", false, false, false);
						}
						if (flag2)
						{
							val.ReCompose();
						}
					}
				}
				catch
				{
				}
				finally
				{
					if (flag)
					{
						HandbookPauseGuard.Release(capi);
					}
				}
			}, 1, true);
		}
		catch
		{
		}
	}

	private static bool TryUpdateActiveTabFromCache(ICoreClientAPI capi, string tabKey, bool requireReadyFlag = true)
	{
		if (!TryAcquireTabUpdateTicket(tabKey, requireReadyFlag))
		{
			return false;
		}
		lock (CacheLock)
		{
			CachedPageCodes = GetTabCacheSnapshot(tabKey);
		}
		LastDialogPageCount = -1;
		TryRefreshOpenDialog(capi);
		SetUpdatingText(capi, show: false);
		return true;
	}

	private static bool IsWoodRecipe(object recipeOrOutput)
	{
		if (recipeOrOutput == null)
		{
			return false;
		}
		Type type = recipeOrOutput.GetType();
		if (CheckOne(TryGetMember(type, recipeOrOutput, "Output")))
		{
			return true;
		}
		if (TryGetMember(type, recipeOrOutput, "Outputs") is IEnumerable enumerable)
		{
			foreach (object item in enumerable)
			{
				if (CheckOne(item))
				{
					return true;
				}
			}
		}
		return CheckOne(recipeOrOutput);
		static bool CheckOne(object o)
		{
			if (o == null)
			{
				return false;
			}
			string text = null;
			string text2 = null;
			ItemStack val = (ItemStack)((o is ItemStack) ? o : null);
			if (val != null)
			{
				text = ((object)((RegistryObject)(val.Collectible?)).Code)?.ToString();
				ITreeAttribute attributes = val.Attributes;
				text2 = ((attributes == null) ? null : ((IAttribute)attributes).ToJsonToken()?.ToString());
			}
			else
			{
				Type type2 = o.GetType();
				object obj = TryGetMember(type2, o, "Code");
				text = ((obj is AssetLocation) ? obj : null)?.ToString() ?? (TryGetMember(type2, o, "code") as string);
				object obj2 = TryGetMember(type2, o, "Attributes");
				if (obj2 == null)
				{
					object obj3 = TryGetMember(type2, o, "ResolvedItemstack");
					ItemStack val2 = (ItemStack)((obj3 is ItemStack) ? obj3 : null);
					if (val2 != null)
					{
						if (text == null)
						{
							text = ((object)((RegistryObject)(val2.Collectible?)).Code)?.ToString();
						}
						obj2 = val2.Attributes;
					}
				}
				IAttribute val3 = (IAttribute)((obj2 is IAttribute) ? obj2 : null);
				text2 = ((val3 == null) ? obj2?.ToString() : val3.ToJsonToken());
			}
			if (!string.IsNullOrEmpty(text))
			{
				if (text.IndexOf("{wood}", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return true;
				}
				if (ContainsWoodMatInCode(text))
				{
					return true;
				}
			}
			if (!string.IsNullOrEmpty(text2))
			{
				if (text2.IndexOf("{wood}", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return true;
				}
				if (ContainsWoodMatInAttributes(text2))
				{
					return true;
				}
			}
			return false;
		}
	}

	public static void AfterPagesLoaded_Postfix(object __instance)
	{
		try
		{
			object? obj = AccessTools.Field(__instance.GetType(), "capi")?.GetValue(__instance);
			object? obj2 = ((obj is ICoreClientAPI) ? obj : null);
			string a = AccessTools.Field(__instance.GetType(), "currentCatgoryCode")?.GetValue(__instance) as string;
			if (obj2 == null || (!string.Equals(a, "craftableall", StringComparison.Ordinal) && !string.Equals(a, "craftable", StringComparison.Ordinal) && !string.Equals(a, "craftablemods", StringComparison.Ordinal) && !string.Equals(a, "craftablewoodtypes", StringComparison.Ordinal) && !string.Equals(a, "craftablestonetypes", StringComparison.Ordinal)))
			{
				return;
			}
			FieldInfo fieldInfo = AccessTools.Field(__instance.GetType(), "allHandbookPages");
			FieldInfo fieldInfo2 = AccessTools.Field(__instance.GetType(), "pageNumberByPageCode");
			IList list = fieldInfo?.GetValue(__instance) as IList;
			IDictionary dictionary = fieldInfo2?.GetValue(__instance) as IDictionary;
			if (list != null && dictionary != null)
			{
				List<object> list2 = list.Cast<object>().OrderBy<object, string>((object p) => GetPageTitle(p), StringComparer.OrdinalIgnoreCase).ToList();
				list.Clear();
				dictionary.Clear();
				for (int num = 0; num < list2.Count; num++)
				{
					object obj3 = list2[num];
					list.Add(obj3);
					if (AccessTools.Property(obj3.GetType(), "PageCode")?.GetValue(obj3) is string key)
					{
						dictionary[key] = num;
					}
					AccessTools.Field(obj3.GetType(), "PageNumber")?.SetValue(obj3, num);
				}
			}
			AccessTools.Method(__instance.GetType(), "FilterItems", (Type[])null, (Type[])null)?.Invoke(__instance, null);
		}
		catch
		{
		}
	}

	private static string GetPageTitle(object page)
	{
		string text = AccessTools.Field(page.GetType(), "TextCacheTitle")?.GetValue(page) as string;
		if (!string.IsNullOrEmpty(text))
		{
			return text;
		}
		return (AccessTools.Property(page.GetType(), "PageCode")?.GetValue(page) as string) ?? string.Empty;
	}

	private static void AddRecipeButton_Postfix(List<RichTextComponentBase> components)
	{
		if (_staticCapi == null || components == null || !Config.EnableFetchButton)
		{
			return;
		}
		for (int i = 0; i < components.Count; i++)
		{
			RichTextComponentBase obj = components[i];
			SlideshowGridRecipeTextComponent val = (SlideshowGridRecipeTextComponent)(object)((obj is SlideshowGridRecipeTextComponent) ? obj : null);
			if (val != null)
			{
				components.Insert(i + 1, (RichTextComponentBase)(object)new RecipeGridButton(_staticCapi, val));
				i++;
			}
		}
	}

	public static bool FilterItems_Prefix(object __instance)
	{
		try
		{
			string a = (string)AccessTools.Field(__instance.GetType(), "currentCatgoryCode").GetValue(__instance);
			if (!string.Equals(a, "craftableall", StringComparison.Ordinal) && !string.Equals(a, "craftable", StringComparison.Ordinal) && !string.Equals(a, "craftablemods", StringComparison.Ordinal) && !string.Equals(a, "craftablewoodtypes", StringComparison.Ordinal) && !string.Equals(a, "craftablestonetypes", StringComparison.Ordinal))
			{
				return true;
			}
			FieldInfo fieldInfo = AccessTools.Field(__instance.GetType(), "capi");
			FieldInfo fieldInfo2 = AccessTools.Field(__instance.GetType(), "shownHandbookPages");
			FieldInfo fieldInfo3 = AccessTools.Field(__instance.GetType(), "overviewGui");
			AccessTools.Field(__instance.GetType(), "listHeight");
			FieldInfo fieldInfo4 = AccessTools.Field(__instance.GetType(), "currentSearchText");
			FieldInfo fieldInfo5 = AccessTools.Field(__instance.GetType(), "loadingPagesAsync");
			fieldInfo?.GetValue(__instance);
			IList list = fieldInfo2?.GetValue(__instance) as IList;
			object? obj = fieldInfo3?.GetValue(__instance);
			GuiComposer val = (GuiComposer)((obj is GuiComposer) ? obj : null);
			if (val == null)
			{
				return true;
			}
			string text = (string)fieldInfo4?.GetValue(__instance);
			if (fieldInfo5 != null)
			{
				_ = (bool)fieldInfo5.GetValue(__instance);
			}
			else
				_ = 0;
			if (list == null || val == null)
			{
				return true;
			}
			PropertyInfo propertyInfo = AccessTools.Property(__instance.GetType(), "SingleComposer") ?? AccessTools.Property(__instance.GetType().BaseType, "SingleComposer");
			try
			{
				propertyInfo?.SetValue(__instance, val);
			}
			catch
			{
			}
			Dictionary<string, int> dictionary = AccessTools.Field(__instance.GetType(), "pageNumberByPageCode")?.GetValue(__instance) as Dictionary<string, int>;
			IList list2 = AccessTools.Field(__instance.GetType(), "allHandbookPages")?.GetValue(__instance) as IList;
			if (dictionary == null || list2 == null)
			{
				return true;
			}
			List<string> list3;
			lock (CacheLock)
			{
				list3 = CachedPageCodes.ToList();
			}
			List<object> list4 = new List<object>();
			int num = 0;
			foreach (string item in list3)
			{
				if (dictionary.TryGetValue(item, out var value) && value >= 0 && value < list2.Count)
				{
					object obj3 = list2[value];
					if (obj3 != null)
					{
						list4.Add(obj3);
					}
				}
				else
				{
					num++;
				}
			}
			List<object> list6;
			if (!string.IsNullOrWhiteSpace(text))
			{
				string text2 = text.ToLowerInvariant().Trim();
				List<(object, float)> list5 = new List<(object, float)>();
				foreach (object item2 in list4)
				{
					MethodInfo method = item2.GetType().GetMethod("GetTextMatchWeight");
					float num2 = ((method == null) ? 0f : ((float)method.Invoke(item2, new object[1] { text2 })));
					if (num2 > 0f)
					{
						list5.Add((item2, num2));
					}
				}
				list6 = (from x in list5.OrderByDescending<(object, float), float>(((object Page, float W) x) => x.W).ThenBy<(object, float), string>(((object Page, float W) x) => GetPageTitle(x.Page), StringComparer.OrdinalIgnoreCase)
					select x.Page).ToList();
			}
			else
			{
				list6 = list4.OrderBy<object, string>((object p) => GetPageTitle(p), StringComparer.OrdinalIgnoreCase).ToList();
			}
			foreach (object item3 in list6)
			{
				PropertyInfo property = item3.GetType().GetProperty("Visible", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				try
				{
					property?.SetValue(item3, true);
				}
				catch
				{
				}
			}
			list.Clear();
			foreach (object item4 in list6)
			{
				list.Add(item4);
			}
			double num3 = 500.0;
			GuiElementFlatList flatList = GuiComposerHelpers.GetFlatList(val, "stacklist");
			if (flatList != null)
			{
				flatList.CalcTotalHeight();
				GuiElementScrollbar scrollbar = GuiComposerHelpers.GetScrollbar(val, "scrollbar");
				if (scrollbar != null)
				{
					scrollbar.SetHeights((float)num3, (float)flatList.insideBounds.fixedHeight);
					if (!string.IsNullOrWhiteSpace(text))
					{
						scrollbar.CurrentYPosition = 0f;
						flatList.insideBounds.fixedY = 3.0;
						flatList.insideBounds.CalcWorldBounds();
					}
				}
			}
			return false;
		}
		catch
		{
			return true;
		}
	}

	private static void TryRefreshOpenDialog(ICoreClientAPI capi)
	{
		try
		{
			Type type = AccessTools.TypeByName("Vintagestory.GameContent.ModSystemSurvivalHandbook");
			object obj = ((type != null) ? GetModSystemByType(capi, type) : null);
			if (obj == null)
			{
				LastDialogPageCount = 0;
				return;
			}
			object obj2 = AccessTools.Field(type, "dialog")?.GetValue(obj);
			if (obj2 == null)
			{
				LastDialogPageCount = 0;
				return;
			}
			string a = AccessTools.Field(obj2.GetType(), "currentCatgoryCode")?.GetValue(obj2) as string;
			if (!string.Equals(a, "craftableall", StringComparison.Ordinal) && !string.Equals(a, "craftable", StringComparison.Ordinal) && !string.Equals(a, "craftablemods", StringComparison.Ordinal) && !string.Equals(a, "craftablewoodtypes", StringComparison.Ordinal) && !string.Equals(a, "craftablestonetypes", StringComparison.Ordinal))
			{
				LastDialogPageCount = 0;
				return;
			}
			int count;
			lock (CacheLock)
			{
				count = CachedPageCodes.Count;
			}
			if (count != LastDialogPageCount)
			{
				LastDialogPageCount = count;
				AccessTools.Method(obj2.GetType(), "FilterItems", (Type[])null, (Type[])null)?.Invoke(obj2, null);
			}
		}
		catch
		{
		}
	}

	private static object GetModSystemByType(ICoreClientAPI capi, Type msType)
	{
		IModLoader modLoader = ((ICoreAPI)capi).ModLoader;
		Type type = ((object)modLoader).GetType();
		MethodInfo methodInfo = type.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo m) => m.Name == "GetModSystem" && m.IsGenericMethodDefinition);
		if (methodInfo != null)
		{
			try
			{
				MethodInfo methodInfo2 = methodInfo.MakeGenericMethod(msType);
				ParameterInfo[] parameters = methodInfo2.GetParameters();
				return (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool)) ? methodInfo2.Invoke(modLoader, new object[1] { true }) : methodInfo2.Invoke(modLoader, null);
			}
			catch
			{
			}
		}
		MethodInfo methodInfo3 = type.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo m) => m.Name == "GetModSystem" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Type));
		if (methodInfo3 != null)
		{
			try
			{
				return methodInfo3.Invoke(modLoader, new object[1] { msType });
			}
			catch
			{
			}
		}
		return null;
	}

	private static Dictionary<StackKey, string> BuildPageCodeMapFromAllStacks(ICoreClientAPI capi)
	{
		Dictionary<StackKey, string> dictionary = new Dictionary<StackKey, string>();
		try
		{
			Type type = AccessTools.TypeByName("Vintagestory.GameContent.ModSystemSurvivalHandbook");
			object obj = ((type != null) ? GetModSystemByType(capi, type) : null);
			if (obj == null)
			{
				return dictionary;
			}
			if (!(AccessTools.Field(type, "allstacks")?.GetValue(obj) is ItemStack[] array) || array.Length == 0)
			{
				return dictionary;
			}
			MethodInfo methodInfo = AccessTools.TypeByName("Vintagestory.GameContent.GuiHandbookItemStackPage")?.GetMethod("PageCodeForStack", BindingFlags.Static | BindingFlags.Public);
			if (methodInfo == null)
			{
				return dictionary;
			}
			ItemStack[] array2 = array;
			foreach (ItemStack val in array2)
			{
				if (((val == null) ? null : ((RegistryObject)(val.Collectible?)).Code) == (AssetLocation)null)
				{
					continue;
				}
				StackKey key = KeyFor(val);
				string value = methodInfo.Invoke(null, new object[1] { val }) as string;
				if (!string.IsNullOrEmpty(value))
				{
					if (!dictionary.ContainsKey(key))
					{
						dictionary[key] = value;
					}
					StackKey key2 = new StackKey(key.Code, "", "");
					if (!dictionary.ContainsKey(key2))
					{
						dictionary[key2] = value;
					}
				}
			}
		}
		catch
		{
		}
		return dictionary;
	}

	private static Dictionary<StackKey, string> GetCachedPageCodeMap(ICoreClientAPI capi)
	{
		try
		{
			Type type = AccessTools.TypeByName("Vintagestory.GameContent.ModSystemSurvivalHandbook");
			object obj = ((type != null) ? GetModSystemByType(capi, type) : null);
			ItemStack[] array = AccessTools.Field(type, "allstacks")?.GetValue(obj) as ItemStack[];
			lock (PageCodeMapLock)
			{
				if (array != AllStacksPageCodeMapSource || AllStacksPageCodeMap.Count == 0)
				{
					AllStacksPageCodeMap = BuildPageCodeMapFromAllStacks(capi);
					AllStacksPageCodeMapSource = array;
				}
				return AllStacksPageCodeMap;
			}
		}
		catch
		{
			return AllStacksPageCodeMap;
		}
	}

	private static void InvalidatePageCodeMapCache()
	{
		lock (PageCodeMapLock)
		{
			AllStacksPageCodeMap.Clear();
			AllStacksPageCodeMapSource = null;
		}
	}

	private static void ClearAllCaches()
	{
		Interlocked.Increment(ref recipeIndexBuildToken);
		lock (ScanQueueLock)
		{
			QueuedScanRequest = null;
			ScanQueueCheckScheduled = false;
		}
		lock (PendingScanLock)
		{
			PendingScanVariantKey = null;
			PendingScanTabKey = null;
		}
		lock (InflightMapLock)
		{
			InflightById.Clear();
		}
		lock (TabUiStateLock)
		{
			TabReadyToUpdateUi.Clear();
		}
		lock (DnaLock)
		{
			TabPoolDNA.Clear();
		}
		lock (WildTokenCountsMemo)
		{
			WildTokenCountsMemo.Clear();
		}
		lock (recipeIndexByVariant)
		{
			recipeIndexByVariant.Clear();
			recipeIndexBuildStarted = false;
			recipeIndexBuilt = false;
		}
		recipeIndexBuildTask = null;
		lock (CacheLock)
		{
			CachedPageCodes = new List<string>();
			AllTabCache = new List<string>();
			CraftableTabCache = new List<string>();
			ModTabCache = new List<string>();
			StoneTypeTabCache = new List<string>();
			WoodTypeTabCache = new List<string>();
			s_EmptyPages = null;
		}
		recipeGroupNeeds = new Dictionary<GridRecipeShim, Dictionary<string, int>>();
		outputsIndex = new Dictionary<StackKey, List<GridRecipeShim>>();
		codeToRecipeGroups = new Dictionary<string, List<(GridRecipeShim, string)>>();
		codeToGkeys = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
		wildMatchCache = new Dictionary<string, List<(GridRecipeShim, string)>>(StringComparer.Ordinal);
		wildcardGroups = new List<WildGroup>();
		recipesFetched = 0;
		recipesUsable = 0;
		ScanSeq = 0;
		_pendingScanId = 0;
		LastDialogPageCount = 0;
		NearbyRadius = ConfiguredSearchRadius;
		recipeIndexForMods = false;
		recipeIndexForStoneOnly = false;
		recipeIndexForWoodOnly = false;
		CraftableAllTabActive = false;
		CraftableModsTabActive = false;
		CraftableStoneTabActive = false;
		CraftableTabActive = false;
		CraftableWoodTabActive = false;
		ScanInProgress = false;
		recipeIndexBuildProgress = 0;
		recipeIndexBuildTotal = 0;
		InvalidatePageCodeMapCache();
		_staticCapi = null;
	}

	private static ItemStack MakeStackFromCode(ICoreClientAPI capi, string code)
	{
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Expected O, but got Unknown
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Expected O, but got Unknown
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Expected O, but got Unknown
		if (string.IsNullOrEmpty(code))
		{
			return null;
		}
		AssetLocation val = new AssetLocation(code);
		Item item = ((IWorldAccessor)capi.World).GetItem(val);
		if (item != null)
		{
			return new ItemStack(item, 1);
		}
		Block block = ((IWorldAccessor)capi.World).GetBlock(val);
		if (block != null)
		{
			return new ItemStack(block, 1);
		}
		return null;
	}

	private static bool CodeStartsWithBowl(AssetLocation code)
	{
		if (code == (AssetLocation)null)
		{
			return false;
		}
		string text = code.Path;
		if (string.IsNullOrEmpty(text))
		{
			string text2 = ((object)code).ToString();
			if (!string.IsNullOrEmpty(text2))
			{
				int num = text2.IndexOf(':');
				text = ((num >= 0) ? text2.Substring(num + 1) : text2);
			}
		}
		if (!string.IsNullOrEmpty(text))
		{
			return text.StartsWith("bowl", StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	private static bool StackRepresentsBowl(ItemStack stack)
	{
		if (((stack == null) ? null : ((RegistryObject)(stack.Collectible?)).Code) == (AssetLocation)null)
		{
			return false;
		}
		return CodeStartsWithBowl(((RegistryObject)stack.Collectible).Code);
	}

	private static bool ShouldSkipGridRecipe(GridRecipeShim shim)
	{
		if (shim == null)
		{
			return false;
		}
		if (ShouldSkipRawGridRecipe(shim.Raw))
		{
			return true;
		}
		if (shim.Outputs != null)
		{
			foreach (ItemStack output in shim.Outputs)
			{
				if (StackRepresentsBowl(output))
				{
					return true;
				}
			}
		}
		if (shim.Ingredients != null)
		{
			foreach (GridIngredientShim ingredient in shim.Ingredients)
			{
				if (ingredient == null)
				{
					continue;
				}
				if (ingredient.PatternCode != (AssetLocation)null && CodeStartsWithBowl(ingredient.PatternCode))
				{
					return true;
				}
				if (ingredient.Options == null)
				{
					continue;
				}
				foreach (ItemStack option in ingredient.Options)
				{
					if (StackRepresentsBowl(option))
					{
						return true;
					}
				}
			}
		}
		return false;
	}

	private static bool ShouldSkipRawGridRecipe(object raw)
	{
		if (raw == null)
		{
			return false;
		}
		Type type = raw.GetType();
		if (type == null || !type.Name.Contains("GridRecipe", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		object obj = TryGetMember(type, raw, "Name");
		return ShouldSkipRecipeByName((AssetLocation)((obj is AssetLocation) ? obj : null));
	}

	private static bool ShouldSkipRecipeByName(AssetLocation name)
	{
		if (name == (AssetLocation)null)
		{
			return false;
		}
		if (!string.Equals(name.Domain, "game", StringComparison.Ordinal))
		{
			return false;
		}
		string text = name.Path;
		if (string.IsNullOrEmpty(text))
		{
			text = name.ToShortString();
		}
		if (string.IsNullOrEmpty(text))
		{
			return false;
		}
		return string.Equals(text, "recipes/grid/nuggets", StringComparison.Ordinal);
	}

	private static List<GridRecipeShim> GetAllGridRecipes(ICoreClientAPI capi, out int fetched, out int usable, bool? modsOnly)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		List<GridRecipeShim> list = new List<GridRecipeShim>();
		fetched = 0;
		usable = 0;
		IClientWorldAccessor world = capi.World;
		IEnumerable<object> enumerable = Enumerable.Empty<object>();
		PropertyInfo? property = ((object)world).GetType().GetProperty("GridRecipes", BindingFlags.Instance | BindingFlags.Public);
		FieldInfo field = ((object)world).GetType().GetField("GridRecipes", BindingFlags.Instance | BindingFlags.Public);
		bool value = property != null || field != null;
		object obj = property?.GetValue(world) ?? field?.GetValue(world);
		bool flag = obj is IEnumerable;
		enumerable = ((!flag) ? FetchGridRecipesMulti(capi) : ((IEnumerable)obj).Cast<object>());
		foreach (object item in enumerable)
		{
			if (item != null && !ShouldSkipRawGridRecipe(item))
			{
				fetched++;
				GridRecipeShim gridRecipeShim = TryBuildGridShim(item, capi);
				if (gridRecipeShim != null && gridRecipeShim.Outputs.Count > 0 && !ShouldSkipGridRecipe(gridRecipeShim) && (!modsOnly.HasValue || modsOnly.Value == gridRecipeShim.IsMod))
				{
					usable++;
					list.Add(gridRecipeShim);
				}
			}
		}
		stopwatch.Stop();
		LogEverywhere(capi, $"Fetched {fetched} grid recipes, {usable} usable, gridMemberFound={value}, usedWorldList={flag}, elapsedMs={stopwatch.ElapsedMilliseconds}", toChat: false, "GetAllGridRecipes");
		return list;
	}

	private static string GetCachePath(ICoreClientAPI capi, bool includeAll, bool modsOnly, bool woodOnly, bool stoneOnly)
	{
		string text;
		try
		{
			MethodInfo method = typeof(ICoreAPI).GetMethod("GetOrCreateDataPath", BindingFlags.Instance | BindingFlags.Public);
			text = ((method != null) ? ((string)method.Invoke(capi, new object[1] { "ShowCraftable" })) : null);
		}
		catch
		{
			text = null;
		}
		if (string.IsNullOrEmpty(text))
		{
			text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShowCraftable");
		}
		Directory.CreateDirectory(text);
		string path = (includeAll ? "recipeindex_all.bin" : (modsOnly ? "recipeindex_mods.bin" : (woodOnly ? "recipeindex_wood.bin" : (stoneOnly ? "recipeindex_stone.bin" : "recipeindex_vanilla.bin"))));
		return Path.Combine(text, path);
	}

	private static int RecommendParallelism(int workItems, int chunkSize = 64, int reserveCores = 2, int minCap = 8, int maxCap = 24, double fraction = 0.65)
	{
		int num = Math.Max(1, Environment.ProcessorCount);
		int num2 = Math.Max(1, num - reserveCores);
		int val = Math.Max(1, (workItems + chunkSize - 1) / chunkSize);
		int val2 = Math.Min(Math.Min(Math.Clamp((int)Math.Round((double)num2 * fraction), minCap, maxCap), num2), val);
		return Math.Max(1, val2);
	}

	private static void StoreRecipeIndex(string variantKey, RecipeIndexData data)
	{
		if (string.IsNullOrEmpty(variantKey) || data == null)
		{
			return;
		}
		lock (recipeIndexByVariant)
		{
			recipeIndexByVariant[variantKey] = data;
		}
	}

	private static bool TryGetRecipeIndex(string variantKey, out RecipeIndexData data)
	{
		data = null;
		if (string.IsNullOrEmpty(variantKey))
		{
			return false;
		}
		lock (recipeIndexByVariant)
		{
			if (recipeIndexByVariant.TryGetValue(variantKey, out var value))
			{
				data = value;
				return data != null;
			}
		}
		return false;
	}

	private static bool ApplyRecipeIndexVariant(string variantKey)
	{
		if (!TryGetRecipeIndex(variantKey, out var data))
		{
			return false;
		}
		codeToRecipeGroups = data.CodeToRecipeGroups ?? new Dictionary<string, List<(GridRecipeShim, string)>>(StringComparer.Ordinal);
		recipeGroupNeeds = data.RecipeGroupNeeds ?? new Dictionary<GridRecipeShim, Dictionary<string, int>>();
		codeToGkeys = data.CodeToGkeys ?? new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
		wildcardGroups = data.WildcardGroups ?? new List<WildGroup>();
		wildMatchCache = data.WildMatchCache ?? new Dictionary<string, List<(GridRecipeShim, string)>>(StringComparer.Ordinal);
		outputsIndex = data.OutputsIndex ?? new Dictionary<StackKey, List<GridRecipeShim>>();
		recipesFetched = data.RecipesFetched;
		recipesUsable = data.RecipesUsable;
		recipeIndexForMods = string.Equals(variantKey, "mods", StringComparison.Ordinal);
		recipeIndexForWoodOnly = string.Equals(variantKey, "wood", StringComparison.Ordinal);
		recipeIndexForStoneOnly = string.Equals(variantKey, "stone", StringComparison.Ordinal);
		return true;
	}

	private static bool EnsureRecipeIndexVariantReady(ICoreClientAPI capi, string variantKey, int timeoutMs = 15000)
	{
		if (string.IsNullOrEmpty(variantKey))
		{
			return false;
		}
		if (TryGetRecipeIndex(variantKey, out var data))
		{
			return ApplyRecipeIndexVariant(variantKey);
		}
		StartRecipeIndexBuild(capi);
		Stopwatch stopwatch = Stopwatch.StartNew();
		while (stopwatch.ElapsedMilliseconds < timeoutMs)
		{
			Thread.Sleep(50);
			if (TryGetRecipeIndex(variantKey, out data))
			{
				return ApplyRecipeIndexVariant(variantKey);
			}
		}
		LogEverywhere(capi ?? _staticCapi, $"Recipe index variant {variantKey} not ready after waiting {timeoutMs}ms", toChat: false, "EnsureRecipeIndexVariantReady");
		return false;
	}

	private static RecipeIndexData LoadRecipeIndex(ICoreClientAPI capi, bool includeAll, bool modsOnly, bool woodOnly, bool stoneOnly)
	{
		//IL_0179: Unknown result type (might be due to invalid IL or missing references)
		//IL_0180: Expected O, but got Unknown
		//IL_0306: Unknown result type (might be due to invalid IL or missing references)
		//IL_030d: Expected O, but got Unknown
		//IL_012a: Unknown result type (might be due to invalid IL or missing references)
		//IL_026b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0298: Unknown result type (might be due to invalid IL or missing references)
		//IL_029d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0144: Unknown result type (might be due to invalid IL or missing references)
		//IL_0149: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			string cachePath = GetCachePath(capi, includeAll, modsOnly, woodOnly, stoneOnly);
			if (!File.Exists(cachePath))
			{
				return null;
			}
			RecipeIndexCache recipeIndexCache;
			using (FileStream fileStream = File.OpenRead(cachePath))
			{
				recipeIndexCache = Serializer.Deserialize<RecipeIndexCache>((Stream)fileStream);
			}
			RecipeIndexData recipeIndexData = new RecipeIndexData();
			List<GridRecipeShim> list = new List<GridRecipeShim>();
			int[] array = ((recipeIndexCache.Recipes != null) ? new int[recipeIndexCache.Recipes.Count] : Array.Empty<int>());
			for (int i = 0; i < array.Length; i++)
			{
				array[i] = -1;
			}
			if (recipeIndexCache.Recipes != null)
			{
				for (int j = 0; j < recipeIndexCache.Recipes.Count; j++)
				{
					CachedRecipe cachedRecipe = recipeIndexCache.Recipes[j];
					if (cachedRecipe == null)
					{
						continue;
					}
					GridRecipeShim gridRecipeShim = new GridRecipeShim();
					List<WildGroup> list2 = new List<WildGroup>();
					foreach (CachedIngredient item in (cachedRecipe.Ingredients != null) ? cachedRecipe.Ingredients : new List<CachedIngredient>())
					{
						if (item == null)
						{
							continue;
						}
						GridIngredientShim gridIngredientShim = new GridIngredientShim
						{
							IsTool = item.IsTool,
							IsWild = item.IsWild,
							QuantityRequired = item.QuantityRequired,
							PatternCode = ((item.PatternCode == null) ? ((AssetLocation)null) : new AssetLocation(item.PatternCode)),
							Allowed = item.Allowed,
							Type = item.Type
						};
						if (item.Options != null)
						{
							foreach (byte[] option in item.Options)
							{
								if (option != null)
								{
									try
									{
										ItemStack val = new ItemStack(option);
										val.ResolveBlockOrItem((IWorldAccessor)(object)capi.World);
										gridIngredientShim.Options.Add(val);
									}
									catch
									{
									}
								}
							}
						}
						gridRecipeShim.Ingredients.Add(gridIngredientShim);
						if (gridIngredientShim.IsWild && gridIngredientShim.PatternCode != (AssetLocation)null)
						{
							string groupKey = $"wild:{gridIngredientShim.PatternCode}|{string.Join(",", (gridIngredientShim.Allowed ?? Array.Empty<string>()).OrderBy((string x) => x))}|T:{gridIngredientShim.Type}";
							list2.Add(new WildGroup
							{
								Recipe = gridRecipeShim,
								GroupKey = groupKey,
								Type = gridIngredientShim.Type,
								Pattern = gridIngredientShim.PatternCode,
								Allowed = gridIngredientShim.Allowed
							});
						}
					}
					if (cachedRecipe.Outputs != null)
					{
						foreach (byte[] output in cachedRecipe.Outputs)
						{
							if (output != null)
							{
								try
								{
									ItemStack val2 = new ItemStack(output);
									val2.ResolveBlockOrItem((IWorldAccessor)(object)capi.World);
									gridRecipeShim.Outputs.Add(val2);
								}
								catch
								{
								}
							}
						}
					}
					if (gridRecipeShim.Outputs == null || gridRecipeShim.Outputs.Count == 0 || ShouldSkipGridRecipe(gridRecipeShim))
					{
						continue;
					}
					array[j] = list.Count;
					list.Add(gridRecipeShim);
					foreach (WildGroup item2 in list2)
					{
						recipeIndexData.WildcardGroups.Add(item2);
					}
					recipeIndexData.RecipeGroupNeeds[gridRecipeShim] = cachedRecipe.Needs ?? new Dictionary<string, int>(StringComparer.Ordinal);
				}
			}
			foreach (KeyValuePair<string, List<CodeRecipeRef>> item3 in recipeIndexCache.CodeToRecipes ?? new Dictionary<string, List<CodeRecipeRef>>())
			{
				List<(GridRecipeShim, string)> list3 = new List<(GridRecipeShim, string)>();
				if (item3.Value != null)
				{
					foreach (CodeRecipeRef item4 in item3.Value)
					{
						if (item4 != null && item4.Recipe >= 0 && item4.Recipe < array.Length)
						{
							int num = array[item4.Recipe];
							if (num >= 0 && num < list.Count)
							{
								list3.Add((list[num], item4.GroupKey));
							}
						}
					}
				}
				recipeIndexData.CodeToRecipeGroups[item3.Key] = list3;
			}
			foreach (KeyValuePair<string, List<(GridRecipeShim, string)>> codeToRecipeGroup in recipeIndexData.CodeToRecipeGroups)
			{
				if (!recipeIndexData.CodeToGkeys.TryGetValue(codeToRecipeGroup.Key, out var value))
				{
					value = (recipeIndexData.CodeToGkeys[codeToRecipeGroup.Key] = new HashSet<string>(StringComparer.Ordinal));
				}
				foreach (var item5 in codeToRecipeGroup.Value)
				{
					if (!string.IsNullOrEmpty(item5.Item2))
					{
						value.Add(item5.Item2);
					}
				}
			}
			recipeIndexData.OutputsIndex = BuildOutputsIndexFrom(list);
			recipeIndexData.RecipesFetched = list.Count;
			recipeIndexData.RecipesUsable = list.Count;
			return recipeIndexData;
		}
		catch
		{
			return null;
		}
	}

	private static void StartRecipeIndexBuild(ICoreClientAPI capi)
	{
		if (capi == null)
		{
			return;
		}
		bool flag = false;
		int num = 0;
		lock (recipeIndexByVariant)
		{
			bool flag2 = RecipeIndexVariants.All(((string VariantKey, bool IncludeAll, bool ModsOnly, bool WoodOnly, bool StoneOnly) v) => recipeIndexByVariant.ContainsKey(v.VariantKey));
			if ((recipeIndexBuilt && flag2) || recipeIndexBuildStarted)
			{
				return;
			}
			recipeIndexBuildStarted = true;
			recipeIndexBuilt = false;
			num = Interlocked.Increment(ref recipeIndexBuildToken);
			flag = true;
		}
		if (!flag)
		{
			return;
		}
		int localToken = num;
		Stopwatch stopwatch = Stopwatch.StartNew();
		try
		{
			recipeIndexBuildTask = Task.Run(delegate
			{
				try
				{
					(string, bool, bool, bool, bool)[] recipeIndexVariants = RecipeIndexVariants;
					for (int i = 0; i < recipeIndexVariants.Length; i++)
					{
						(string, bool, bool, bool, bool) tuple = recipeIndexVariants[i];
						if (localToken != Volatile.Read(in recipeIndexBuildToken))
						{
							return;
						}
						string item = tuple.Item1;
						RecipeIndexData recipeIndexData = LoadRecipeIndex(capi, tuple.Item2, tuple.Item3, tuple.Item4, tuple.Item5);
						bool flag3 = false;
						if (recipeIndexData == null)
						{
							recipeIndexData = BuildRecipeIndex(capi, tuple.Item2, tuple.Item3, tuple.Item4, tuple.Item5);
							flag3 = true;
						}
						if (localToken != Volatile.Read(in recipeIndexBuildToken))
						{
							return;
						}
						StoreRecipeIndex(item, recipeIndexData);
						if (flag3)
						{
							LogEverywhere(capi, "Recipe index built for variant=" + item + "; keeping DNA & cache. Next scan will decide.", toChat: false, "StartRecipeIndexBuild");
						}
						if (localToken != Volatile.Read(in recipeIndexBuildToken))
						{
							return;
						}
						if (string.Equals(GetActiveTabKey(), TabKeyFromVariant(item), StringComparison.Ordinal))
						{
							ApplyRecipeIndexVariant(item);
						}
					}
					if (localToken == Volatile.Read(in recipeIndexBuildToken))
					{
						recipeIndexBuilt = true;
						string variantKey = VariantKeyFromTabKey(GetActiveTabKey());
						if (localToken == Volatile.Read(in recipeIndexBuildToken))
						{
							ApplyRecipeIndexVariant(variantKey);
							if (localToken == Volatile.Read(in recipeIndexBuildToken))
							{
								GetCachedPageCodeMap(capi);
							}
						}
					}
				}
				catch (Exception value)
				{
					LogEverywhere(capi, $"Failed to build recipe index: {value}", toChat: false, "StartRecipeIndexBuild");
				}
				finally
				{
					if (localToken != Volatile.Read(in recipeIndexBuildToken))
					{
						lock (recipeIndexByVariant)
						{
							recipeIndexBuildStarted = false;
							recipeIndexBuilt = false;
						}
					}
				}
			});
		}
		finally
		{
			stopwatch.Stop();
			LogEverywhere(capi, $"StartRecipeIndexBuild completed in {stopwatch.ElapsedMilliseconds}ms", toChat: false, "StartRecipeIndexBuild");
		}
	}

	private static RecipeIndexData BuildRecipeIndex(ICoreClientAPI capi, bool includeAll, bool modsOnly, bool woodOnly, bool stoneOnly)
	{
		//IL_0211: Unknown result type (might be due to invalid IL or missing references)
		//IL_0270: Unknown result type (might be due to invalid IL or missing references)
		//IL_0275: Unknown result type (might be due to invalid IL or missing references)
		Stopwatch stopwatch = Stopwatch.StartNew();
		RecipeIndexData recipeIndexData = new RecipeIndexData();
		int fetched;
		int usable;
		List<GridRecipeShim> list = GetAllGridRecipes(capi, out fetched, out usable, includeAll ? ((bool?)null) : new bool?(modsOnly));
		if (!includeAll)
		{
			if (woodOnly)
			{
				list = list.Where((GridRecipeShim gridRecipeShim) => !gridRecipeShim.IsMod && IsWoodRecipe(gridRecipeShim.Raw)).ToList();
			}
			else if (stoneOnly)
			{
				list = list.Where((GridRecipeShim gridRecipeShim) => !gridRecipeShim.IsMod && IsStoneRecipe(gridRecipeShim.Raw)).ToList();
			}
			else if (!modsOnly)
			{
				list = list.Where((GridRecipeShim gridRecipeShim) => !gridRecipeShim.IsMod && !IsWoodRecipe(gridRecipeShim.Raw) && !IsStoneRecipe(gridRecipeShim.Raw)).ToList();
			}
		}
		recipeIndexData.RecipesFetched = fetched;
		recipeIndexData.RecipesUsable = usable;
		recipeIndexBuildTotal = list.Count;
		recipeIndexBuildProgress = 0;
		recipeIndexData.OutputsIndex = BuildOutputsIndexFrom(list);
		Dictionary<string, List<string>> wildCache = new Dictionary<string, List<string>>(StringComparer.Ordinal);
		foreach (GridRecipeShim r in list)
		{
			Dictionary<string, int> dictionary = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (GridIngredientShim ingredient in r.Ingredients)
			{
				if (ingredient == null)
				{
					continue;
				}
				string groupKey;
				IEnumerable<string> enumerable;
				if (ingredient.IsWild)
				{
					string[] source = ingredient.Allowed ?? Array.Empty<string>();
					groupKey = $"wild:{ingredient.PatternCode}|{string.Join(",", source.OrderBy((string x) => x))}|T:{ingredient.Type}";
					enumerable = GetWildMatches(ingredient);
					if (ingredient.PatternCode != (AssetLocation)null)
					{
						recipeIndexData.WildcardGroups.Add(new WildGroup
						{
							Recipe = r,
							GroupKey = groupKey,
							Type = ingredient.Type,
							Pattern = ingredient.PatternCode,
							Allowed = ingredient.Allowed
						});
					}
				}
				else
				{
					IEnumerable<string> enumerable2 = (from st in ingredient.Options
						select (st == null) ? null : ((object)((RegistryObject)(st.Collectible?)).Code)?.ToString() into s
						where !string.IsNullOrEmpty(s)
						select s).Distinct<string>(StringComparer.Ordinal);
					groupKey = string.Join("|", enumerable2.OrderBy((string s) => s));
					enumerable = enumerable2;
				}
				int num = Math.Max(1, ingredient.QuantityRequired);
				if (ingredient.IsTool)
				{
					num = 1;
				}
				if (!dictionary.TryGetValue(groupKey, out var value))
				{
					value = 0;
				}
				dictionary[groupKey] = value + num;
				foreach (string item in enumerable)
				{
					if (!string.IsNullOrEmpty(item))
					{
						if (!recipeIndexData.CodeToRecipeGroups.TryGetValue(item, out List<(GridRecipeShim, string)> value2))
						{
							value2 = (recipeIndexData.CodeToRecipeGroups[item] = new List<(GridRecipeShim, string)>());
						}
						if (!value2.Any<(GridRecipeShim, string)>(((GridRecipeShim Recipe, string GroupKey) p) => p.Recipe == r && p.GroupKey == groupKey))
						{
							value2.Add((r, groupKey));
						}
					}
				}
				foreach (string item2 in enumerable)
				{
					if (!string.IsNullOrEmpty(item2))
					{
						if (!recipeIndexData.CodeToGkeys.TryGetValue(item2, out var value3))
						{
							value3 = (recipeIndexData.CodeToGkeys[item2] = new HashSet<string>(StringComparer.Ordinal));
						}
						value3.Add(groupKey);
					}
				}
			}
			recipeIndexData.RecipeGroupNeeds[r] = dictionary;
			recipeIndexBuildProgress++;
		}
		stopwatch.Stop();
		long elapsedMs = stopwatch.ElapsedMilliseconds;
		((IEventAPI)capi.Event).EnqueueMainThreadTask((Action)delegate
		{
			LogEverywhere(capi, $"Recipe index build processed {recipeIndexBuildProgress}/{recipeIndexBuildTotal} recipes in {elapsedMs}ms", toChat: false, "BuildRecipeIndex");
		}, (string)null);
		return recipeIndexData;
		IEnumerable<string> GetWildMatches(GridIngredientShim ing)
		{
			//IL_009b: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c9: Unknown result type (might be due to invalid IL or missing references)
			//IL_00cf: Invalid comparison between Unknown and I4
			//IL_0111: Unknown result type (might be due to invalid IL or missing references)
			//IL_0191: Unknown result type (might be due to invalid IL or missing references)
			//IL_0198: Expected O, but got Unknown
			if (ing == null || ing.PatternCode == (AssetLocation)null)
			{
				return Array.Empty<string>();
			}
			string[] array = ing.Allowed ?? Array.Empty<string>();
			string key = $"wild:{ing.PatternCode}|{string.Join(",", array.OrderBy((string x) => x))}|T:{ing.Type}";
			if (wildCache.TryGetValue(key, out var value4))
			{
				return value4;
			}
			List<string> list3 = new List<string>();
			IEnumerable<CollectibleObject> enumerable3 = null;
			if ((int)ing.Type == 1)
			{
				enumerable3 = (IEnumerable<CollectibleObject>)(((IWorldAccessor)capi.World).Items?.Where((Item i) => i != null));
			}
			else if ((int)ing.Type == 0)
			{
				enumerable3 = (IEnumerable<CollectibleObject>)(((IWorldAccessor)capi.World).Blocks?.Where((Block b) => b != null));
			}
			if (enumerable3 != null)
			{
				foreach (CollectibleObject item3 in enumerable3)
				{
					string text = ((object)((RegistryObject)(item3?)).Code)?.ToString();
					if (!string.IsNullOrEmpty(text))
					{
						try
						{
							AssetLocation val = new AssetLocation(text);
							if (WildcardUtil.Match(ing.PatternCode, val, array))
							{
								list3.Add(text);
							}
						}
						catch
						{
						}
					}
				}
			}
			List<string> list4 = list3.Distinct<string>(StringComparer.Ordinal).ToList();
			wildCache[key] = list4;
			return list4;
		}
	}

	private static IEnumerable<object> FetchGridRecipesMulti(ICoreClientAPI capi)
	{
		IClientWorldAccessor world = capi.World;
		List<object> list = new List<object>();
		object[] array = new object[3]
		{
			capi,
			world,
			(world != null) ? ((IWorldAccessor)world).Api : null
		};
		foreach (object obj in array)
		{
			MethodInfo methodInfo = obj?.GetType().GetMethod("GetGridRecipes", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (!(methodInfo != null) || methodInfo.GetParameters().Length != 0)
			{
				continue;
			}
			try
			{
				if (methodInfo.Invoke(obj, null) is IEnumerable item)
				{
					list.Add(item);
				}
			}
			catch
			{
			}
		}
		(object, string)[] array2 = new(object, string)[2]
		{
			(world, "CraftingRecipes"),
			((world != null) ? ((IWorldAccessor)world).Api : null, "CraftingRecipes")
		};
		for (int i = 0; i < array2.Length; i++)
		{
			(object, string) tuple = array2[i];
			if (TryGetMember(tuple.Item1?.GetType(), tuple.Item1, tuple.Item2) is IEnumerable item2)
			{
				list.Add(item2);
			}
		}
		object obj3 = TryGetMember(((object)world)?.GetType(), world, "RecipeRegistry") ?? TryGetMember((world == null) ? null : ((object)((IWorldAccessor)world).Api)?.GetType(), (world != null) ? ((IWorldAccessor)world).Api : null, "RecipeRegistry");
		if (obj3 != null && TryGetMember(obj3.GetType(), obj3, "GridRecipes") is IEnumerable item3)
		{
			list.Add(item3);
		}
		HashSet<object> seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
		foreach (object item4 in list)
		{
			if (!(item4 is IEnumerable enumerable))
			{
				continue;
			}
			foreach (object item5 in enumerable)
			{
				if (item5 != null && seen.Add(item5))
				{
					yield return item5;
				}
			}
		}
	}

	private static GridRecipeShim TryBuildGridShim(object raw, ICoreClientAPI capi)
	{
		//IL_0188: Unknown result type (might be due to invalid IL or missing references)
		//IL_018d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0452: Unknown result type (might be due to invalid IL or missing references)
		//IL_0457: Unknown result type (might be due to invalid IL or missing references)
		if (raw == null)
		{
			return null;
		}
		Type type = raw.GetType();
		if (!type.Name.Contains("GridRecipe", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		object obj = TryGetMember(type, raw, "Name");
		AssetLocation val = (AssetLocation)((obj is AssetLocation) ? obj : null);
		if (ShouldSkipRecipeByName(val))
		{
			return null;
		}
		GridRecipeShim gridRecipeShim = new GridRecipeShim
		{
			Raw = raw
		};
		bool flag = false;
		if (TryGetMember(type, raw, "resolvedIngredients") is IEnumerable enumerable)
		{
			foreach (object item in enumerable)
			{
				if (item == null)
				{
					continue;
				}
				Type type2 = item.GetType();
				GridIngredientShim gridIngredientShim = new GridIngredientShim();
				gridIngredientShim.IsTool = TryGetMember(type2, item, "IsTool") as bool? == true;
				gridIngredientShim.IsWild = TryGetMember(type2, item, "IsWildCard") as bool? == true;
				ref AssetLocation patternCode = ref gridIngredientShim.PatternCode;
				object obj2 = TryGetMember(type2, item, "Code");
				patternCode = (AssetLocation)((obj2 is AssetLocation) ? obj2 : null);
				object obj3 = TryGetMember(type2, item, "AllowedVariants");
				gridIngredientShim.Allowed = obj3 as string[];
				if (gridIngredientShim.Allowed == null && obj3 is Dictionary<string, string[]> source)
				{
					gridIngredientShim.Allowed = source.SelectMany((KeyValuePair<string, string[]> kv) => kv.Value ?? Array.Empty<string>()).Distinct().ToArray();
				}
				gridIngredientShim.Type = (EnumItemClass)(((_003F?)(TryGetMember(type2, item, "Type") as EnumItemClass?)) ?? 1);
				object obj4 = TryGetMember(type2, item, "ResolvedItemstack");
				ItemStack val2 = (ItemStack)((obj4 is ItemStack) ? obj4 : null);
				IEnumerable enumerable2 = TryGetMember(type2, item, "ResolvedItemstacks") as IEnumerable;
				if (gridIngredientShim.IsWild)
				{
					gridIngredientShim.QuantityRequired = (TryGetMember(type2, item, "Quantity") as int?) ?? 1;
				}
				else
				{
					if (val2 != null)
					{
						gridIngredientShim.Options.Add(val2);
					}
					else if (enumerable2 != null)
					{
						foreach (object item2 in enumerable2)
						{
							ItemStack val3 = (ItemStack)((item2 is ItemStack) ? item2 : null);
							if (val3 != null)
							{
								gridIngredientShim.Options.Add(val3);
							}
						}
					}
					int quantityRequired = 1;
					if (val2 != null)
					{
						quantityRequired = Math.Max(1, val2.StackSize);
					}
					else if (gridIngredientShim.Options.Count > 0)
					{
						quantityRequired = Math.Max(1, gridIngredientShim.Options[0].StackSize);
					}
					gridIngredientShim.QuantityRequired = quantityRequired;
				}
				if (gridIngredientShim.IsWild || gridIngredientShim.Options.Count > 0)
				{
					gridRecipeShim.Ingredients.Add(gridIngredientShim);
				}
				flag = true;
			}
		}
		if (!flag)
		{
			IDictionary dictionary = TryGetMember(type, raw, "Ingredients") as IDictionary;
			string text = TryGetMember(type, raw, "IngredientPattern") as string;
			if (dictionary != null && text != null)
			{
				string text2 = text;
				for (int num = 0; num < text2.Length; num++)
				{
					char c = text2[num];
					if (c == ' ' || c == '_')
					{
						continue;
					}
					string key = c.ToString();
					if (!dictionary.Contains(key))
					{
						continue;
					}
					object obj5 = dictionary[key];
					if (obj5 == null)
					{
						continue;
					}
					Type type3 = obj5.GetType();
					GridIngredientShim gridIngredientShim2 = new GridIngredientShim();
					gridIngredientShim2.IsTool = TryGetMember(type3, obj5, "IsTool") as bool? == true;
					ref AssetLocation patternCode2 = ref gridIngredientShim2.PatternCode;
					object obj6 = TryGetMember(type3, obj5, "Code");
					patternCode2 = (AssetLocation)((obj6 is AssetLocation) ? obj6 : null);
					object obj7 = TryGetMember(type3, obj5, "AllowedVariants");
					gridIngredientShim2.Allowed = obj7 as string[];
					if (gridIngredientShim2.Allowed == null && obj7 is Dictionary<string, string[]> source2)
					{
						gridIngredientShim2.Allowed = source2.SelectMany((KeyValuePair<string, string[]> kv) => kv.Value ?? Array.Empty<string>()).Distinct().ToArray();
					}
					gridIngredientShim2.Type = (EnumItemClass)(((_003F?)(TryGetMember(type3, obj5, "Type") as EnumItemClass?)) ?? 1);
					gridIngredientShim2.QuantityRequired = (TryGetMember(type3, obj5, "Quantity") as int?) ?? 1;
					bool flag2 = TryGetMember(type3, obj5, "IsWildCard") as bool? == true;
					if (!flag2)
					{
						AssetLocation patternCode3 = gridIngredientShim2.PatternCode;
						string text3 = ((patternCode3 != null) ? patternCode3.Path : null);
						if (text3 != null && (text3.Contains("*") || text3.Contains("{") || text3.Contains("}") || text3.StartsWith("@")))
						{
							flag2 = true;
						}
					}
					gridIngredientShim2.IsWild = flag2;
					if (!gridIngredientShim2.IsWild)
					{
						string code = ((object)gridIngredientShim2.PatternCode)?.ToString();
						ItemStack val4 = MakeStackFromCode(capi, code);
						if (val4 != null)
						{
							val4.StackSize = gridIngredientShim2.QuantityRequired;
							gridIngredientShim2.Options.Add(val4);
						}
					}
					if (gridIngredientShim2.IsWild || gridIngredientShim2.Options.Count > 0)
					{
						gridRecipeShim.Ingredients.Add(gridIngredientShim2);
					}
				}
			}
		}
		object obj8 = TryGetMember(type, raw, "Output");
		if (obj8 != null)
		{
			Type type4 = obj8.GetType();
			object obj9 = TryGetMember(type4, obj8, "ResolvedItemstack");
			ItemStack val5 = (ItemStack)((obj9 is ItemStack) ? obj9 : null);
			if (val5 != null && val5.Collectible != null)
			{
				gridRecipeShim.Outputs.Add(val5);
			}
			else
			{
				object obj10 = TryGetMember(type4, obj8, "Code");
				ItemStack val6 = MakeStackFromCode(capi, ((obj10 is AssetLocation) ? obj10 : null)?.ToString());
				if (val6 != null)
				{
					int val7 = (TryGetMember(type4, obj8, "Quantity") as int?) ?? 1;
					val6.StackSize = Math.Max(1, val7);
					gridRecipeShim.Outputs.Add(val6);
				}
			}
		}
		if ((TryGetMember(type, raw, "ResolvedOutputs") ?? TryGetMember(type, raw, "Outputs")) is IEnumerable enumerable3)
		{
			foreach (object item3 in enumerable3)
			{
				if (item3 == null)
				{
					continue;
				}
				ItemStack val8 = (ItemStack)((item3 is ItemStack) ? item3 : null);
				if (val8 != null)
				{
					if (val8.Collectible != null)
					{
						gridRecipeShim.Outputs.Add(val8);
						continue;
					}
					object obj11 = TryGetMember(item3.GetType(), item3, "Code");
					ItemStack val9 = MakeStackFromCode(capi, ((obj11 is AssetLocation) ? obj11 : null)?.ToString());
					if (val9 != null)
					{
						gridRecipeShim.Outputs.Add(val9);
					}
					continue;
				}
				Type type5 = item3.GetType();
				object obj12 = TryGetMember(type5, item3, "ResolvedItemstack");
				ItemStack val10 = (ItemStack)((obj12 is ItemStack) ? obj12 : null);
				if (val10 != null && val10.Collectible != null)
				{
					gridRecipeShim.Outputs.Add(val10);
					continue;
				}
				object obj13 = TryGetMember(type5, item3, "Code");
				ItemStack val11 = MakeStackFromCode(capi, ((obj13 is AssetLocation) ? obj13 : null)?.ToString());
				if (val11 != null)
				{
					int val12 = (TryGetMember(type5, item3, "Quantity") as int?) ?? 1;
					val11.StackSize = Math.Max(1, val12);
					gridRecipeShim.Outputs.Add(val11);
				}
			}
		}
		if (val != (AssetLocation)null)
		{
			gridRecipeShim.IsMod = !string.Equals(val.Domain, "game", StringComparison.Ordinal);
		}
		else
		{
			gridRecipeShim.IsMod = gridRecipeShim.Outputs.Any((ItemStack o) => ((o == null) ? null : ((RegistryObject)(o.Collectible?)).Code) != (AssetLocation)null && !string.Equals(((RegistryObject)o.Collectible).Code.Domain, "game", StringComparison.Ordinal));
		}
		return gridRecipeShim;
	}

	private static Dictionary<StackKey, List<GridRecipeShim>> BuildOutputsIndexFrom(List<GridRecipeShim> recipes)
	{
		Dictionary<StackKey, List<GridRecipeShim>> dictionary = new Dictionary<StackKey, List<GridRecipeShim>>();
		foreach (GridRecipeShim recipe in recipes)
		{
			if (recipe?.Outputs == null)
			{
				continue;
			}
			foreach (ItemStack output in recipe.Outputs)
			{
				if (!(((output == null) ? null : ((RegistryObject)(output.Collectible?)).Code) == (AssetLocation)null))
				{
					StackKey key = KeyFor(output);
					if (!dictionary.TryGetValue(key, out var value))
					{
						value = (dictionary[key] = new List<GridRecipeShim>(2));
					}
					value.Add(recipe);
				}
			}
		}
		return dictionary;
	}

	private static object TryGetMember(Type t, object obj, string name)
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

	private static ResourcePool ClonePool(ResourcePool pool)
	{
		//IL_009f: Unknown result type (might be due to invalid IL or missing references)
		ResourcePool resourcePool = new ResourcePool();
		foreach (KeyValuePair<Key, int> count in pool.Counts)
		{
			resourcePool.Counts[new Key
			{
				Code = count.Key.Code
			}] = count.Value;
		}
		foreach (KeyValuePair<Key, EnumItemClass> @class in pool.Classes)
		{
			resourcePool.Classes[new Key
			{
				Code = @class.Key.Code
			}] = @class.Value;
		}
		return resourcePool;
	}

	private static bool RecipeOutputsMatchDesired(GridRecipeShim shim, ItemStack desired)
	{
		if (desired == null)
		{
			return true;
		}
		if (shim == null || shim.Outputs == null || shim.Outputs.Count == 0)
		{
			return false;
		}
		foreach (ItemStack output in shim.Outputs)
		{
			if (output != null && output.Satisfies(desired) && desired.Satisfies(output))
			{
				return true;
			}
		}
		return false;
	}

	private static bool RecipeSatisfiedByPool(ICoreClientAPI capi, ResourcePool pool, GridRecipeShim shim, ItemStack desired)
	{
		//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
		if (shim == null)
		{
			return false;
		}
		if (!RecipeOutputsMatchDesired(shim, desired))
		{
			return false;
		}
		object obj3;
		if (desired != null)
		{
			string? obj = ((object)((RegistryObject)(desired.Collectible?)).Code)?.ToString();
			ITreeAttribute attributes = desired.Attributes;
			ITreeAttribute obj2 = ((attributes is TreeAttribute) ? attributes : null);
			obj3 = obj + " " + ((obj2 != null) ? ((TreeAttribute)obj2).ToJsonToken() : null);
		}
		else
		{
			obj3 = null;
		}
		string target = (string)obj3;
		ResourcePool resourcePool = ClonePool(pool);
		foreach (GridIngredientShim ingredient in shim.Ingredients)
		{
			if (ingredient == null)
			{
				continue;
			}
			if (ingredient.IsWild)
			{
				string[] array = ingredient.Allowed;
				if (target != null && array != null && array.Length != 0)
				{
					string text = array.FirstOrDefault((string v) => target.Contains(v));
					if (text != null)
					{
						array = new string[1] { text };
					}
				}
				if (!resourcePool.TryConsumeWildcard(ingredient.Type, ingredient.PatternCode, array, Math.Max(1, ingredient.QuantityRequired), consume: true))
				{
					return false;
				}
			}
			else if (!resourcePool.TryConsumeAny(ingredient.Options, Math.Max(1, ingredient.QuantityRequired), consume: true))
			{
				return false;
			}
		}
		return true;
	}

	private static IEnumerable<GridRecipeShim> CandidateShimsForStack(ICoreClientAPI capi, ItemStack desired, bool? modsOnly, Dictionary<StackKey, List<GridRecipeShim>> index)
	{
		StackKey key = KeyFor(desired);
		if (index == null || !index.TryGetValue(key, out var value) || value == null)
		{
			yield break;
		}
		foreach (GridRecipeShim item in value)
		{
			if (item != null && (!modsOnly.HasValue || modsOnly.Value == item.IsMod))
			{
				yield return item;
			}
		}
	}

	private static void AddCraftablePagesFromAllStacksFiltered(ICoreClientAPI capi, ResourcePool pool, HashSet<string> dest, int partitions, Dictionary<StackKey, List<GridRecipeShim>> index, Func<GridRecipeShim, bool> recipePredicate, string callerName)
	{
		try
		{
			Func<GridRecipeShim, bool> predicate = recipePredicate ?? ((Func<GridRecipeShim, bool>)((GridRecipeShim _) => true));
			if (callerName == null)
			{
				callerName = "AddCraftablePagesFromAllStacks";
			}
			Type type = AccessTools.TypeByName("Vintagestory.GameContent.ModSystemSurvivalHandbook");
			object obj = ((type != null) ? GetModSystemByType(capi, type) : null);
			ItemStack[] stacks = AccessTools.Field(type, "allstacks")?.GetValue(obj) as ItemStack[];
			if (stacks == null || stacks.Length == 0)
			{
				return;
			}
			Type type2 = AccessTools.TypeByName("Vintagestory.GameContent.GuiHandbookItemStackPage");
			ConstructorInfo ctor = type2?.GetConstructor(new Type[2]
			{
				typeof(ICoreClientAPI),
				typeof(ItemStack)
			});
			FieldInfo fiStack = AccessTools.Field(type2, "Stack");
			MethodInfo miPageCode = type2?.GetMethod("PageCodeForStack", BindingFlags.Static | BindingFlags.Public);
			if (ctor == null || miPageCode == null)
			{
				return;
			}
			if (partitions <= 0)
			{
				partitions = RecommendParallelism(stacks.Length, 32);
			}
			if (partitions == 1 || stacks.Length < 2)
			{
				ItemStack[] array = stacks;
				foreach (ItemStack val in array)
				{
					if (((val != null) ? val.Collectible : null) == null)
					{
						continue;
					}
					object obj2 = ctor.Invoke(new object[2] { capi, val });
					object? obj3 = fiStack?.GetValue(obj2);
					ItemStack val2 = (ItemStack)(((obj3 is ItemStack) ? obj3 : null) ?? val);
					foreach (GridRecipeShim item in CandidateShimsForStack(capi, val2, false, index))
					{
						if (predicate(item) && RecipeSatisfiedByPool(capi, pool, item, val2))
						{
							string text = miPageCode.Invoke(null, new object[1] { val2 }) as string;
							if (!string.IsNullOrEmpty(text))
							{
								dest.Add(text);
							}
							break;
						}
					}
				}
				return;
			}
			int[] order;
			if (index != null && index.Count > 0)
			{
				order = (from t in Enumerable.Range(0, stacks.Length).Select(delegate(int i)
					{
						StackKey key = KeyFor(stacks[i]);
						List<GridRecipeShim> value;
						return (i: i, weight: index.TryGetValue(key, out value) ? value.Count : 0);
					})
					orderby t.weight descending
					select t.i).ToArray();
			}
			else
			{
				order = Enumerable.Range(0, stacks.Length).ToArray();
			}
			partitions = Math.Min(partitions, stacks.Length);
			Stopwatch stopwatch = Stopwatch.StartNew();
			Task<HashSet<string>>[] array2 = new Task<HashSet<string>>[partitions];
			int next = 0;
			for (int num2 = 0; num2 < partitions; num2++)
			{
				int partIndex = num2;
				array2[num2] = Task.Run(delegate
				{
					HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
					Stopwatch stopwatch2 = Stopwatch.StartNew();
					Stopwatch stopwatch3 = Stopwatch.StartNew();
					int num3 = 0;
					while (true)
					{
						int num4 = Interlocked.Add(ref next, 32) - 32;
						if (num4 >= order.Length)
						{
							break;
						}
						int num5 = Math.Min(num4 + 32, order.Length);
						for (int i = num4; i < num5; i++)
						{
							int num6 = order[i];
							ItemStack val3 = stacks[num6];
							if (((val3 != null) ? val3.Collectible : null) != null)
							{
								Stopwatch stopwatch4 = Stopwatch.StartNew();
								object obj5 = ctor.Invoke(new object[2] { capi, val3 });
								object? obj6 = fiStack?.GetValue(obj5);
								ItemStack val4 = (ItemStack)(((obj6 is ItemStack) ? obj6 : null) ?? val3);
								Dictionary<StackKey, string> cachedPageCodeMap = GetCachedPageCodeMap(capi);
								foreach (GridRecipeShim item2 in CandidateShimsForStack(capi, val4, false, index))
								{
									if (predicate(item2) && RecipeSatisfiedByPool(capi, pool, item2, val4))
									{
										if (!cachedPageCodeMap.TryGetValue(KeyFor(val4), out var value))
										{
											value = miPageCode.Invoke(null, new object[1] { val4 }) as string;
										}
										if (!string.IsNullOrEmpty(value))
										{
											hashSet.Add(value);
										}
										break;
									}
								}
								stopwatch4.Stop();
								if (stopwatch4.ElapsedMilliseconds > 175)
								{
									string value2 = ((val4 == null) ? null : ((object)((RegistryObject)(val4.Collectible?)).Code)?.ToString()) ?? ((object)((RegistryObject)(val3.Collectible?)).Code)?.ToString() ?? $"index={num6}";
									LogEverywhere(capi, $"Slow stack {value2} took {stopwatch4.ElapsedMilliseconds}ms to process on partition {partIndex + 1}/{partitions}", toChat: false, callerName);
								}
								num3++;
							}
						}
						if (stopwatch3.ElapsedMilliseconds >= 3)
						{
							Thread.Yield();
							stopwatch3.Restart();
						}
					}
					stopwatch2.Stop();
					LogEverywhere(capi, $"Partition {partIndex + 1}/{partitions} processed {num3} item stacks in {stopwatch2.ElapsedMilliseconds}ms", toChat: false, callerName);
					return hashSet;
				});
			}
			Task[] tasks = array2;
			Task.WaitAll(tasks);
			Task<HashSet<string>>[] array3 = array2;
			for (int num = 0; num < array3.Length; num++)
			{
				foreach (string item3 in array3[num].Result)
				{
					dest.Add(item3);
				}
			}
			stopwatch.Stop();
			LogEverywhere(capi, $"Processed {stacks.Length} item stacks in {stopwatch.ElapsedMilliseconds}ms using {partitions} partitions", toChat: false, callerName);
		}
		catch
		{
		}
	}

	private static void AddCraftablePagesFromAllStacks(ICoreClientAPI capi, ResourcePool pool, HashSet<string> dest, Dictionary<StackKey, List<GridRecipeShim>> index)
	{
		AddCraftablePagesFromAllStacks(capi, pool, dest, ConfiguredAllStacksPartitions, index);
	}

	private static void AddCraftablePagesFromAllStacks(ICoreClientAPI capi, ResourcePool pool, HashSet<string> dest, int partitions, Dictionary<StackKey, List<GridRecipeShim>> index)
	{
		AddCraftablePagesFromAllStacksFiltered(capi, pool, dest, partitions, index, (GridRecipeShim shim) => !IsWoodRecipe(shim.Raw), "AddCraftablePagesFromAllStacks");
	}

	private static void AddCraftablePagesFromAllStacksFromModStacks(ICoreClientAPI capi, ResourcePool pool, HashSet<string> dest, Dictionary<StackKey, List<GridRecipeShim>> index)
	{
		AddCraftablePagesFromAllStacksFromModStacks(capi, pool, dest, ConfiguredAllStacksPartitions, index);
	}

	private static void AddCraftablePagesFromAllStacksFromModStacks(ICoreClientAPI capi, ResourcePool pool, HashSet<string> dest, int partitions, Dictionary<StackKey, List<GridRecipeShim>> index)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		try
		{
			Type type = AccessTools.TypeByName("Vintagestory.GameContent.ModSystemSurvivalHandbook");
			object obj = ((type != null) ? GetModSystemByType(capi, type) : null);
			ItemStack[] stacks = AccessTools.Field(type, "allstacks")?.GetValue(obj) as ItemStack[];
			if (stacks == null || stacks.Length == 0)
			{
				return;
			}
			Type type2 = AccessTools.TypeByName("Vintagestory.GameContent.GuiHandbookItemStackPage");
			ConstructorInfo ctor = type2?.GetConstructor(new Type[2]
			{
				typeof(ICoreClientAPI),
				typeof(ItemStack)
			});
			FieldInfo fiStack = AccessTools.Field(type2, "Stack");
			MethodInfo miPageCode = type2?.GetMethod("PageCodeForStack", BindingFlags.Static | BindingFlags.Public);
			if (ctor == null || miPageCode == null)
			{
				return;
			}
			if (partitions <= 0)
			{
				partitions = RecommendParallelism(stacks.Length);
			}
			if (partitions == 1 || stacks.Length < 2)
			{
				ItemStack[] array = stacks;
				foreach (ItemStack val in array)
				{
					if (((val != null) ? val.Collectible : null) == null)
					{
						continue;
					}
					object obj2 = ctor.Invoke(new object[2] { capi, val });
					object? obj3 = fiStack?.GetValue(obj2);
					ItemStack val2 = (ItemStack)(((obj3 is ItemStack) ? obj3 : null) ?? val);
					foreach (GridRecipeShim item in CandidateShimsForStack(capi, val2, true, index))
					{
						if (RecipeSatisfiedByPool(capi, pool, item, val2))
						{
							string text = miPageCode.Invoke(null, new object[1] { val2 }) as string;
							if (!string.IsNullOrEmpty(text))
							{
								dest.Add(text);
							}
							break;
						}
					}
				}
				return;
			}
			partitions = Math.Min(partitions, stacks.Length);
			Task<HashSet<string>>[] array2 = new Task<HashSet<string>>[partitions];
			int next = 0;
			for (int j = 0; j < partitions; j++)
			{
				int partIndex = j;
				array2[j] = Task.Run(delegate
				{
					HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
					Stopwatch stopwatch2 = Stopwatch.StartNew();
					Stopwatch stopwatch3 = Stopwatch.StartNew();
					int num = 0;
					while (true)
					{
						int num2 = Interlocked.Add(ref next, 64) - 64;
						if (num2 >= stacks.Length)
						{
							break;
						}
						int num3 = Math.Min(num2 + 64, stacks.Length);
						for (int k = num2; k < num3; k++)
						{
							ItemStack val3 = stacks[k];
							if (((val3 != null) ? val3.Collectible : null) != null)
							{
								Stopwatch stopwatch4 = Stopwatch.StartNew();
								object obj5 = ctor.Invoke(new object[2] { capi, val3 });
								object? obj6 = fiStack?.GetValue(obj5);
								ItemStack val4 = (ItemStack)(((obj6 is ItemStack) ? obj6 : null) ?? val3);
								foreach (GridRecipeShim item2 in CandidateShimsForStack(capi, val4, true, index))
								{
									if (RecipeSatisfiedByPool(capi, pool, item2, val4))
									{
										string text2 = miPageCode.Invoke(null, new object[1] { val4 }) as string;
										if (!string.IsNullOrEmpty(text2))
										{
											hashSet.Add(text2);
										}
										break;
									}
								}
								stopwatch4.Stop();
								if (stopwatch4.ElapsedMilliseconds > 175)
								{
									string value = ((val4 == null) ? null : ((object)((RegistryObject)(val4.Collectible?)).Code)?.ToString()) ?? ((object)((RegistryObject)(val3.Collectible?)).Code)?.ToString() ?? $"index={k}";
									LogEverywhere(capi, $"Slow stack {value} took {stopwatch4.ElapsedMilliseconds}ms to process on partition {partIndex + 1}/{partitions}", toChat: false, "AddCraftablePagesFromAllStacksFromModStacks");
								}
								num++;
							}
						}
						if (stopwatch3.ElapsedMilliseconds >= 3)
						{
							Thread.Yield();
							stopwatch3.Restart();
						}
					}
					stopwatch2.Stop();
					LogEverywhere(capi, $"Partition {partIndex + 1}/{partitions} processed {num} item stacks in {stopwatch2.ElapsedMilliseconds}ms", toChat: false, "AddCraftablePagesFromAllStacksFromModStacks");
					return hashSet;
				});
			}
			Task[] tasks = array2;
			Task.WaitAll(tasks);
			Task<HashSet<string>>[] array3 = array2;
			for (int i = 0; i < array3.Length; i++)
			{
				foreach (string item3 in array3[i].Result)
				{
					dest.Add(item3);
				}
			}
			stopwatch.Stop();
			LogEverywhere(capi, $"Processed {stacks.Length} item stacks in {stopwatch.ElapsedMilliseconds}ms using {partitions} partitions", toChat: false, "AddCraftablePagesFromAllStacksFromModStacks");
		}
		catch
		{
		}
	}

	private static void AddCraftablePagesFromAllStacks_WoodOnly(ICoreClientAPI capi, ResourcePool pool, HashSet<string> dest, Dictionary<StackKey, List<GridRecipeShim>> index)
	{
		AddCraftablePagesFromAllStacks_WoodOnly(capi, pool, dest, ConfiguredAllStacksPartitions, index);
	}

	private static void AddCraftablePagesFromAllStacks_WoodOnly(ICoreClientAPI capi, ResourcePool pool, HashSet<string> dest, int partitions, Dictionary<StackKey, List<GridRecipeShim>> index)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		try
		{
			AddCraftablePagesFromAllStacksFiltered(capi, pool, dest, partitions, index, (GridRecipeShim shim) => IsWoodRecipe(shim.Raw), "AddCraftablePagesFromAllStacks_WoodOnly");
		}
		catch
		{
		}
		finally
		{
			stopwatch.Stop();
			LogEverywhere(capi, $"AddCraftablePagesFromAllStacks_WoodOnly completed in {stopwatch.ElapsedMilliseconds}ms", toChat: false, "AddCraftablePagesFromAllStacks_WoodOnly");
		}
	}

	private static void AddCraftablePagesFromAllStacks_StoneOnly(ICoreClientAPI capi, ResourcePool pool, HashSet<string> dest, Dictionary<StackKey, List<GridRecipeShim>> index)
	{
		AddCraftablePagesFromAllStacks_StoneOnly(capi, pool, dest, ConfiguredAllStacksPartitions, index);
	}

	private static void AddCraftablePagesFromAllStacks_StoneOnly(ICoreClientAPI capi, ResourcePool pool, HashSet<string> dest, int partitions, Dictionary<StackKey, List<GridRecipeShim>> index)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		try
		{
			AddCraftablePagesFromAllStacksFiltered(capi, pool, dest, partitions, index, (GridRecipeShim shim) => IsStoneRecipe(shim.Raw), "AddCraftablePagesFromAllStacks_StoneOnly");
		}
		catch
		{
		}
		finally
		{
			stopwatch.Stop();
			LogEverywhere(capi, $"AddCraftablePagesFromAllStacks_StoneOnly completed in {stopwatch.ElapsedMilliseconds}ms", toChat: false, "AddCraftablePagesFromAllStacks_StoneOnly");
		}
	}

	private void OnServerScanReply(CraftScanReply data)
	{
		//IL_010d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0114: Unknown result type (might be due to invalid IL or missing references)
		//IL_011b: Expected O, but got Unknown
		//IL_011f: Invalid comparison between Unknown and I4
		//IL_0162: Unknown result type (might be due to invalid IL or missing references)
		//IL_0169: Expected O, but got Unknown
		//IL_013d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0144: Expected O, but got Unknown
		try
		{
			CraftScanReply craftScanReply = data;
			if (craftScanReply != null && craftScanReply.IsFetch)
			{
				int codeCount = data.Codes?.Count ?? 0;
				int scanId = data.ScanId;
				((IEventAPI)_capi.Event).EnqueueMainThreadTask((Action)delegate
				{
					try
					{
						LogEverywhere(_capi, $"[Fetch] ← #{scanId} stacks={codeCount} (skipping cache rebuild)", toChat: true, "OnServerScanReply");
					}
					finally
					{
						FinishScanAndProcessQueue(_capi);
					}
				}, "SCFetchReply");
				return;
			}
			ResourcePool pool = new ResourcePool();
			for (int num = 0; num < data.Codes.Count; num++)
			{
				string text = data.Codes[num];
				int num2 = data.Counts[num];
				EnumItemClass val = data.Classes[num];
				AssetLocation val2 = new AssetLocation(text);
				ItemStack val3 = null;
				if ((int)val == 1)
				{
					Item item = ((IWorldAccessor)_capi.World).GetItem(val2);
					if (item != null)
					{
						val3 = new ItemStack(item, num2);
					}
				}
				else
				{
					Block block = ((IWorldAccessor)_capi.World).GetBlock(val2);
					if (block != null)
					{
						val3 = new ItemStack(block, num2);
					}
				}
				if (val3 != null)
				{
					pool.Add(val3);
				}
			}
			string tabKey = data.TabKey;
			string text2 = null;
			bool flag = false;
			bool flag2 = false;
			bool flag3 = false;
			ScanRequestInfo value = default(ScanRequestInfo);
			bool flag4 = false;
			if (data.ScanId != 0)
			{
				lock (InflightMapLock)
				{
					if (InflightById.TryGetValue(data.ScanId, out value))
					{
						InflightById.Remove(data.ScanId);
						flag4 = true;
					}
				}
			}
			if (string.IsNullOrEmpty(tabKey) && flag4 && !string.IsNullOrEmpty(value.TabKey))
			{
				tabKey = value.TabKey;
				bool includeAll = value.IncludeAll;
				flag = value.ModsOnly;
				flag2 = value.WoodOnly;
				flag3 = value.StoneOnly;
				text2 = GetVariantKey(includeAll, flag, flag2, flag3);
			}
			if (string.IsNullOrEmpty(tabKey))
			{
				lock (PendingScanLock)
				{
					text2 = PendingScanVariantKey;
					tabKey = PendingScanTabKey;
					PendingScanVariantKey = null;
					PendingScanTabKey = null;
				}
				if (string.IsNullOrEmpty(tabKey))
				{
					tabKey = ((!string.IsNullOrEmpty(text2)) ? TabKeyFromVariant(text2) : GetActiveTabKey());
				}
				if (string.IsNullOrEmpty(text2))
				{
					text2 = VariantKeyFromTabKey(tabKey);
				}
				string.Equals(text2, "all", StringComparison.Ordinal);
				flag = string.Equals(text2, "mods", StringComparison.Ordinal);
				flag2 = string.Equals(text2, "wood", StringComparison.Ordinal);
				flag3 = string.Equals(text2, "stone", StringComparison.Ordinal);
			}
			ulong dna = ComputeResourcePoolDNA(pool);
			bool flag5;
			lock (DnaLock)
			{
				flag5 = TabPoolDNA.TryGetValue(tabKey, out var value2) && value2 == dna;
			}
			int cachedPages;
			lock (CacheLock)
			{
				cachedPages = GetTabCache(tabKey)?.Count ?? 0;
			}
			bool flag6 = cachedPages == 0;
			if (flag5 && !flag6)
			{
				SetTabReadyToUpdateUI(tabKey, ready: true);
				((IEventAPI)_capi.Event).EnqueueMainThreadTask((Action)delegate
				{
					try
					{
						LogEverywhere(_capi, $"[Scan] ← #{data.ScanId} DNA MATCH tab={tabKey} dna=0x{dna:X16} pages={cachedPages}", toChat: true, "OnServerScanReply");
						TryUpdateActiveTabFromCache(_capi, tabKey);
					}
					finally
					{
						FinishScanAndProcessQueue(_capi);
					}
				}, "SCScanMatch");
				return;
			}
			string rebuildReason = (flag5 ? "CACHE EMPTY" : "DNA MISMATCH");
			SetTabReadyToUpdateUI(tabKey, ready: false);
			((IEventAPI)_capi.Event).EnqueueMainThreadTask((Action)delegate
			{
				if (IsTabActive(tabKey))
				{
					SetUpdatingText(_capi, show: true);
				}
			}, "SCSetUpdating");
			Task.Run(delegate
			{
				try
				{
					int outputs;
					int fetched;
					int usable;
					List<string> generatedPageCodes;
					int pages = RebuildCacheWithPool(_capi, pool, tabKey, out outputs, out fetched, out usable, out generatedPageCodes);
					lock (DnaLock)
					{
						TabPoolDNA[tabKey] = dna;
					}
					SetTabReadyToUpdateUI(tabKey, ready: true);
					((IEventAPI)_capi.Event).EnqueueMainThreadTask((Action)delegate
					{
						try
						{
							LogEverywhere(_capi, $"[Scan] ← #{data.ScanId} REBUILD ({rebuildReason}) tab={tabKey} dna=0x{dna:X16} outputs={outputs}, pages={pages}, fetched={fetched}, usable={usable}", toChat: true, "OnServerScanReply");
							TryUpdateActiveTabFromCache(_capi, tabKey);
						}
						finally
						{
							FinishScanAndProcessQueue(_capi);
						}
					}, "SCShowNewCache");
				}
				catch (Exception ex)
				{
					Exception ex2 = ex;
					Exception ex3 = ex2;
					((IEventAPI)_capi.Event).EnqueueMainThreadTask((Action)delegate
					{
						try
						{
							LogEverywhere(_capi, $"Rebuild failed: {ex3}", toChat: true, "OnServerScanReply");
						}
						finally
						{
							FinishScanAndProcessQueue(_capi);
						}
					}, "SCRebuildFail");
				}
			});
		}
		catch (Exception value3)
		{
			LogEverywhere(_capi, $"OnServerScanReply error: {value3}", toChat: true, "OnServerScanReply");
			FinishScan(_capi);
			ICoreClientAPI capi = _capi;
			if (capi != null)
			{
				((IEventAPI)capi.Event).EnqueueMainThreadTask((Action)delegate
				{
					TryProcessQueuedScan(_capi);
				}, "SCScanReplyError");
			}
		}
	}

	private static int RebuildCacheWithPool(ICoreClientAPI capi, ResourcePool pool, string tabKey, out int craftableOutputsCount, out int fetched, out int usable, out List<string> generatedPageCodes)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		string text = VariantKeyFromTabKey(tabKey);
		if (!EnsureRecipeIndexVariantReady(capi, text))
		{
			throw new InvalidOperationException("Recipe index variant '" + text + "' not ready");
		}
		bool flag = string.Equals(text, "all", StringComparison.Ordinal);
		bool flag2 = string.Equals(text, "mods", StringComparison.Ordinal);
		bool flag3 = string.Equals(text, "wood", StringComparison.Ordinal);
		bool flag4 = string.Equals(text, "stone", StringComparison.Ordinal);
		craftableOutputsCount = 0;
		fetched = recipesFetched;
		usable = recipesUsable;
		HashSet<GridRecipeShim> hashSet = new HashSet<GridRecipeShim>(ReferenceEqualityComparer.Instance);
		foreach (KeyValuePair<Key, int> count in pool.Counts)
		{
			string code = count.Key.Code;
			if (!codeToRecipeGroups.TryGetValue(code, out List<(GridRecipeShim, string)> value))
			{
				continue;
			}
			foreach (var item2 in value)
			{
				GridRecipeShim item = item2.Item1;
				hashSet.Add(item);
			}
		}
		Dictionary<GridRecipeShim, Dictionary<string, int>> dictionary = new Dictionary<GridRecipeShim, Dictionary<string, int>>(ReferenceEqualityComparer.Instance);
		foreach (GridRecipeShim item3 in hashSet)
		{
			dictionary[item3] = recipeGroupNeeds[item3].ToDictionary<KeyValuePair<string, int>, string, int>((KeyValuePair<string, int> g) => g.Key, (KeyValuePair<string, int> g) => g.Value, StringComparer.Ordinal);
		}
		new Dictionary<string, int>(StringComparer.Ordinal);
		HashSet<GridRecipeShim> hashSet2 = new HashSet<GridRecipeShim>(ReferenceEqualityComparer.Instance);
		foreach (KeyValuePair<Key, int> count2 in pool.Counts)
		{
			string code2 = count2.Key.Code;
			int value2 = count2.Value;
			if (value2 <= 0 || !codeToRecipeGroups.TryGetValue(code2, out List<(GridRecipeShim, string)> value3) || value3 == null || value3.Count == 0)
			{
				continue;
			}
			Dictionary<GridRecipeShim, int> dictionary2 = new Dictionary<GridRecipeShim, int>(ReferenceEqualityComparer.Instance);
			foreach (var (key, key2) in value3)
			{
				if (dictionary.TryGetValue(key, out var value4) && value4.TryGetValue(key2, out var value5) && value5 > 0)
				{
					int num = Math.Min(value5, value2);
					if (num > 0)
					{
						value4[key2] = value5 - num;
					}
					dictionary2[key] = (dictionary2.TryGetValue(key, out var value6) ? value6 : 0) + 1;
				}
			}
			foreach (KeyValuePair<GridRecipeShim, int> item4 in dictionary2)
			{
				if (item4.Value >= 2)
				{
					hashSet2.Add(item4.Key);
				}
			}
		}
		HashSet<StackKey> hashSet3 = new HashSet<StackKey>();
		foreach (KeyValuePair<GridRecipeShim, Dictionary<string, int>> item5 in dictionary)
		{
			GridRecipeShim key3 = item5.Key;
			Dictionary<string, int> value7 = item5.Value;
			if (value7.Count > 0 && value7.All((KeyValuePair<string, int> g) => g.Value <= 0) && (!hashSet2.Contains(key3) || CanSatisfyPrecisely_NoGkeyToCodes(key3, pool)))
			{
				ExpandOutputsForRecipe(capi, pool, key3, hashSet3, recipeGroupNeeds);
			}
		}
		craftableOutputsCount = hashSet3.Count;
		Dictionary<StackKey, string> cachedPageCodeMap = GetCachedPageCodeMap(capi);
		HashSet<string> resultPageCodes = new HashSet<string>(StringComparer.Ordinal);
		MethodInfo methodInfo = AccessTools.TypeByName("Vintagestory.GameContent.GuiHandbookItemStackPage")?.GetMethod("PageCodeForStack", BindingFlags.Static | BindingFlags.Public);
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		foreach (StackKey item6 in hashSet3)
		{
			string value8 = null;
			bool flag5;
			lock (PageCodeMapLock)
			{
				flag5 = cachedPageCodeMap.TryGetValue(item6, out value8);
			}
			if (flag5)
			{
				resultPageCodes.Add(value8);
				num2++;
			}
			else if (methodInfo != null)
			{
				try
				{
					ItemStack val = KeyToItemStack(capi, item6);
					if (val != null)
					{
						value8 = (string)methodInfo.Invoke(null, new object[1] { val });
						if (!string.IsNullOrEmpty(value8))
						{
							resultPageCodes.Add(value8);
						}
						else
						{
							num3++;
						}
					}
				}
				catch
				{
					num3++;
				}
			}
			num4++;
			if (num4 % 64 == 0)
			{
				Flush();
			}
		}
		if (!TryGetRecipeIndex(text, out var data))
		{
			throw new InvalidOperationException("Recipe index variant '" + text + "' not ready");
		}
		Dictionary<StackKey, List<GridRecipeShim>> index = data.OutputsIndex ?? new Dictionary<StackKey, List<GridRecipeShim>>();
		if (flag)
		{
			AddCraftablePagesFromAllStacksFromModStacks(capi, pool, resultPageCodes, index);
			AddCraftablePagesFromAllStacks_WoodOnly(capi, pool, resultPageCodes, index);
			AddCraftablePagesFromAllStacks_StoneOnly(capi, pool, resultPageCodes, index);
			AddCraftablePagesFromAllStacks(capi, pool, resultPageCodes, index);
		}
		else if (flag2)
		{
			AddCraftablePagesFromAllStacksFromModStacks(capi, pool, resultPageCodes, index);
		}
		else if (flag3)
		{
			AddCraftablePagesFromAllStacks_WoodOnly(capi, pool, resultPageCodes, index);
		}
		else if (flag4)
		{
			AddCraftablePagesFromAllStacks_StoneOnly(capi, pool, resultPageCodes, index);
		}
		else
		{
			AddCraftablePagesFromAllStacks(capi, pool, resultPageCodes, index);
		}
		craftableOutputsCount = resultPageCodes.Count;
		Flush();
		List<string> list = (generatedPageCodes = resultPageCodes.ToList());
		lock (CacheLock)
		{
			SetTabCache(tabKey, list);
		}
		stopwatch.Stop();
		LogEverywhere(capi, $"RebuildCacheWithPool completed in {stopwatch.ElapsedMilliseconds}ms", toChat: false, "RebuildCacheWithPool");
		return list.Count;
		static bool CanSatisfyPrecisely_NoGkeyToCodes(GridRecipeShim recipe, ResourcePool poolLocal)
		{
			if (recipe == null)
			{
				return false;
			}
			Dictionary<string, int> codeAvail = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (KeyValuePair<Key, int> count3 in poolLocal.Counts)
			{
				codeAvail[count3.Key.Code] = count3.Value;
			}
			foreach (KeyValuePair<string, int> gkv in recipeGroupNeeds[recipe])
			{
				int value9 = gkv.Value;
				if (value9 > 0)
				{
					HashSet<string> value11;
					IEnumerable<string> enumerable = ((!gkv.Key.StartsWith("wild:", StringComparison.Ordinal)) ? (from c in gkv.Key.Split('|')
						where codeAvail.ContainsKey(c)
						select c) : codeAvail.Keys.Where((string c) => codeToGkeys.TryGetValue(c, out value11) && value11 != null && value11.Contains(gkv.Key)));
					int num5 = 0;
					foreach (string item7 in enumerable)
					{
						int value10;
						int num6 = (codeAvail.TryGetValue(item7, out value10) ? value10 : 0);
						if (num6 > 0)
						{
							int num7 = Math.Min(value9 - num5, num6);
							if (num7 > 0)
							{
								codeAvail[item7] = num6 - num7;
								num5 += num7;
								if (num5 >= value9)
								{
									break;
								}
							}
						}
					}
					if (num5 < value9)
					{
						return false;
					}
				}
			}
			return true;
		}
		void Flush()
		{
			if (IsTabActive(tabKey))
			{
				List<string> cachedPageCodes = resultPageCodes.ToList();
				lock (CacheLock)
				{
					CachedPageCodes = cachedPageCodes;
				}
				((IEventAPI)capi.Event).EnqueueMainThreadTask((Action)delegate
				{
					TryRefreshOpenDialog(capi);
				}, (string)null);
			}
		}
	}
}
