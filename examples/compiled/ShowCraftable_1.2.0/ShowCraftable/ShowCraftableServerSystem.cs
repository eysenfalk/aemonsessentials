using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ShowCraftable;

public class ShowCraftableServerSystem : ModSystem
{
	private class SlotRef
	{
		public ItemSlot Slot;

		public string Code;

		public EnumItemClass Class;

		public BlockEntity BlockEntity;
	}

	private ICoreServerAPI sapi;

	public override void StartServerSide(ICoreServerAPI api)
	{
		sapi = api;
		api.Network.RegisterChannel("showcraftablescan").RegisterMessageType(typeof(CraftScanRequest)).RegisterMessageType(typeof(CraftScanReply))
			.SetMessageHandler<CraftScanRequest>((NetworkClientMessageHandler<CraftScanRequest>)OnScanRequest);
	}

	private bool SlotMatches(SlotRef slot, CraftIngredient ing)
	{
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0082: Expected O, but got Unknown
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ae: Expected O, but got Unknown
		if (ing != null)
		{
			object obj;
			if (slot == null)
			{
				obj = null;
			}
			else
			{
				ItemSlot slot2 = slot.Slot;
				obj = ((slot2 != null) ? slot2.Itemstack : null);
			}
			if (obj != null)
			{
				if (ing.Codes != null && ing.Codes.Contains(slot.Code))
				{
					return true;
				}
				if (ing.IsWildcard && !string.IsNullOrEmpty(ing.PatternCode))
				{
					try
					{
						if (ing.HasType && slot.Class != ing.Type)
						{
							return false;
						}
						AssetLocation val = new AssetLocation(ing.PatternCode);
						AssetLocation val2 = new AssetLocation(slot.Code);
						string[] array = ((ing.Allowed != null && ing.Allowed.Count > 0) ? ing.Allowed.ToArray() : null);
						if (WildcardUtil.Match(val, val2, array))
						{
							return true;
						}
					}
					catch
					{
					}
				}
				return false;
			}
		}
		return false;
	}

	private bool CanSatisfyVariant(Dictionary<string, int> counts, CraftIngredientList variant)
	{
		//IL_019f: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a6: Expected O, but got Unknown
		//IL_0149: Unknown result type (might be due to invalid IL or missing references)
		//IL_0150: Expected O, but got Unknown
		if (variant == null)
		{
			return false;
		}
		Dictionary<string, int> dictionary = new Dictionary<string, int>(counts);
		foreach (CraftIngredient ingredient in variant.Ingredients)
		{
			int num = ingredient.Quantity;
			if (num <= 0)
			{
				continue;
			}
			if (ingredient.Codes != null && ingredient.Codes.Count > 0)
			{
				int num2 = 0;
				foreach (string code in ingredient.Codes)
				{
					if (dictionary.TryGetValue(code, out var value))
					{
						num2 += value;
					}
					if (num2 >= num)
					{
						break;
					}
				}
				if (num2 < num)
				{
					return false;
				}
				foreach (string code2 in ingredient.Codes)
				{
					if (num <= 0)
					{
						break;
					}
					if (dictionary.TryGetValue(code2, out var value2) && value2 > 0)
					{
						int num3 = Math.Min(num, value2);
						dictionary[code2] = value2 - num3;
						if (dictionary[code2] <= 0)
						{
							dictionary.Remove(code2);
						}
						num -= num3;
					}
				}
				continue;
			}
			if (ingredient.IsWildcard && !string.IsNullOrEmpty(ingredient.PatternCode))
			{
				AssetLocation val = new AssetLocation(ingredient.PatternCode);
				string[] array = ((ingredient.Allowed != null && ingredient.Allowed.Count > 0) ? ingredient.Allowed.ToArray() : null);
				foreach (KeyValuePair<string, int> item in dictionary.ToList())
				{
					if (num <= 0)
					{
						break;
					}
					try
					{
						AssetLocation val2 = new AssetLocation(item.Key);
						if (!WildcardUtil.Match(val, val2, array))
						{
							continue;
						}
					}
					catch
					{
						continue;
					}
					int num4 = Math.Min(num, item.Value);
					dictionary[item.Key] = item.Value - num4;
					if (dictionary[item.Key] <= 0)
					{
						dictionary.Remove(item.Key);
					}
					num -= num4;
				}
				if (num > 0)
				{
					return false;
				}
				continue;
			}
			return false;
		}
		return true;
	}

	private bool ExecuteVariant(List<SlotRef> slots, CraftIngredientList variant, IServerPlayer player, Dictionary<string, (int count, EnumItemClass cls)> sum, Dictionary<string, int> playerCounts)
	{
		//IL_00df: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e6: Expected O, but got Unknown
		//IL_0212: Unknown result type (might be due to invalid IL or missing references)
		bool result = true;
		Dictionary<string, int> counts = ((playerCounts != null) ? new Dictionary<string, int>(playerCounts) : new Dictionary<string, int>());
		foreach (CraftIngredient ingredient in variant.Ingredients)
		{
			int quantity = ingredient.Quantity;
			if (quantity <= 0)
			{
				continue;
			}
			quantity = DeductFrom(counts, ingredient, quantity);
			if (quantity <= 0)
			{
				continue;
			}
			foreach (SlotRef slot in slots)
			{
				if (quantity <= 0)
				{
					break;
				}
				if (!SlotMatches(slot, ingredient))
				{
					continue;
				}
				int num = Math.Min(quantity, slot.Slot.StackSize);
				ItemStack val = slot.Slot.TakeOut(num);
				if (val == null)
				{
					continue;
				}
				int stackSize = val.StackSize;
				((IPlayer)player).InventoryManager.TryGiveItemstack(val, false);
				int num2 = stackSize - val.StackSize;
				quantity -= num2;
				if (val.StackSize > 0)
				{
					DummySlot val2 = new DummySlot(val);
					((ItemSlot)val2).TryPutInto(((Entity)((IPlayer)player).Entity).World, slot.Slot, val.StackSize);
					if (!((ItemSlot)val2).Empty)
					{
						((Entity)((IPlayer)player).Entity).World.SpawnItemEntity(((ItemSlot)val2).Itemstack, ((Entity)((IPlayer)player).Entity).Pos.XYZ, (Vec3d)null);
					}
				}
				slot.Slot.MarkDirty();
				BlockEntity blockEntity = slot.BlockEntity;
				BlockEntityGroundStorage val3 = (BlockEntityGroundStorage)(object)((blockEntity is BlockEntityGroundStorage) ? blockEntity : null);
				if (val3 != null)
				{
					if (((BlockEntityContainer)val3).Inventory.Empty && !val3.clientsideFirstPlacement)
					{
						((BlockEntity)val3).Api.World.BlockAccessor.SetBlock(0, ((BlockEntity)val3).Pos);
						((BlockEntity)val3).Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(((BlockEntity)val3).Pos);
					}
					else
					{
						((BlockEntityDisplay)val3).updateMeshes();
						((BlockEntity)val3).MarkDirty(true, (IPlayer)null);
					}
				}
				else
				{
					BlockEntity blockEntity2 = slot.BlockEntity;
					if (blockEntity2 != null)
					{
						blockEntity2.MarkDirty(true, (IPlayer)null);
					}
				}
				if (sum.TryGetValue(slot.Code, out (int, EnumItemClass) value))
				{
					int num3 = value.Item1 - num2;
					if (num3 <= 0)
					{
						sum.Remove(slot.Code);
					}
					else
					{
						sum[slot.Code] = (num3, value.Item2);
					}
				}
			}
			if (quantity > 0)
			{
				result = false;
			}
		}
		return result;
	}

	private List<ItemStack> PreviewVariant(List<SlotRef> slots, CraftIngredientList variant, Dictionary<string, int> playerCounts)
	{
		List<ItemStack> list = new List<ItemStack>();
		Dictionary<string, int> counts = ((playerCounts != null) ? new Dictionary<string, int>(playerCounts) : new Dictionary<string, int>());
		foreach (CraftIngredient ingredient in variant.Ingredients)
		{
			int quantity = ingredient.Quantity;
			if (quantity <= 0)
			{
				continue;
			}
			quantity = DeductFrom(counts, ingredient, quantity);
			if (quantity <= 0)
			{
				continue;
			}
			foreach (SlotRef slot in slots)
			{
				if (quantity <= 0)
				{
					break;
				}
				if (SlotMatches(slot, ingredient))
				{
					int num = Math.Min(quantity, slot.Slot.StackSize);
					ItemStack val = slot.Slot.Itemstack.Clone();
					val.StackSize = num;
					list.Add(val);
					quantity -= num;
				}
			}
		}
		return list;
	}

	private int DeductFrom(Dictionary<string, int> counts, CraftIngredient ing, int need)
	{
		//IL_00ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f3: Expected O, but got Unknown
		//IL_00b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c0: Expected O, but got Unknown
		if (counts == null || counts.Count == 0 || need <= 0)
		{
			return need;
		}
		if (ing.Codes != null && ing.Codes.Count > 0)
		{
			foreach (string code in ing.Codes)
			{
				if (need <= 0)
				{
					break;
				}
				if (counts.TryGetValue(code, out var value) && value > 0)
				{
					int num = Math.Min(need, value);
					value -= num;
					if (value <= 0)
					{
						counts.Remove(code);
					}
					else
					{
						counts[code] = value;
					}
					need -= num;
				}
			}
		}
		else if (ing.IsWildcard && !string.IsNullOrEmpty(ing.PatternCode))
		{
			AssetLocation val = new AssetLocation(ing.PatternCode);
			foreach (KeyValuePair<string, int> item in counts.ToList())
			{
				if (need <= 0)
				{
					break;
				}
				try
				{
					AssetLocation val2 = new AssetLocation(item.Key);
					string[] array = ((ing.Allowed != null && ing.Allowed.Count > 0) ? ing.Allowed.ToArray() : null);
					if (!WildcardUtil.Match(val, val2, array))
					{
						continue;
					}
				}
				catch
				{
					continue;
				}
				int num2 = Math.Min(need, item.Value);
				int num3 = item.Value - num2;
				if (num3 <= 0)
				{
					counts.Remove(item.Key);
				}
				else
				{
					counts[item.Key] = num3;
				}
				need -= num2;
			}
		}
		return need;
	}

	private bool HasInventorySpace(IServerPlayer player, List<ItemStack> items)
	{
		if (items == null || items.Count == 0)
		{
			return true;
		}
		Dictionary<string, (ItemStack, int)> dictionary = new Dictionary<string, (ItemStack, int)>();
		foreach (ItemStack item in items)
		{
			if (!(((item == null) ? null : ((RegistryObject)(item.Collectible?)).Code) == (AssetLocation)null))
			{
				string key = ((object)((RegistryObject)item.Collectible).Code).ToString();
				if (dictionary.TryGetValue(key, out var value))
				{
					dictionary[key] = (value.Item1, value.Item2 + item.StackSize);
				}
				else
				{
					dictionary[key] = (item.Clone(), item.StackSize);
				}
			}
		}
		int num = 0;
		IInventory[] obj = new IInventory[3]
		{
			((IPlayer)player).InventoryManager.GetOwnInventory("hotbar"),
			((IPlayer)player).InventoryManager.GetOwnInventory("craftinggrid"),
			((IPlayer)player).InventoryManager.GetOwnInventory("backpack")
		};
		HashSet<ItemSlot> seenSlotRefs = new HashSet<ItemSlot>();
		HashSet<ItemStack> seenStackRefs = new HashSet<ItemStack>();
		HashSet<string> seenKeys = new HashSet<string>();
		IInventory[] array = (IInventory[])(object)obj;
		foreach (IInventory val in array)
		{
			if (val == null)
			{
				continue;
			}
			for (int j = 0; j < ((IReadOnlyCollection<ItemSlot>)val).Count; j++)
			{
				ItemSlot val2 = val[j];
				if (Skip(val, j, val2))
				{
					continue;
				}
				ItemStack itemstack = val2.Itemstack;
				if (itemstack == null)
				{
					num++;
					continue;
				}
				string text = ((object)((RegistryObject)(itemstack.Collectible?)).Code)?.ToString();
				if (text == null || !dictionary.TryGetValue(text, out var value2))
				{
					continue;
				}
				int mergableQuantity = itemstack.Collectible.GetMergableQuantity(itemstack, value2.Item1, (EnumMergePriority)0);
				if (mergableQuantity > 0)
				{
					int num2 = Math.Min(mergableQuantity, value2.Item2);
					value2.Item2 -= num2;
					if (value2.Item2 <= 0)
					{
						dictionary.Remove(text);
					}
					else
					{
						dictionary[text] = value2;
					}
				}
			}
		}
		int num3 = 0;
		foreach (var value3 in dictionary.Values)
		{
			int maxStackSize = value3.Item1.Collectible.MaxStackSize;
			num3 += (value3.Item2 + maxStackSize - 1) / maxStackSize;
		}
		return num3 <= num;
		bool Skip(IInventory inv, int value3, ItemSlot slot)
		{
			bool result = false;
			if (slot != null && !seenSlotRefs.Add(slot))
			{
				result = true;
			}
			ItemStack val3 = ((slot != null) ? slot.Itemstack : null);
			if (val3 != null && !seenStackRefs.Add(val3))
			{
				result = true;
			}
			string text2 = null;
			try
			{
				text2 = $"{((inv != null) ? inv.InventoryID : null)}:{value3}";
			}
			catch
			{
			}
			if (text2 != null && !seenKeys.Add(text2))
			{
				result = true;
			}
			return result;
		}
	}

	private void OnScanRequest(IServerPlayer fromPlayer, CraftScanRequest req)
	{
		//IL_0160: Unknown result type (might be due to invalid IL or missing references)
		//IL_0166: Invalid comparison between Unknown and I4
		//IL_0173: Unknown result type (might be due to invalid IL or missing references)
		//IL_017b: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_0198: Unknown result type (might be due to invalid IL or missing references)
		//IL_017b: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f3: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f9: Invalid comparison between Unknown and I4
		//IL_0306: Unknown result type (might be due to invalid IL or missing references)
		//IL_030e: Unknown result type (might be due to invalid IL or missing references)
		//IL_033f: Unknown result type (might be due to invalid IL or missing references)
		//IL_032b: Unknown result type (might be due to invalid IL or missing references)
		//IL_030e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0363: Unknown result type (might be due to invalid IL or missing references)
		//IL_0365: Unknown result type (might be due to invalid IL or missing references)
		BlockPos asBlockPos = ((Entity)((IPlayer)fromPlayer).Entity).Pos.AsBlockPos;
		IBlockAccessor blockAccessor = ((IWorldAccessor)sapi.World).BlockAccessor;
		int num = Math.Max(0, req.Radius);
		bool flag = req.CollectItems && req.Variants != null && req.Variants.Count > 0;
		Dictionary<string, (int, EnumItemClass)> dictionary = new Dictionary<string, (int, EnumItemClass)>();
		List<SlotRef> list = new List<SlotRef>();
		Dictionary<string, int> dictionary2 = new Dictionary<string, int>();
		HashSet<ItemSlot> seenSlotRefs = new HashSet<ItemSlot>();
		HashSet<ItemStack> seenStackRefs = new HashSet<ItemStack>();
		HashSet<string> seenKeys = new HashSet<string>();
		IInventory[] array = (IInventory[])(object)new IInventory[3]
		{
			((IPlayer)fromPlayer).InventoryManager.GetOwnInventory("hotbar"),
			((IPlayer)fromPlayer).InventoryManager.GetOwnInventory("craftinggrid"),
			((IPlayer)fromPlayer).InventoryManager.GetOwnInventory("backpack")
		};
		foreach (IInventory val in array)
		{
			if (val == null)
			{
				continue;
			}
			for (int j = 0; j < ((IReadOnlyCollection<ItemSlot>)val).Count; j++)
			{
				ItemSlot val2 = val[j];
				if (IsDuplicate(val, j, val2))
				{
					continue;
				}
				ItemStack val3 = ((val2 != null) ? val2.Itemstack : null);
				if (!(((val3 == null) ? null : ((RegistryObject)(val3.Collectible?)).Code) == (AssetLocation)null))
				{
					string key = ((object)((RegistryObject)val3.Collectible).Code).ToString();
					int num2 = Math.Max(1, val3.StackSize);
					EnumItemClass item = (EnumItemClass)(((int)val3.Class != 1 || val3.Block == null) ? ((int)val3.Class) : 0);
					if (dictionary.TryGetValue(key, out var value))
					{
						dictionary[key] = (value.Item1 + num2, item);
					}
					else
					{
						dictionary[key] = (num2, item);
					}
					if (dictionary2.TryGetValue(key, out var value2))
					{
						dictionary2[key] = value2 + num2;
					}
					else
					{
						dictionary2[key] = num2;
					}
				}
			}
		}
		ModSystemBlockReinforcement modSystem = ((ICoreAPI)sapi).ModLoader.GetModSystem<ModSystemBlockReinforcement>(true);
		for (int k = -num; k <= num; k++)
		{
			for (int l = -1; l <= 2; l++)
			{
				for (int m = -num; m <= num; m++)
				{
					BlockEntity blockEntity = blockAccessor.GetBlockEntity(asBlockPos.AddCopy(k, l, m));
					if (blockEntity == null || (modSystem != null && modSystem.IsLockedForInteract(blockEntity.Pos, (IPlayer)(object)fromPlayer)))
					{
						continue;
					}
					IInventory val4 = ShowCraftableSystem.TryGetInventoryFromBE(blockEntity);
					if (val4 == null)
					{
						continue;
					}
					for (int n = 0; n < ((IReadOnlyCollection<ItemSlot>)val4).Count; n++)
					{
						ItemSlot val5 = val4[n];
						if (IsDuplicate(val4, n, val5))
						{
							continue;
						}
						ItemStack val6 = ((val5 != null) ? val5.Itemstack : null);
						if (!(((val6 == null) ? null : ((RegistryObject)(val6.Collectible?)).Code) == (AssetLocation)null))
						{
							string text = ((object)((RegistryObject)val6.Collectible).Code).ToString();
							int num3 = Math.Max(1, val6.StackSize);
							EnumItemClass val7 = (EnumItemClass)(((int)val6.Class != 1 || val6.Block == null) ? ((int)val6.Class) : 0);
							if (dictionary.TryGetValue(text, out var value3))
							{
								dictionary[text] = (value3.Item1 + num3, val7);
							}
							else
							{
								dictionary[text] = (num3, val7);
							}
							list.Add(new SlotRef
							{
								Slot = val5,
								Code = text,
								Class = val7,
								BlockEntity = blockEntity
							});
						}
					}
				}
			}
		}
		if (flag)
		{
			Dictionary<string, int> dictionary3 = dictionary.ToDictionary<KeyValuePair<string, (int, EnumItemClass)>, string, int>((KeyValuePair<string, (int count, EnumItemClass cls)> kv) => kv.Key, (KeyValuePair<string, (int count, EnumItemClass cls)> kv) => kv.Value.count);
			bool flag2 = false;
			bool flag3 = false;
			bool flag4 = false;
			foreach (CraftIngredientList variant in req.Variants)
			{
				Dictionary<string, int> dictionary4;
				Dictionary<string, int> playerCounts;
				if (CanSatisfyVariant(dictionary2, variant))
				{
					dictionary4 = new Dictionary<string, int>(dictionary3);
					foreach (KeyValuePair<string, int> item2 in dictionary2)
					{
						if (dictionary4.TryGetValue(item2.Key, out var value4))
						{
							int num4 = value4 - item2.Value;
							if (num4 <= 0)
							{
								dictionary4.Remove(item2.Key);
							}
							else
							{
								dictionary4[item2.Key] = num4;
							}
						}
					}
					playerCounts = null;
				}
				else
				{
					dictionary4 = dictionary3;
					playerCounts = dictionary2;
				}
				if (!CanSatisfyVariant(dictionary4, variant))
				{
					continue;
				}
				List<ItemStack> items = PreviewVariant(list, variant, playerCounts);
				if (!HasInventorySpace(fromPlayer, items))
				{
					flag3 = true;
					break;
				}
				bool num5 = ExecuteVariant(list, variant, fromPlayer, dictionary, playerCounts);
				flag2 = true;
				if (!num5)
				{
					flag4 = true;
				}
				break;
			}
			if (!flag2)
			{
				if (flag3)
				{
					string text2 = "[ShowCraftable] Not enough inventory space to fetch the ingredients!";
					fromPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, text2, (EnumChatType)1, (string)null);
				}
				else
				{
					string text3 = "[ShowCraftable] Could not find required ingredients to fetch.";
					fromPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, text3, (EnumChatType)1, (string)null);
				}
			}
			else if (flag4)
			{
				fromPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, "[ShowCraftable] Could not get all ingredients, your inventory is full!", (EnumChatType)1, (string)null);
			}
		}
		CraftScanReply craftScanReply = new CraftScanReply
		{
			Codes = dictionary.Keys.ToList(),
			Counts = dictionary.Values.Select<(int, EnumItemClass), int>(((int count, EnumItemClass cls) v) => v.count).ToList(),
			Classes = dictionary.Values.Select<(int, EnumItemClass), EnumItemClass>(((int count, EnumItemClass cls) v) => v.cls).ToList(),
			ScanId = req.ScanId,
			TabKey = req.TabKey,
			IsFetch = flag
		};
		sapi.Network.GetChannel("showcraftablescan").SendPacket<CraftScanReply>(craftScanReply, (IServerPlayer[])(object)new IServerPlayer[1] { fromPlayer });
		bool IsDuplicate(IInventory inv, int index, ItemSlot slot)
		{
			bool result = false;
			if (slot != null && !seenSlotRefs.Add(slot))
			{
				result = true;
			}
			ItemStack val8 = ((slot != null) ? slot.Itemstack : null);
			if (val8 != null && !seenStackRefs.Add(val8))
			{
				result = true;
			}
			string text4 = null;
			try
			{
				text4 = $"{((inv != null) ? inv.InventoryID : null)}:{index}";
			}
			catch
			{
			}
			if (text4 != null && !seenKeys.Add(text4))
			{
				result = true;
			}
			return result;
		}
	}
}
