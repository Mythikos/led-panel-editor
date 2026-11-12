using System.Text.Json;

using LedPanel.Core.Messages;
using LedPanel.Core.Persistence;
using LedPanel.Core.Tests.Support;

namespace LedPanel.Core.Tests.Messages;

[Trait("Area", "Protocol")]
public sealed class MessageCodecTests
{

	[Theory]
	[MemberData(nameof(ReferenceFixtures.Cases), MemberType = typeof(ReferenceFixtures))]
	public void EveryEditorSettingMatchesReferenceBytesAndRoundTrips(int index, string json)
	{
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement example = document.RootElement;
		MessageSettings settings = example.GetProperty("settings").Deserialize<MessageSettings>(JsonStorage.Options) ?? throw new InvalidDataException();
		byte[] expected = Convert.FromHexString(example.GetProperty("payload_hex").GetString() ?? "");
		Assert.Equal(expected, MessageCodec.Build(ReferenceFixtures.Template, settings));
		Assert.Equal(settings, MessageCodec.Parse(expected));
		Assert.True(index >= 0, "Parity cases have stable identifiers.");
	}

	[Theory]
	[InlineData("")]
	[InlineData("newline\ninjection")]
	[InlineData("\u0004")]
	[InlineData("Ã©")]
	public void UnsupportedTextIsRejected(string text) => Assert.Throws<InvalidDataException>(() => MessageCodec.Build(ReferenceFixtures.Template, new() { Text = text }));

	[Fact]
	public void TruncatedControlsAndMessagesAreRejected()
	{
		byte[] template = ReferenceFixtures.Template;
		int terminator = Array.IndexOf(template, (byte)4);
		for (int length = 0; length <= terminator; length++)
		{
			Assert.Throws<InvalidDataException>(() => MessageCodec.Parse(template[..length]));
		}
	}
}
