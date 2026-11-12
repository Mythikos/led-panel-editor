using System.Text.Json;

namespace LedPanel.Core.Tests.Support;

internal static class ReferenceFixtures
{
	public static IEnumerable<object[]> Cases()
	{
		using JsonDocument document = ReferenceFixtures.Open();
		return document.RootElement.GetProperty("cases").EnumerateArray().Select((item, index) => new object[] { index, item.GetRawText() }).ToArray();
	}

	internal static JsonDocument Open() => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "reference.json")));
	internal static byte[] Bytes(string field)
	{
		using JsonDocument document = Open();
		return Convert.FromHexString(document.RootElement.GetProperty(field).GetString() ?? "");
	}

	internal static byte[] Template => Bytes("template_hex");
	internal static byte[] Playlist => Bytes("playlist_hex");
}
