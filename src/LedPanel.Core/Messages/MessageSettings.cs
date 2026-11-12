namespace LedPanel.Core.Messages;

internal sealed record MessageSettings
{
	internal const int MaximumTextLength = 800;
	internal const int MaximumMessages = 20;
	public string Text { get; init; } = "Hello world!";
	public string Color { get; init; } = "red";
	public string Font { get; init; } = "bold";
	public string Entry { get; init; } = "move-left";
	public string Exit { get; init; } = "move-left";
	public int Speed { get; init; } = 3;
	public int Pause
	{
		get; init;
	}
	public string Align { get; init; } = "left";
	public bool Flash
	{
		get; init;
	}
	public string Layout { get; init; } = "line";

	internal string? ValidationError()
	{
		if (string.IsNullOrEmpty(Text) || Text.Length > MaximumTextLength || Text.Any(character => character is < ' ' or > '~'))
		{
			return "Use 1–800 printable ASCII characters.";
		}

		if (!MessageCodec.Colors.ContainsKey(Color) || !MessageCodec.Fonts.ContainsKey(Font) || !MessageCodec.Effects.ContainsKey(Entry)
			|| !MessageCodec.Effects.ContainsKey(Exit) || !MessageCodec.Alignments.ContainsKey(Align) || !MessageCodec.Layouts.ContainsKey(Layout))
		{
			return "Unsupported display setting.";
		}

		return Speed is < 0 or > 6 || Pause is < 0 or > 99 ? "Speed must be 0–6; pause must be 0–99 seconds." : null;
	}

	internal void Validate()
	{
		if (ValidationError() is string error)
		{
			throw new InvalidDataException(error);
		}
	}
}
