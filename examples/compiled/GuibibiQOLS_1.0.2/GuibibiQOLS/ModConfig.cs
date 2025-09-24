namespace GuibibiQOLS;

public class ModConfig
{
	public static ModConfig Loaded { get; set; } = new ModConfig();

	public bool NotepadEnabled { get; set; } = true;

	public bool HandbookTweaks { get; set; } = true;
}
