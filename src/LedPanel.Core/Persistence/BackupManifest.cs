namespace LedPanel.Core.Persistence;

internal sealed record BackupManifest(bool Complete, IReadOnlyList<BackupFile> Files);
