using System.IO;
using System.Text.Json;

using LedPanel.Core.Editor;
using LedPanel.Core.Fonts;
using LedPanel.Core.Messages;

namespace LedPanel.Desktop.Tests.Support;

internal sealed class FakeEditorService : IEditorService
{
	public bool Connected
	{
		get; private set;
	}
	public bool CanUpload => Connected;
	public bool CanRestore => true;
	public IReadOnlyList<MessageSettings> Queue { get; private set; } = [new() { Text = "A AA AAAAA", Layout = "pages", Entry = "appear", Exit = "move-up", Pause = 3 }];
	public IReadOnlyDictionary<string, BitmapFont>? Fonts
	{
		get;
	}
	internal MessageSettings? Preview
	{
		get; private set;
	}
	internal int UploadCount
	{
		get; private set;
	}
	internal TaskCompletionSource? UploadGate
	{
		get; set;
	}
	internal bool FailUpload
	{
		get; set;
	}
	internal bool DisposedConnection
	{
		get; private set;
	}

	internal FakeEditorService()
	{
		using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "reference.json")));
		Fonts = BitmapFont.Profiles.ToDictionary(pair => pair.Key, pair => BitmapFont.Decode(
			Convert.FromHexString(fixture.RootElement.GetProperty("fonts").GetProperty(pair.Key).GetProperty("data_hex").GetString() ?? ""), pair.Value.Width, pair.Value.Height));
	}

	public Task<string> ConnectAsync(CancellationToken cancellationToken)
	{
		Connected = true;
		return Task.FromResult("Panel connected");
	}
	public Task<string> PreviewAsync(MessageSettings message, CancellationToken cancellationToken)
	{
		Preview = message;
		return Task.FromResult("30-second preview sent.");
	}
	public async Task<string> UploadAsync(IReadOnlyList<MessageSettings> queue, CancellationToken cancellationToken, IProgress<string>? progress = null)
	{
		UploadCount++;
		if (UploadGate is not null)
		{
			await UploadGate.Task.WaitAsync(cancellationToken);
		}

		if (FailUpload)
		{
			throw new IOException("Simulated lost connection");
		}

		Queue = queue.ToArray();
		return "Queue saved. Playing from #1.";
	}

	public Task<string> RestoreAsync(CancellationToken cancellationToken)
	{
		Queue = [new() { Text = "Original" }];
		return Task.FromResult("Original restored.");
	}
	public Task DisconnectAsync(CancellationToken cancellationToken)
	{
		Connected = false;
		DisposedConnection = true;
		return Task.CompletedTask;
	}
	public Task<bool> HealthAsync(CancellationToken cancellationToken) => Task.FromResult(Connected);
	public void Dispose()
	{
	}
}
