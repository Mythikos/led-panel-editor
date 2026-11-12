using System.Buffers.Binary;

namespace LedPanel.Core.Fonts;

internal sealed record BitmapFont(int Width, int Height, int[] Rows)
{
	internal static readonly IReadOnlyDictionary<string, (char Code, int Width, int Height)> Profiles = new Dictionary<string, (char, int, int)>
	{
		["compact"] = ('1', 6, 7),
		["standard"] = ('2', 8, 14),
		["bold"] = ('N', 10, 14),
		["tall"] = ('4', 9, 16)
	}.AsReadOnly();

	internal static BitmapFont Decode(byte[] data, int width, int height)
	{
		int stride = (width + 7) / 8;
		if (width is < 1 or > 16 || height is < 1 or > 16 || data.Length < 127 * height * stride + 64)
		{
			throw new InvalidDataException("Truncated or unsupported font.");
		}

		ReadOnlySpan<byte> trailer = data.AsSpan(data.Length - 64);
		if (!trailer.StartsWith("\u0004FONT"u8) || BinaryPrimitives.ReadUInt32LittleEndian(trailer[16..]) != width
			|| BinaryPrimitives.ReadUInt32LittleEndian(trailer[20..]) != height || BinaryPrimitives.ReadUInt32LittleEndian(trailer[24..]) != stride)
		{
			throw new InvalidDataException("Font trailer does not match its dimensions.");
		}

		int[] rows = new int[95 * height];
		for (int code = 32; code < 127; code++)
		{
			for (int row = 0; row < height; row++)
			{
				int offset = (code * height + row) * stride;
				int bits = stride == 1 ? data[offset] : BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
				rows[(code - 32) * height + row] = bits >> (stride * 8 - width);
			}
		}

		return new(width, height, rows);
	}
}
