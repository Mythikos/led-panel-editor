namespace LedPanel.Core.Devices;

internal interface IAdapterSession
{
	bool IsReady
	{
		get;
	}
	Task EnsureAsync(CancellationToken cancellationToken);
	Task CloseAsync(CancellationToken cancellationToken);
}
