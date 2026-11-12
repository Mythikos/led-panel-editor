using System.Net;
using System.Net.Sockets;

using LedPanel.Core.Protocol;

namespace LedPanel.Core.Transport;

internal sealed class UdpPanelConnection : IPanelConnection
{
	private readonly UdpClient _socket;
	private readonly string _captureDirectory;
	private readonly TimeSpan _timeout;
	private ushort _serial = 0x4000;
	internal byte Group { get; set; } = 1;
	internal byte Unit { get; set; } = 1;

	internal UdpPanelConnection(IPAddress source, IPEndPoint destination, string captureDirectory, TimeSpan? timeout = null)
	{
		_captureDirectory = captureDirectory;
		Directory.CreateDirectory(captureDirectory);
		_timeout = timeout ?? TimeSpan.FromSeconds(2);
		_socket = new UdpClient(new IPEndPoint(source, 0));
		_socket.Connect(destination);
	}

	public async Task<PanelFrame> RequestAsync(byte main, byte sub, byte[] arguments, byte[] data, CancellationToken cancellationToken)
	{
		int attempts = JetFileProtocol.IsRead(main, sub) ? 3 : 1;
		for (int attempt = 0; ; attempt++)
		{
			_serial++;
			byte[] packet = JetFileProtocol.Encode(main, sub, _serial, arguments, data, Group, Unit);
			string stem = Path.Combine(_captureDirectory, $"{_serial:x4}-{main:x2}{sub:x2}");
			if (main != 10 || sub != 1)
			{
				await File.WriteAllBytesAsync(stem + "-request.bin", packet, cancellationToken);
			}

			using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			deadline.CancelAfter(_timeout);
			try
			{
				await _socket.SendAsync(packet, deadline.Token);
				for (int ignored = 0; ignored < 8; ignored++)
				{
					UdpReceiveResult received = await _socket.ReceiveAsync(deadline.Token);
					await File.WriteAllBytesAsync(stem + $"-response-{ignored}.bin", received.Buffer, cancellationToken);
					PanelFrame reply = JetFileProtocol.Decode(received.Buffer);
					bool auth = reply.Serial == 0 && reply.Main == 0 && reply.Sub == 0 && reply.Status == 0x9030;
					bool addresses = reply.Source.SequenceEqual(new byte[] { 0, 0 }) && reply.Destination.SequenceEqual(new[] { Group, Unit })
						|| reply.Destination.SequenceEqual(new byte[] { 0, 0 }) && reply.Source.SequenceEqual(new[] { Group, Unit });
					if (!auth && !addresses)
					{
						throw new InvalidDataException("Response addresses do not match this panel.");
					}

					if (!auth && (reply.Serial != _serial || reply.Main != main || reply.Sub != sub))
					{
						continue;
					}

					if (reply.Status is ushort status && status != 0x9000)
					{
						throw new PanelStatusException(status);
					}

					return reply;
				}

				throw new InvalidDataException("Too many unrelated panel responses.");
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				if (attempt + 1 == attempts)
				{
					throw new TimeoutException("The panel did not reply. A write may still have completed; reload before trying again.");
				}
			}
		}
	}

	public void Dispose() => _socket.Dispose();
}
