using System.Collections.Generic;
using ProtoBuf;

namespace ShowCraftable;

[ProtoContract]
public class CraftIngredientList
{
	[ProtoMember(1)]
	public List<CraftIngredient> Ingredients { get; set; } = new List<CraftIngredient>();
}
