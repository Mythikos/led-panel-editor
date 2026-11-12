using System.Buffers.Binary;

namespace LedPanel.Core.Protocol;

internal sealed record PanelFrame(byte[] Source, byte[] Destination, ushort Serial, byte Main, byte Sub, byte Flag, byte[] Arguments, byte[] Data)
{
	internal ushort? Status => Flag == 1 ? BinaryPrimitives.ReadUInt16LittleEndian(Data) : null;
}
