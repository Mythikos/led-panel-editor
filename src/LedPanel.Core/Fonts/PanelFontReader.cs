using System.Buffers.Binary;
using System.Text;

using LedPanel.Core.Devices;

namespace LedPanel.Core.Fonts;

internal static class PanelFontReader
{
	internal static async Task<IReadOnlyDictionary<string, CapturedFont>> CaptureAsync(PanelFiles files, string directory, CancellationToken cancellationToken)
	{
		byte[] listing = await files.ReadAsync("FONTLIST.LST", 3, cancellationToken);
		if (listing.Length < 4 || !listing.AsSpan(0, 2).SequenceEqual("FL"u8))
		{
			throw new InvalidDataException("Unsupported font directory.");
		}

		int count = BinaryPrimitives.ReadUInt16LittleEndian(listing.AsSpan(2));
		if (count is < 1 or > 256 || listing.Length != 4 + count * 28)
		{
			throw new InvalidDataException("Invalid font directory size.");
		}

		var available = new Dictionary<uint, (string Name, uint Size, int Width, int Height, int Stride)>();
		for (int offset = 4; offset < listing.Length; offset += 28)
		{
			string name = Encoding.ASCII.GetString(listing, offset, 12).TrimEnd('\0');
			uint code = BinaryPrimitives.ReadUInt32LittleEndian(listing.AsSpan(offset + 12));
			uint size = BinaryPrimitives.ReadUInt32LittleEndian(listing.AsSpan(offset + 16));
			if (size is < 1 or > 65536 || !available.TryAdd(code, (name, size, listing[offset + 24], listing[offset + 25], listing[offset + 26])))
			{
				throw new InvalidDataException("Duplicate or oversized font directory entry.");
			}
		}

		var captures = new Dictionary<string, CapturedFont>();
		foreach ((string key, (char code, int width, int height)) in BitmapFont.Profiles)
		{
			if (!available.TryGetValue(code, out var entry) || entry.Width != width || entry.Height != height || entry.Stride != (width + 7) / 8)
			{
				throw new InvalidDataException("Panel font profile does not match this editor.");
			}

			byte[] data = await files.ReadAsync(entry.Name, 3, cancellationToken);
			if (data.Length != entry.Size)
			{
				throw new InvalidDataException("Font size differs from directory.");
			}

			BitmapFont.Decode(data, width, height);
			await File.WriteAllBytesAsync(Path.Combine(directory, "font-" + key + ".bin"), data, cancellationToken);
			captures[key] = new(entry.Name, data);
		}

		return captures.AsReadOnly();
	}
}
