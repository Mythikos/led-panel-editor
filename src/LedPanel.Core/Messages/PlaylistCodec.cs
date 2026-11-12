using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

using LedPanel.Core.Protocol;

namespace LedPanel.Core.Messages;


internal static class PlaylistCodec
{
	internal static PlaylistEntry[] Decode(byte[] data)
	{
		if (data.Length < 8 || !data.AsSpan(0, 4).SequenceEqual("SQ\x04\0"u8) || data.Length != 8 + 36 * BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4)))
		{
			throw new InvalidDataException("Unsupported or malformed playlist.");
		}

		var entries = new List<PlaylistEntry>();
		for (int offset = 8; offset < data.Length; offset += 36)
		{
			ReadOnlySpan<byte> row = data.AsSpan(offset, 36);
			int terminator = row[24..].IndexOf((byte)0);
			if (row[0] is < 65 or > 90 || row[1] is not (84 or 65) || row[2] != 15 || terminator < 1 || row[(24 + terminator)..].ContainsAnyExcept((byte)0))
			{
				throw new InvalidDataException("Invalid playlist entry.");
			}

			string name = Encoding.ASCII.GetString(row.Slice(24, terminator));
			if (!Regex.IsMatch(name, "^[A-Za-z0-9_-]{1,8}\\.[A-Za-z0-9]{1,3}$"))
			{
				throw new InvalidDataException("Invalid playlist filename.");
			}

			entries.Add(new($"{(char)row[0]}:\\{(char)row[1]}\\{name}", BinaryPrimitives.ReadUInt16LittleEndian(row[22..]), BinaryPrimitives.ReadUInt16LittleEndian(row[20..])));
		}

		return entries.ToArray();
	}

	internal static bool IsStagedPath(string path) => Regex.IsMatch(path, @"\AD:\\T\\ED[0-9A-F]{5}\.Nmg\z");

	internal static byte[] Build(byte[] previous, IReadOnlyList<(string Path, byte[] Data)> messages)
	{
		if (Decode(previous).Length == 0 || messages.Count is < 1 or > MessageSettings.MaximumMessages || messages.Select(item => item.Path).Distinct().Count() != messages.Count)
		{
			throw new InvalidDataException("Invalid queue size or duplicate message paths.");
		}

		byte[] result = new byte[8 + 36 * messages.Count];
		previous.AsSpan(0, 8).CopyTo(result);
		BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), (ushort)messages.Count);
		for (int index = 0; index < messages.Count; index++)
		{
			(string path, byte[] data) = messages[index];
			if (!IsStagedPath(path) || data.Length is < 1 or > 65535)
			{
				throw new InvalidDataException("Invalid staged message.");
			}

			Span<byte> row = result.AsSpan(8 + index * 36, 36);
			previous.AsSpan(8, 36).CopyTo(row);
			"DT\x0f"u8.CopyTo(row);
			row[3] = 0x80;
			BinaryPrimitives.WriteUInt16LittleEndian(row[20..], JetFileProtocol.Checksum(data));
			BinaryPrimitives.WriteUInt16LittleEndian(row[22..], (ushort)data.Length);
			row[24..].Clear();
			Encoding.ASCII.GetBytes(path.AsSpan(path.LastIndexOf('\\') + 1), row[24..]);
		}

		return result;
	}
}
