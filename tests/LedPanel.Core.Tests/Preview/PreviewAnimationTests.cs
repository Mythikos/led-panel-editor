using System.Text.Json;

using LedPanel.Core.Fonts;
using LedPanel.Core.Messages;
using LedPanel.Core.Persistence;
using LedPanel.Core.Preview;
using LedPanel.Core.Tests.Support;

namespace LedPanel.Core.Tests.Preview;

[Trait("Area", "Preview")]
public sealed class PreviewAnimationTests
{
	[Theory]
	[MemberData(nameof(ReferenceFixtures.Cases), MemberType = typeof(ReferenceFixtures))]
	public void FontsWrappingAndAnimatedFramesMatchReference(int index, string json)
	{
		using JsonDocument document = JsonDocument.Parse(json);
		using JsonDocument reference = ReferenceFixtures.Open();
		JsonElement example = document.RootElement;
		MessageSettings settings = example.GetProperty("settings").Deserialize<MessageSettings>(JsonStorage.Options) ?? throw new InvalidDataException();
		JsonElement source = reference.RootElement.GetProperty("fonts").GetProperty(settings.Font);
		BitmapFont font = BitmapFont.Decode(Convert.FromHexString(source.GetProperty("data_hex").GetString() ?? ""), source.GetProperty("width").GetInt32(), source.GetProperty("height").GetInt32());
		Assert.Equal(source.GetProperty("rows").EnumerateArray().Select(row => row.GetInt32()), font.Rows);
		PreviewLayout layout = PreviewLayout.Create(settings, font);
		Assert.Equal(example.GetProperty("pages").GetArrayLength(), layout.Pages.Count);
		for (int page = 0; page < layout.Pages.Count; page++)
		{
			Assert.Equal(example.GetProperty("pages")[page].GetProperty("sha256").GetString(), ContentHash.Compute(layout.Pages[page].Pixels));
		}

		Assert.Equal(example.GetProperty("duration").GetDouble(), new PreviewAnimation(layout.Settings, layout.Pages[0].Width).Duration, 8);
		foreach (JsonElement frame in example.GetProperty("frames").EnumerateArray())
		{
			Assert.Equal(frame.GetProperty("sha256").GetString(), ContentHash.Compute(PreviewAnimation.Frame(layout, frame.GetProperty("seconds").GetDouble())));
		}

		Assert.True(index >= 0, "Each render parity case is identified independently.");
	}


	[Theory]
	[InlineData(3, 120, 4)]
	[InlineData(6, 140, 7)]
	public void HardwareTimingCalibrationIsPreserved(int speed, int width, double seconds) => Assert.Equal(seconds, new PreviewAnimation(new() { Speed = speed }, width).Duration, 8);
}
