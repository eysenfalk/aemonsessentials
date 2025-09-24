using System.Collections.Generic;
using ProtoBuf;
using Vintagestory.API.Common;

namespace ShowCraftable;

[ProtoContract]
public class CraftScanReply
{
	[ProtoMember(1)]
	public List<string> Codes { get; set; } = new List<string>();

	[ProtoMember(2)]
	public List<int> Counts { get; set; } = new List<int>();

	[ProtoMember(3)]
	public List<EnumItemClass> Classes { get; set; } = new List<EnumItemClass>();

	[ProtoMember(4)]
	public int ScanId { get; set; }

	[ProtoMember(5)]
	public string TabKey { get; set; }

	[ProtoMember(6)]
	public bool IsFetch { get; set; }
}
