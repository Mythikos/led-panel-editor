using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

using LedPanel.Core.Protocol;
using LedPanel.Core.Transport;

namespace LedPanel.Core.Tests.Transport;

[Trait("Area", "Transport")]
public sealed class UdpPanelConnectionTests : IDisposable
{
	private readonly string _directory = Path.Combine(Path.GetTempPath(), "ledpanel-udp-" + Guid.NewGuid().ToString("N"));
	public UdpPanelConnectionTests() => Directory.CreateDirectory(_directory);
	public void Dispose() => Directory.Delete(_directory, true);

	[Theory]
	[InlineData(true, 3)]
	[InlineData(false, 1)]
	public async Task OnlyReadsRetryAndEachAttemptHasANewSerial(bool read, int count)
	{
		using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
		using var client = new UdpPanelConnection(IPAddress.Loopback, (IPEndPoint)(server.Client.LocalEndPoint ?? throw new InvalidOperationException()), _directory, TimeSpan.FromMilliseconds(60));
		await Assert.ThrowsAsync<TimeoutException>(() => client.RequestAsync(read ? (byte)1 : (byte)2, read ? (byte)10 : (byte)9, [], [], TestContext.Current.CancellationToken));
		var serials = new HashSet<ushort>();
		while (server.Available > 0)
		{
			UdpReceiveResult packet = await server.ReceiveAsync(TestContext.Current.CancellationToken);
			serials.Add(BinaryPrimitives.ReadUInt16LittleEndian(packet.Buffer.AsSpan(10)));
		}

		Assert.Equal(count, serials.Count);
	}

	[Fact]
	public async Task MatchingResponseIsValidatedAndCredentialsAreNotCaptured()
	{
		using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
		using var client = new UdpPanelConnection(IPAddress.Loopback, (IPEndPoint)(server.Client.LocalEndPoint ?? throw new InvalidOperationException()), _directory);
		Task<PanelFrame> request = client.RequestAsync(10, 1, new byte[20], [], TestContext.Current.CancellationToken);
		UdpReceiveResult received = await server.ReceiveAsync(TestContext.Current.CancellationToken);
		byte[] response = [.. received.Buffer.AsSpan(0, 16), 0, 0x90];
		response[1] = 0xa8;
		response[4] = 2;
		response[14] = 0;
		response[15] = 1;
		BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(2), JetFileProtocol.Checksum(response.AsSpan(4)));
		await server.SendAsync(response, received.RemoteEndPoint, TestContext.Current.CancellationToken);
		Assert.Equal((ushort)0x9000, (await request).Status);
		Assert.Empty(Directory.GetFiles(_directory, "*-request.bin"));
	}
}
