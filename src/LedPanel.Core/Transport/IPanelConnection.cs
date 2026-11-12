using LedPanel.Core.Protocol;

namespace LedPanel.Core.Transport;

internal interface IPanelConnection : IDisposable
{
	Task<PanelFrame> RequestAsync(byte main, byte sub, byte[] arguments, byte[] data, CancellationToken cancellationToken);
}
