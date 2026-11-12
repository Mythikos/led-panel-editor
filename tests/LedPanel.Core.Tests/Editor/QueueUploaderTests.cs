using LedPanel.Core.Devices;
using LedPanel.Core.Editor;
using LedPanel.Core.Messages;
using LedPanel.Core.Tests.Support;

namespace LedPanel.Core.Tests.Editor;

[Trait("Area", "Uploads")]
public sealed class QueueUploaderTests : IDisposable
{
	private readonly string _directory = Path.Combine(Path.GetTempPath(), "ledpanel-tests-" + Guid.NewGuid().ToString("N"));
	public QueueUploaderTests() => Directory.CreateDirectory(_directory);
	public void Dispose() => Directory.Delete(_directory, true);

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task AllFilesAreVerifiedBeforeOneSwitchAndFirstMessageStartsWithoutReplay(bool lostAcknowledgment)
	{
		using var panel = new SimulatedPanel { DropWriteAcknowledgment = lostAcknowledgment };
		byte[] original = panel.Files[@"D:\T\ED00000.Nmg"].ToArray();
		byte[][] payloads = [MessageCodec.Build(ReferenceFixtures.Template, new() { Text = "First" }), MessageCodec.Build(ReferenceFixtures.Template, new() { Text = new string('A', 600) })];
		byte[] playlist = await new QueueUploader(panel, _directory, TimeProvider.System).UploadAsync(payloads, panel.Files["SEQUENT.SYS"].ToArray(), TestContext.Current.CancellationToken);
		PlaylistEntry[] entries = PlaylistCodec.Decode(playlist);
		Assert.Equal(3, panel.Writes.Count);
		Assert.Equal("SEQUENT.SYS", panel.Writes.Last());
		Assert.Equal(entries[0].Path, panel.ActivePath);
		Assert.Equal(payloads[0], panel.Files[entries[0].Path]);
		Assert.Equal(payloads[1], panel.Files[entries[1].Path]);
		Assert.Equal(original, panel.Files[@"D:\T\ED00000.Nmg"]);
		Assert.DoesNotContain(panel.Commands, command => command.Main == 6 && command.Sub != 13);
	}

	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	public async Task StagingFailureKeepsOriginalQueue(int message)
	{
		using var panel = new SimulatedPanel { FailMessageNumber = message };
		byte[] previous = panel.Files["SEQUENT.SYS"].ToArray();
		var uploader = new QueueUploader(panel, _directory, TimeProvider.System);
		await Assert.ThrowsAsync<IOException>(() => uploader.UploadAsync(["First"u8.ToArray(), "Second"u8.ToArray()], previous, TestContext.Current.CancellationToken));
		Assert.Equal(previous, panel.Files["SEQUENT.SYS"]);
		Assert.DoesNotContain("SEQUENT.SYS", panel.Writes);
	}

	[Fact]
	public async Task ExternalChangeDuringStagingPreventsPlaylistOverwrite()
	{
		using var panel = new SimulatedPanel { ChangePlaylistOnStaging = true };
		var uploader = new QueueUploader(panel, _directory, TimeProvider.System);
		await Assert.ThrowsAsync<InvalidDataException>(() => uploader.UploadAsync(["First"u8.ToArray()], panel.Files["SEQUENT.SYS"].ToArray(), TestContext.Current.CancellationToken));
		Assert.DoesNotContain("SEQUENT.SYS", panel.Writes);
	}

	[Fact]
	public async Task CorruptCurrentPlaylistCanBeRestoredWithoutChangingOriginalMessage()
	{
		using var panel = new SimulatedPanel();
		var original = panel.Files.Where(pair => pair.Key is "SEQUENT.SYS" or "CONFIG.SYS" || pair.Key.EndsWith(".Nmg", StringComparison.Ordinal))
			.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
		panel.Files["SEQUENT.SYS"] = "broken"u8.ToArray();
		await new QueueUploader(panel, _directory, TimeProvider.System).RestoreAsync(original, TestContext.Current.CancellationToken);
		Assert.Equal(new[] { "SEQUENT.SYS" }, panel.Writes);
		Assert.Equal(original[@"D:\T\ED00000.Nmg"], panel.Files[@"D:\T\ED00000.Nmg"]);
		Assert.Equal(@"D:\T\ED00000.Nmg", panel.ActivePath);
	}

	[Fact]
	public async Task TemporaryPlaybackTimeoutNeverWritesFiles()
	{
		using var panel = new SimulatedPanel { Temporary = true };
		var uploader = new QueueUploader(panel, _directory, new ExpiredTime());
		await Assert.ThrowsAsync<TimeoutException>(() => uploader.UploadAsync(["First"u8.ToArray()], panel.Files["SEQUENT.SYS"], TestContext.Current.CancellationToken));
		Assert.Empty(panel.Writes);
	}

	[Fact]
	public async Task SecondMessageDoesNotSatisfyFirstMessageVerification()
	{
		using var panel = new SimulatedPanel { ActivePath = @"D:\T\ED22222.Nmg" };
		await Assert.ThrowsAsync<InvalidDataException>(() => new PanelPlayback(panel, new ExpiredTime()).WaitFirstAsync(@"D:\T\ED11111.Nmg", TestContext.Current.CancellationToken));
	}

	private sealed class ExpiredTime : TimeProvider
	{
		private long _timestamp;
		public override long TimestampFrequency => 1;
		public override long GetTimestamp() => Interlocked.Add(ref _timestamp, 60);
	}
}
