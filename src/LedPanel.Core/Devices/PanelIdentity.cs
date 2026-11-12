using System.Buffers.Binary;
using System.Text;

using LedPanel.Core.Protocol;
using LedPanel.Core.Transport;

namespace LedPanel.Core.Devices;

internal sealed record PanelIdentity(ushort Cpu, ushort Protocol, int Width, int Height, byte Group, byte Unit, string Mac)
{
	internal static async Task<PanelIdentity> ReadAsync(IPanelConnection connection, string expectedMac, CancellationToken cancellationToken)
	{
		PanelFrame hardware = await connection.RequestAsync(1, 10, [], [], cancellationToken);
		PanelFrame identity = await connection.RequestAsync(1, 12, [], [], cancellationToken);
		if (hardware.Status is not null || hardware.Arguments.Length != 16 || identity.Status is not null || identity.Data.Length != 22)
		{
			throw new InvalidDataException("Invalid hardware identity response.");
		}

		string mac = string.Join(":", identity.Data[16..].Select(value => value.ToString("x2")));
		byte[] data = hardware.Arguments;
		var result = new PanelIdentity(BinaryPrimitives.ReadUInt16LittleEndian(data), BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(12)),
			BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(8)), BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(10)), data[14], data[15], mac);
		if (mac != expectedMac || result.Width != 128 || result.Height != 16 || result.Protocol != 0x2505 || result.Group is 0 or 255 || result.Unit is 0 or 255)
		{
			throw new InvalidDataException("Connected panel does not match the selected 128 × 16 JetFile II device.");
		}

		return result;
	}

	internal static async Task<PanelIdentity> AuthenticateAsync(IPanelConnection connection, string expectedMac, CancellationToken cancellationToken)
	{
		try
		{
			return await ReadAsync(connection, expectedMac, cancellationToken);
		}
		catch (Exception error) when (error is TimeoutException || error is PanelStatusException { Status: 0x9030 })
		{
			// Only after the adapter watchdog verified the selected physical MAC.
			// One known legacy login, never guessed passwords or logged credentials.
			byte[] credentials = new byte[20];
			Encoding.ASCII.GetBytes("administrator", credentials);
			Encoding.ASCII.GetBytes("000000", credentials.AsSpan(14));
			try
			{
				await connection.RequestAsync(10, 1, credentials, [], cancellationToken);
			}
			catch (TimeoutException)
			{
				// Old firmware can authenticate without returning an acknowledgment.
			}

			return await ReadAsync(connection, expectedMac, cancellationToken);
		}
	}
}
