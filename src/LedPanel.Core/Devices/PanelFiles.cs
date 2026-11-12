using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

using LedPanel.Core.Messages;
using LedPanel.Core.Protocol;
using LedPanel.Core.Transport;

namespace LedPanel.Core.Devices;

internal sealed class PanelFiles(IPanelConnection connection)
{
	internal async Task<byte[]> ReadAsync(string name, byte kind, CancellationToken cancellationToken)
	{
		if (name.Length is < 1 or > 255 || name.Any(character => character is < ' ' or > '~') || kind is not (2 or 3 or 8)
			|| kind == 2 && name is not ("CONFIG.SYS" or "SEQUENT.SYS")
			|| kind == 3 && !Regex.IsMatch(name, @"\A(?:FONTLIST\.LST|[A-Za-z0-9_-]{1,8}\.[Ff][Nn][Tt])\z"))
		{
			throw new InvalidDataException("Invalid read path or file kind.");
		}

		using var stream = new MemoryStream();
		int? total = null;
		for (int packet = 1; packet <= 2049; packet++)
		{
			byte[] arguments = kind is 2 or 3 ? new byte[16] : new byte[(4 + name.Length + 1 + 3) / 4 * 4];
			int offset = kind is 2 or 3 ? 12 : 0;
			Encoding.ASCII.GetBytes(name, arguments.AsSpan(kind is 2 or 3 ? 0 : 4));
			BinaryPrimitives.WriteUInt16LittleEndian(arguments.AsSpan(offset), 512);
			BinaryPrimitives.WriteUInt16LittleEndian(arguments.AsSpan(offset + 2), (ushort)packet);
			PanelFrame reply = await connection.RequestAsync(1, kind, arguments, [], cancellationToken);
			if (reply.Status is not null || reply.Arguments.Length != 8)
			{
				throw new InvalidDataException("Missing file metadata.");
			}

			uint large = BinaryPrimitives.ReadUInt32LittleEndian(reply.Arguments.AsSpan(4));
			uint size = large == 0 ? BinaryPrimitives.ReadUInt16LittleEndian(reply.Arguments) : large;
			if (size > 1024 * 1024 || BinaryPrimitives.ReadUInt16LittleEndian(reply.Arguments.AsSpan(2)) != packet
				|| total is not null && total != size || reply.Data.Length != Math.Min(512, (long)size - stream.Length))
			{
				throw new InvalidDataException("Incomplete, oversized, or changing file transfer.");
			}

			total = (int)size;
			stream.Write(reply.Data);
			if (stream.Length == size)
			{
				return stream.ToArray();
			}
		}

		throw new InvalidDataException("File transfer exceeded its packet limit.");
	}

	internal async Task WriteAsync(string name, byte[] data, bool system, CancellationToken cancellationToken)
	{
		if (system ? name != "SEQUENT.SYS" || PlaylistCodec.Decode(data).Length == 0 : !PlaylistCodec.IsStagedPath(name))
		{
			throw new InvalidDataException("Only a new editor file or a verified playlist may be written.");
		}

		if (data.Length is < 1 or > 65535)
		{
			throw new InvalidDataException("Invalid upload size.");
		}

		int count = (data.Length + 511) / 512;
		for (int packet = 1; packet <= count; packet++)
		{
			byte[] arguments = system ? new byte[24] : new byte[(10 + name.Length + 1 + 3) / 4 * 4];
			int offset = system ? 12 : 0;
			Encoding.ASCII.GetBytes(name, arguments.AsSpan(system ? 0 : 10));
			BinaryPrimitives.WriteUInt32LittleEndian(arguments.AsSpan(offset), (uint)data.Length);
			BinaryPrimitives.WriteUInt16LittleEndian(arguments.AsSpan(offset + 4), 512);
			BinaryPrimitives.WriteUInt16LittleEndian(arguments.AsSpan(offset + 6), (ushort)count);
			BinaryPrimitives.WriteUInt16LittleEndian(arguments.AsSpan(offset + 8), (ushort)packet);
			byte[] chunk = data.AsSpan((packet - 1) * 512, Math.Min(512, data.Length - (packet - 1) * 512)).ToArray();
			try
			{
				PanelFrame reply = await connection.RequestAsync(2, system ? (byte)2 : (byte)8, arguments, chunk, cancellationToken);
				if (reply.Status != 0x9000)
				{
					throw new InvalidDataException("Unexpected write acknowledgment.");
				}
			}
			catch (TimeoutException)
			{
				// Never retransmit an uncertain write. Full readback below decides success.
			}
		}

		byte[] readback = await ReadAsync(name, system ? (byte)2 : (byte)8, cancellationToken);
		if (!readback.SequenceEqual(data))
		{
			throw new InvalidDataException("Uploaded file did not match readback. Reload the panel before another upload.");
		}
	}
}
