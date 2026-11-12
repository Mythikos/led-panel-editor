using LedPanel.Core.Messages;
using LedPanel.Core.Tests.Support;

namespace LedPanel.Core.Tests.Messages;

[Trait("Area", "Messages")]
public sealed class PlaylistCodecTests
{
	[Fact]
	public void PlaylistMatchesReferenceAndPreservesOrderAndSchedule()
	{
		byte[] result = PlaylistCodec.Build(ReferenceFixtures.Playlist, [("D:\\T\\EDABCDE.Nmg", "First"u8.ToArray()), ("D:\\T\\ED12345.Nmg", "Second"u8.ToArray())]);
		Assert.Equal(ReferenceFixtures.Bytes("built_playlist_hex"), result);
		Assert.Equal(new[] { "D:\\T\\EDABCDE.Nmg", "D:\\T\\ED12345.Nmg" }, PlaylistCodec.Decode(result).Select(entry => entry.Path));
		for (int length = 0; length < result.Length; length++)
		{
			Assert.Throws<InvalidDataException>(() => PlaylistCodec.Decode(result[..length]));
		}
	}
}
