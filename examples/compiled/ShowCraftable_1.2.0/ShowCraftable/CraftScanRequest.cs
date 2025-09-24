using System.Collections.Generic;
using ProtoBuf;

namespace ShowCraftable;

[ProtoContract]
public class CraftScanRequest
{
	[ProtoMember(1)]
	public int Radius { get; set; }

	[ProtoMember(2)]
	public bool CollectItems { get; set; }

	[ProtoMember(3)]
	public List<CraftIngredientList> Variants { get; set; } = new List<CraftIngredientList>();

	[ProtoMember(4)]
	public int ScanId { get; set; }

	[ProtoMember(5)]
	public string TabKey { get; set; }
}
