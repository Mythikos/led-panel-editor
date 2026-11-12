using LedPanel.Core.Fonts;
using LedPanel.Core.Messages;

namespace LedPanel.Core.Preview;

internal sealed record PreviewLayout(MessageSettings Settings, IReadOnlyList<Raster> Pages, bool Overflow)
{
	internal static Raster Rasterize(string text, BitmapFont font)
	{
		if (text.Length > MessageSettings.MaximumTextLength || text.Any(character => character is < ' ' or > '~'))
		{
			throw new InvalidDataException("Unsupported preview text.");
		}

		int width = Math.Max(1, text.Length * font.Width);
		byte[] pixels = new byte[width * 16];
		int top = (16 - font.Height) / 2;
		for (int index = 0; index < text.Length; index++)
		{
			for (int row = 0; row < font.Height; row++)
			{
				int bits = font.Rows[(text[index] - 32) * font.Height + row];
				for (int column = 0; column < font.Width; column++)
				{
					pixels[(row + top) * width + index * font.Width + column] = (byte)((bits >> (font.Width - column - 1)) & 1);
				}
			}
		}

		return new(width, pixels);
	}

	internal static PreviewLayout Create(MessageSettings settings, BitmapFont font)
	{
		Raster lineRaster = Rasterize(settings.Text, font);
		if (settings.Layout != "pages")
		{
			bool overflow = lineRaster.Width > 128;
			string direction = settings.Entry == "move-right" ? "move-right" : "move-left";
			return new(overflow ? settings with
			{
				Entry = direction,
				Exit = direction,
				Pause = 0,
				Align = "left",
				Flash = false
			} : settings, [lineRaster], overflow);
		}

		int columns = 128 / font.Width;
		var lines = new List<string>();
		string line = "";
		foreach (string word in settings.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			if (line.Length > 0 && line.Length + word.Length + 1 <= columns)
			{
				line += " " + word;
				continue;
			}

			if (line.Length > 0)
			{
				lines.Add(line);
			}

			string remainder = word;
			while (remainder.Length > columns)
			{
				lines.Add(remainder[..columns]);
				remainder = remainder[columns..];
			}

			line = remainder;
		}

		if (line.Length > 0 || lines.Count == 0)
		{
			lines.Add(line);
		}

		var pages = new List<Raster>();
		foreach (string[] page in lines.Chunk(Math.Max(1, 17 / (font.Height + 1))))
		{
			byte[] pixels = new byte[128 * 16];
			int top = (16 - (page.Length * font.Height + page.Length - 1)) / 2;
			for (int index = 0; index < page.Length; index++)
			{
				Raster raster = Rasterize(page[index], font);
				int left = settings.Align == "right" ? 128 - raster.Width : settings.Align == "center" ? (128 - raster.Width) / 2 : 0;
				for (int row = 0; row < font.Height; row++)
				{
					Array.Copy(raster.Pixels, ((16 - font.Height) / 2 + row) * raster.Width, pixels, (top + index * (font.Height + 1) + row) * 128 + left, raster.Width);
				}
			}

			pages.Add(new(128, pixels));
		}

		return new(settings, pages, false);
	}
}
