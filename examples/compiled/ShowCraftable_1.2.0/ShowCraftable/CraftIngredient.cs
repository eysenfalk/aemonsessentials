using System.Collections.Generic;
using ProtoBuf;
using Vintagestory.API.Common;

namespace ShowCraftable;

[ProtoContract]
public class CraftIngredient
{
	[ProtoMember(1)]
	public bool IsWildcard { get; set; }

	[ProtoMember(2)]
	public int Quantity { get; set; }

	[ProtoMember(3)]
	public List<string> Codes { get; set; } = new List<string>();

	[ProtoMember(4)]
	public string PatternCode { get; set; }

	[ProtoMember(5)]
	public List<string> Allowed { get; set; } = new List<string>();

	[ProtoMember(6)]
	public EnumItemClass Type { get; set; }

	[ProtoMember(7)]
	public bool HasType { get; set; }
}
