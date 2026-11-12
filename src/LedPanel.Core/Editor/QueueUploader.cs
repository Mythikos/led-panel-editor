using System.Security.Cryptography;

using LedPanel.Core.Devices;
using LedPanel.Core.Messages;
using LedPanel.Core.Persistence;
using LedPanel.Core.Protocol;
using LedPanel.Core.Transport;

namespace LedPanel.Core.Editor;

internal sealed class QueueUploader(IPanelConnection connection, string directory, TimeProvider timeProvider)
{
	private readonly PanelFiles _files = new(connection);
	private readonly PanelPlayback _playback = new(connection, timeProvider);

	internal async Task<byte[]> UploadAsync(IReadOnlyList<byte[]> payloads, byte[] expectedPlaylist, CancellationToken cancellationToken, IProgress<string>? progress = null)
	{
		if (payloads.Count is < 1 or > MessageSettings.MaximumMessages || payloads.Any(payload => payload.Length is < 1 or > 1000))
		{
			throw new InvalidDataException("Invalid message queue.");
		}

		await _playback.WaitScheduledAsync(cancellationToken, progress);
		await VerifyPlaylistAsync(expectedPlaylist, cancellationToken);
		BackupManifest backup = await BackupStore.CaptureAsync(_files, directory, cancellationToken);
		if (!(await File.ReadAllBytesAsync(Path.Combine(directory, "sequent.sys"), cancellationToken)).SequenceEqual(expectedPlaylist))
		{
			throw new InvalidDataException("Playlist changed during backup. Reload before uploading.");
		}

		var journal = new Dictionary<string, object> { ["phase"] = "backed_up", ["backup"] = backup, ["payload_sha256"] = payloads.Select(ContentHash.Compute).ToArray() };
		string journalPath = Path.Combine(directory, "transaction.json");
		JsonStorage.Save(journalPath, journal);
		try
		{
			var staged = new List<(string Path, byte[] Data)>();
			foreach (byte[] payload in payloads)
			{
				string path = await AllocatePathAsync(staged.Select(item => item.Path), cancellationToken);
				await File.WriteAllBytesAsync(Path.Combine(directory, $"new-message-{staged.Count:D3}.bin"), payload, cancellationToken);
				journal["phase"] = "writing_message";
				journal["path"] = path;
				journal["message_index"] = staged.Count;
				JsonStorage.Save(journalPath, journal);
				progress?.Report($"Uploading message {staged.Count + 1} of {payloads.Count}…");
				await _files.WriteAsync(path, payload, false, cancellationToken);
				staged.Add((path, payload));
				journal["verified_paths"] = staged.Select(item => item.Path).ToArray();
				JsonStorage.Save(journalPath, journal);
			}

			byte[] playlist = PlaylistCodec.Build(expectedPlaylist, staged);
			await File.WriteAllBytesAsync(Path.Combine(directory, "new-playlist.sys"), playlist, cancellationToken);
			await VerifyPlaylistAsync(expectedPlaylist, cancellationToken);
			journal["phase"] = "switching_playlist";
			JsonStorage.Save(journalPath, journal);
			await _files.WriteAsync("SEQUENT.SYS", playlist, true, cancellationToken);
			journal["phase"] = "playlist_verified";
			journal["activation"] = "playlist_write";
			journal["first_path"] = staged[0].Path;
			JsonStorage.Save(journalPath, journal);
			// The write starts #1. Sending 0601 here advances to #2 on this firmware.
			await _playback.WaitFirstAsync(staged[0].Path, cancellationToken);
			journal["phase"] = "complete";
			JsonStorage.Save(journalPath, journal);
			return playlist;
		}
		catch (Exception error)
		{
			journal["error"] = error.Message;
			JsonStorage.Save(journalPath, journal);
			throw;
		}
	}

	private async Task VerifyPlaylistAsync(byte[] expected, CancellationToken cancellationToken)
	{
		if (!(await _files.ReadAsync("SEQUENT.SYS", 2, cancellationToken)).SequenceEqual(expected))
		{
			throw new InvalidDataException("Panel changed since it was loaded. Reload before uploading.");
		}
	}

	private async Task<string> AllocatePathAsync(IEnumerable<string> staged, CancellationToken cancellationToken)
	{
		for (int attempt = 0; attempt < 8; attempt++)
		{
			string path = @"D:\T\ED" + Convert.ToHexString(RandomNumberGenerator.GetBytes(3))[..5] + ".Nmg";
			if (staged.Contains(path))
			{
				continue;
			}

			try
			{
				await _files.ReadAsync(path, 8, cancellationToken);
			}
			catch (PanelStatusException error) when (error.Status == 0x9008)
			{
				return path;
			}
		}

		throw new IOException("Unable to allocate an unused message filename.");
	}

	internal async Task<byte[]> RestoreAsync(IReadOnlyDictionary<string, byte[]> original, CancellationToken cancellationToken)
	{
		await _playback.WaitScheduledAsync(cancellationToken);
		byte[] playlist = original["SEQUENT.SYS"].ToArray();
		PlaylistEntry[] entries = PlaylistCodec.Decode(playlist);
		if (entries.Length == 0)
		{
			throw new InvalidDataException("Original playlist is empty.");
		}

		foreach (PlaylistEntry entry in entries)
		{
			if (!original.TryGetValue(entry.Path, out byte[]? bytes) || !(await _files.ReadAsync(entry.Path, 8, cancellationToken)).SequenceEqual(bytes))
			{
				throw new InvalidDataException("Original panel file is missing or changed. Restoration stopped.");
			}
		}

		foreach (string name in new[] { "CONFIG.SYS", "SEQUENT.SYS" })
		{
			try
			{
				await File.WriteAllBytesAsync(Path.Combine(directory, "recovery-before-" + name), await _files.ReadAsync(name, 2, cancellationToken), cancellationToken);
			}
			catch (PanelStatusException error) when (name == "SEQUENT.SYS" && error.Status == 0x9008)
			{
				JsonStorage.Save(Path.Combine(directory, "missing-playlist.json"), new
				{
					Missing = name
				});
			}
		}

		for (int offset = 11; offset < playlist.Length; offset += 36)
		{
			playlist[offset] = 0x80;
		}

		string journalPath = Path.Combine(directory, "transaction.json");
		JsonStorage.Save(journalPath, new
		{
			Phase = "restoring_playlist"
		});
		await _files.WriteAsync("SEQUENT.SYS", playlist, true, cancellationToken);
		await _playback.WaitFirstAsync(entries[0].Path, cancellationToken);
		JsonStorage.Save(journalPath, new
		{
			Phase = "complete",
			Activation = "playlist_write",
			RestoredPlaylistHex = Convert.ToHexStringLower(playlist)
		});
		return playlist;
	}
}
