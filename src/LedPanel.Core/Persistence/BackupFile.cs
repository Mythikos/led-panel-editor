namespace LedPanel.Core.Persistence;

internal sealed record BackupFile(string PanelPath, string LocalFile, int Size, string Sha256);
