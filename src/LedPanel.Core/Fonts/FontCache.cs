using System.Text.Json;

using LedPanel.Core.Persistence;

namespace LedPanel.Core.Fonts;

internal static class FontCache
{
	internal static IReadOnlyDictionary<string, BitmapFont> Load(string path, string mac)
	{
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
		JsonElement root = document.RootElement;
		if (root.GetProperty("version").GetInt32() != 1 || root.GetProperty("mac").GetString() != mac)
		{
			throw new InvalidDataException("Font cache belongs to a different panel.");
		}

		var fonts = new Dictionary<string, BitmapFont>();
		foreach ((string key, (char _, int width, int height)) in BitmapFont.Profiles)
		{
			JsonElement row = root.GetProperty("fonts").GetProperty(key);
			byte[] data = Convert.FromHexString(row.GetProperty("data_hex").GetString() ?? "");
			if (ContentHash.Compute(data) != row.GetProperty("sha256").GetString())
			{
				throw new InvalidDataException("Font cache integrity check failed.");
			}

			fonts.Add(key, BitmapFont.Decode(data, width, height));
		}

		return fonts.AsReadOnly();
	}

	internal static void Save(string path, string mac, IReadOnlyDictionary<string, CapturedFont> captures)
	{
		JsonStorage.Save(path, new
		{
			Version = 1,
			Mac = mac,
			Fonts = captures.ToDictionary(pair => pair.Key, pair => new
			{
				pair.Value.Name,
				Sha256 = ContentHash.Compute(pair.Value.Data),
				DataHex = Convert.ToHexStringLower(pair.Value.Data)
			})
		});
	}
}
