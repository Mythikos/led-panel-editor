namespace LedPanel.Core.Protocol;

internal sealed class PanelStatusException(ushort status) : IOException($"Panel returned status 0x{status:x4}.")
{
	internal ushort Status { get; } = status;
}
