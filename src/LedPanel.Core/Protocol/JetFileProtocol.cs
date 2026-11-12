using System.Buffers.Binary;

namespace LedPanel.Core.Protocol;

internal static class JetFileProtocol
{
	internal const int HeaderSize = 16;
	internal static bool IsRead(byte main, byte sub) => main == 1 && sub is 2 or 3 or 8 or 10 or 11 or 12 || main == 6 && sub == 13;

	internal static ushort Checksum(ReadOnlySpan<byte> bytes)
	{
		int sum = 0;
		foreach (byte value in bytes)
		{
			sum += value;
		}

		return (ushort)sum;
	}

	internal static byte[] Encode(byte main, byte sub, ushort serial, byte[] arguments, byte[] data, byte group, byte unit)
	{
		if (arguments.Length % 4 != 0 || arguments.Length > 1020 || data.Length > ushort.MaxValue || group is 0 or 255 || unit is 0 or 255)
		{
			throw new InvalidDataException("Invalid packet bounds or ambiguous panel address.");
		}

		byte[] packet = new byte[HeaderSize + arguments.Length + data.Length];
		packet[0] = 0x55;
		packet[1] = 0xa7;
		BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4), (ushort)data.Length);
		packet[8] = group;
		packet[9] = unit;
		BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(10), serial);
		packet[12] = main;
		packet[13] = sub;
		packet[14] = (byte)(arguments.Length / 4);
		arguments.CopyTo(packet, HeaderSize);
		data.CopyTo(packet, HeaderSize + arguments.Length);
		BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), Checksum(packet.AsSpan(4)));
		return packet;
	}

	internal static PanelFrame Decode(byte[] packet)
	{
		if (packet.Length < HeaderSize || packet[0] != 0x55 || packet[1] != 0xa8 || packet[15] > 1)
		{
			throw new InvalidDataException("Invalid panel response header.");
		}

		int size = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4));
		int offset = HeaderSize + packet[14] * 4;
		if (packet.Length != offset + size || BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2)) != Checksum(packet.AsSpan(4)) || packet[15] == 1 && size != 2)
		{
			throw new InvalidDataException("Panel response length or checksum mismatch.");
		}

		return new(packet[6..8], packet[8..10], BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(10)), packet[12], packet[13], packet[15], packet[16..offset], packet[offset..]);
	}
}
