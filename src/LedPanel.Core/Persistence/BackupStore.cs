using System.Text.Json;

using LedPanel.Core.Devices;
using LedPanel.Core.Messages;

namespace LedPanel.Core.Persistence;

internal static class BackupStore
{
	internal static Dictionary<string, byte[]> Load(string directory)
	{
		BackupManifest manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(Path.Combine(directory, "backup.json")), JsonStorage.Options)
			?? throw new InvalidDataException("Missing original backup manifest.");
		if (!manifest.Complete || manifest.Files.Count < 3)
		{
			throw new InvalidDataException("Original backup is incomplete.");
		}

		var files = new Dictionary<string, byte[]>();
		foreach (BackupFile row in manifest.Files)
		{
			if (Path.GetFileName(row.LocalFile) != row.LocalFile || row.LocalFile is "." or ".." || row.LocalFile.Contains(':'))
			{
				throw new InvalidDataException("Invalid local backup filename.");
			}

			byte[] data = File.ReadAllBytes(Path.Combine(directory, row.LocalFile));
			if (data.Length != row.Size || ContentHash.Compute(data) != row.Sha256 || !files.TryAdd(row.PanelPath, data))
			{
				throw new InvalidDataException("Original backup integrity check failed.");
			}
		}

		return files;
	}

	internal static async Task<BackupManifest> CaptureAsync(PanelFiles files, string directory, CancellationToken cancellationToken)
	{
		var rows = new List<BackupFile>();
		string manifestPath = Path.Combine(directory, "backup.json");
		JsonStorage.Save(manifestPath, new BackupManifest(false, rows));
		async Task<byte[]> SaveAsync(string name, string local, byte kind)
		{
			byte[] data = await files.ReadAsync(name, kind, cancellationToken);
			await using var stream = new FileStream(Path.Combine(directory, local), FileMode.CreateNew, FileAccess.Write, FileShare.None);
			await stream.WriteAsync(data, cancellationToken);
			rows.Add(new(name, local, data.Length, ContentHash.Compute(data)));
			JsonStorage.Save(manifestPath, new BackupManifest(false, rows));
			return data;
		}

		await SaveAsync("CONFIG.SYS", "config.sys", 2);
		byte[] playlist = await SaveAsync("SEQUENT.SYS", "sequent.sys", 2);
		PlaylistEntry[] entries = PlaylistCodec.Decode(playlist);
		for (int index = 0; index < entries.Length; index++)
		{
			byte[] data = await SaveAsync(entries[index].Path, $"message-{index:D3}.bin", 8);
			if (data.Length != entries[index].Size)
			{
				throw new InvalidDataException("Message size differs from playlist.");
			}
		}

		var manifest = new BackupManifest(true, rows);
		JsonStorage.Save(manifestPath, manifest);
		return manifest;
	}
}
