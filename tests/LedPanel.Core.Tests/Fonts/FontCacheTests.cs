using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

using LedPanel.Core.Devices;
using LedPanel.Core.Fonts;
using LedPanel.Core.Tests.Support;

namespace LedPanel.Core.Tests.Fonts;

[Trait("Area", "Fonts")]
public sealed class FontCacheTests : IDisposable
{
	private readonly string _directory = Path.Combine(Path.GetTempPath(), "ledpanel-font-cache-" + Guid.NewGuid().ToString("N"));
	public FontCacheTests() => Directory.CreateDirectory(_directory);
	public void Dispose() => Directory.Delete(_directory, true);

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(2)]
	public async Task CapturedFontsKeepLegacyCacheFormatAndRejectWrongIdentityOrHash(int fault)
	{
		using var panel = new SimulatedPanel();
		using JsonDocument fixture = ReferenceFixtures.Open();
		Dictionary<string, object> fonts = fixture.RootElement.GetProperty("fonts").EnumerateObject().ToDictionary(font => font.Name, font => (object)new
		{
			name = font.Name + ".fnt",
			sha256 = fault == 2 ? new string('0', 64) : Convert.ToHexStringLower(SHA256.HashData(panel.Files[font.Name + ".fnt"])),
			data_hex = Convert.ToHexStringLower(panel.Files[font.Name + ".fnt"])
		});
		string legacyJson = JsonSerializer.Serialize(new
		{
			version = 1,
			mac = fault == 1 ? "00:11:22:33:44:66" : panel.Mac,
			fonts
		});
		string path = Path.Combine(_directory, "panel-fonts.json");
		await File.WriteAllTextAsync(path, legacyJson, TestContext.Current.CancellationToken);
		if (fault != 0)
		{
			Assert.Throws<InvalidDataException>(() => FontCache.Load(path, panel.Mac));
			return;
		}

		IReadOnlyDictionary<string, BitmapFont> existing = FontCache.Load(path, panel.Mac);
		IReadOnlyDictionary<string, CapturedFont> captures = await PanelFontReader.CaptureAsync(new PanelFiles(panel), _directory, TestContext.Current.CancellationToken);
		FontCache.Save(path, panel.Mac, captures);
		Assert.True(JsonNode.DeepEquals(JsonNode.Parse(legacyJson), JsonNode.Parse(File.ReadAllText(path))),
			"Refactoring must preserve the version-1 cache schema and values.");
		IReadOnlyDictionary<string, BitmapFont> saved = FontCache.Load(path, panel.Mac);
		foreach ((string key, BitmapFont font) in existing)
		{
			Assert.Equal(font.Width, saved[key].Width);
			Assert.Equal(font.Height, saved[key].Height);
			Assert.Equal(font.Rows, saved[key].Rows);
			Assert.Equal(panel.Files[key + ".fnt"], File.ReadAllBytes(Path.Combine(_directory, "font-" + key + ".bin")));
		}

		Assert.Empty(panel.Writes);
	}
}
