using LedPanel.Core.Protocol;

namespace LedPanel.Core.Tests.Protocol;

[Trait("Area", "Protocol")]
public sealed class JetFileProtocolTests
{
	[Fact]
	public void CapturedReplyAndEveryTruncationAreHandled()
	{
		byte[] capture = Convert.FromHexString("55a80b05000000000000cdab010a04000a9701022303ffff8000100005250101");
		Assert.Equal(16, JetFileProtocol.Decode(capture).Arguments.Length);
		for (int length = 0; length < capture.Length; length++)
		{
			Assert.Throws<InvalidDataException>(() => JetFileProtocol.Decode(capture[..length]));
		}

		capture[^1] ^= 1;
		Assert.Throws<InvalidDataException>(() => JetFileProtocol.Decode(capture));
	}
}
