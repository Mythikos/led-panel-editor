using LedPanel.Core.Fonts;
using LedPanel.Core.Messages;

namespace LedPanel.Core.Editor;

internal interface IEditorService : IDisposable
{
	bool Connected
	{
		get;
	}
	bool CanUpload
	{
		get;
	}
	bool CanRestore
	{
		get;
	}
	IReadOnlyList<MessageSettings> Queue
	{
		get;
	}
	IReadOnlyDictionary<string, BitmapFont>? Fonts
	{
		get;
	}
	Task<string> ConnectAsync(CancellationToken cancellationToken);
	Task<string> PreviewAsync(MessageSettings message, CancellationToken cancellationToken);
	Task<string> UploadAsync(IReadOnlyList<MessageSettings> queue, CancellationToken cancellationToken, IProgress<string>? progress = null);
	Task<string> RestoreAsync(CancellationToken cancellationToken);
	Task DisconnectAsync(CancellationToken cancellationToken);
	Task<bool> HealthAsync(CancellationToken cancellationToken);
}
