using LedPanel.Core.Devices;
using LedPanel.Core.Editor;
using LedPanel.Core.Messages;
using LedPanel.Core.Persistence;
using LedPanel.Core.Tests.Support;

namespace LedPanel.Core.Tests.Editor;

[Trait("Area", "Editor")]
public sealed class EditorServiceTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "ledpanel-service-" + Guid.NewGuid().ToString("N"));
	public EditorServiceTests() => Directory.CreateDirectory(_root);
	public void Dispose() => Directory.Delete(_root, true);

	[Fact]
	public async Task ConnectPreviewQueueUploadRestoreAndDisconnectPreserveOriginal()
	{
		using var panel = new SimulatedPanel();
		var network = new Adapter();
		var service = Create(panel, network);
		await service.ConnectAsync(TestContext.Current.CancellationToken);
		Assert.True(service.CanUpload, "Verified identity and queue enable uploads.");
		Assert.Equal(4, service.Fonts?.Count);
		string original = Path.Combine(_root, "original");
		Dictionary<string, byte[]> originalBytes = BackupStore.Load(original);
		await service.PreviewAsync(new()
		{
			Text = "Preview"
		}, TestContext.Current.CancellationToken);
		Assert.Empty(panel.Writes);
		Assert.Equal(new byte[] { 30, 0, 0, 0 }, panel.Commands.Last(command => command is { Main: 2, Sub: 9 }).Arguments);
		await service.UploadAsync([new() { Text = "First" }, new() { Text = "Second", Layout = "pages" }], TestContext.Current.CancellationToken);
		Assert.Equal(new[] { "First", "Second" }, service.Queue.Select(message => message.Text));
		Assert.Equal(PlaylistCodec.Decode(panel.Files["SEQUENT.SYS"])[0].Path, panel.ActivePath);
		await service.RestoreAsync(TestContext.Current.CancellationToken);
		Assert.Equal("Hello world!", Assert.Single(service.Queue).Text);
		foreach ((string key, byte[] bytes) in originalBytes)
		{
			Assert.Equal(bytes, BackupStore.Load(original)[key]);
		}

		await service.DisconnectAsync(TestContext.Current.CancellationToken);
		Assert.False(service.Connected, "Disconnect must clear device write eligibility.");
		Assert.False(network.IsReady, "Disconnect must restore the session's adapter settings.");
	}

	[Fact]
	public async Task WrongIdentityPreventsAnyWriteAndClosesNetworkSession()
	{
		using var panel = new SimulatedPanel { Mac = "00:11:22:33:44:66" };
		var network = new Adapter();
		var service = Create(panel, network);
		await Assert.ThrowsAsync<InvalidDataException>(() => service.ConnectAsync(TestContext.Current.CancellationToken));
		Assert.Empty(panel.Writes);
		Assert.False(network.IsReady, "Identity rejection cleans up the adapter session.");
	}

	[Fact]
	public async Task MalformedFontCacheAndUnsupportedFontsDoNotDisableRecoveryOrEditing()
	{
		using var panel = new SimulatedPanel();
		var service = Create(panel, new());
		File.WriteAllText(Path.Combine(_root, "panel-fonts.json"), "[]");
		panel.Files["FONTLIST.LST"] = "broken"u8.ToArray();
		string message = await service.ConnectAsync(TestContext.Current.CancellationToken);
		Assert.Contains("preview unavailable", message);
		Assert.True(service.CanUpload, "Fonts are presentation assets, not a prerequisite for safe message uploads.");
		Assert.True(service.CanRestore, "Original recovery remains available if font acquisition fails.");
	}

	[Fact]
	public async Task ReconnectRetainsLastQueueUntilCompleteReadbackAndRejectsMalformedCurrentPlaylist()
	{
		using var panel = new SimulatedPanel();
		var service = Create(panel, new());
		await service.ConnectAsync(TestContext.Current.CancellationToken);
		panel.Disconnected = true;
		Assert.False(await service.HealthAsync(TestContext.Current.CancellationToken), "Lost device must disable writes.");
		Assert.Equal("Hello world!", Assert.Single(service.Queue).Text);
		panel.Disconnected = false;
		Assert.True(await service.HealthAsync(TestContext.Current.CancellationToken), "Reconnect must reidentify the panel and load its current queue.");
		panel.Files["SEQUENT.SYS"] = "broken"u8.ToArray();
		await service.ConnectAsync(TestContext.Current.CancellationToken);
		Assert.True(service.Connected, "Recovery requires the verified identity even if the playlist is malformed.");
		Assert.False(service.CanUpload, "A malformed current playlist must not be used as an upload baseline.");
		Assert.True(service.CanRestore, "A valid original backup enables recovery.");
		await service.RestoreAsync(TestContext.Current.CancellationToken);
		Assert.Equal("Hello world!", Assert.Single(service.Queue).Text);
	}

	[Fact]
	public async Task InvalidQueueIsRejectedBeforeHardwareAccess()
	{
		using var panel = new SimulatedPanel();
		var service = Create(panel, new());
		await service.ConnectAsync(TestContext.Current.CancellationToken);
		int before = panel.Commands.Count;
		await Assert.ThrowsAsync<InvalidDataException>(() => service.UploadAsync([new(), new() { Text = "" }], TestContext.Current.CancellationToken));
		Assert.Equal(before, panel.Commands.Count);
	}

	private EditorService Create(SimulatedPanel panel, Adapter adapter) => new(_root, new("192.0.2.1", "192.0.2.2", "00:11:22:33:44:55", "fixture"), adapter, _ => panel);

	private sealed class Adapter : IAdapterSession
	{
		public bool IsReady
		{
			get; private set;
		}
		public Task EnsureAsync(CancellationToken cancellationToken)
		{
			IsReady = true;
			return Task.CompletedTask;
		}
		public Task CloseAsync(CancellationToken cancellationToken)
		{
			IsReady = false;
			return Task.CompletedTask;
		}
	}
}
