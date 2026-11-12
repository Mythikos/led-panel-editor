using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

using LedPanel.Core.Fonts;
using LedPanel.Core.Messages;
using LedPanel.Core.Protocol;
using LedPanel.Core.Transport;

namespace LedPanel.Core.Tests.Support;

internal sealed class SimulatedPanel : IPanelConnection
{
	internal Dictionary<string, byte[]> Files { get; } = [];
	internal List<string> Writes { get; } = [];
	internal List<(byte Main, byte Sub, byte[] Arguments, byte[] Data)> Commands { get; } = [];
	internal string? ActivePath
	{
		get; set;
	}
	internal bool Temporary
	{
		get; set;
	}
	internal string? FailWrite
	{
		get; set;
	}
	internal int FailMessageNumber
	{
		get; set;
	}
	internal bool DropWriteAcknowledgment
	{
		get; set;
	}
	internal bool ChangePlaylistOnStaging
	{
		get; set;
	}
	internal bool Disconnected
	{
		get; set;
	}
	internal string Mac { get; set; } = "00:11:22:33:44:55";
	internal Func<PanelFrame, PanelFrame>? MutateRead
	{
		get; set;
	}

	internal SimulatedPanel()
	{
		byte[] message = MessageCodec.Build(ReferenceFixtures.Template, new());
		Files["CONFIG.SYS"] = [1, 2, 3];
		Files[@"D:\T\ED00000.Nmg"] = message;
		Files["SEQUENT.SYS"] = PlaylistCodec.Build(ReferenceFixtures.Playlist, [(@"D:\T\ED00000.Nmg", message)]);
		ActivePath = @"D:\T\ED00000.Nmg";
		using JsonDocument fixture = ReferenceFixtures.Open();
		var listing = new List<byte>("FL\x04\0"u8.ToArray());
		foreach ((string name, (char code, int width, int height)) in BitmapFont.Profiles)
		{
			byte[] data = Convert.FromHexString(fixture.RootElement.GetProperty("fonts").GetProperty(name).GetProperty("data_hex").GetString() ?? "");
			Files[name + ".fnt"] = data;
			byte[] row = new byte[28];
			Encoding.ASCII.GetBytes(name + ".fnt", row);
			BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(12), code);
			BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(16), (uint)data.Length);
			row[24] = (byte)width;
			row[25] = (byte)height;
			row[26] = (byte)((width + 7) / 8);
			listing.AddRange(row);
		}

		Files["FONTLIST.LST"] = listing.ToArray();
	}

	public Task<PanelFrame> RequestAsync(byte main, byte sub, byte[] arguments, byte[] data, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		Commands.Add((main, sub, arguments.ToArray(), data.ToArray()));
		if (Disconnected)
		{
			throw new TimeoutException("Disconnected fixture");
		}

		PanelFrame Reply(byte[] args, byte[] body, byte flag = 0) => new([0, 0], [1, 1], 1, main, sub, flag, args, body);
		if (main == 1 && sub == 10)
		{
			return Task.FromResult(Reply(Convert.FromHexString("0a9701022303ffff8000100005250101"), []));
		}

		if (main == 1 && sub == 12)
		{
			return Task.FromResult(Reply([], [.. new byte[16], .. Convert.FromHexString(Mac.Replace(":", ""))]));
		}

		if (main == 1 && sub == 11)
		{
			return Task.FromResult(Reply([Temporary ? (byte)1 : (byte)0, 0, 0, 0, 0, 0, 0, 0], []));
		}

		if (main == 6 && sub == 13)
		{
			return Task.FromResult(Reply([0, 0, 0, 0, .. Encoding.ASCII.GetBytes(ActivePath ?? ""), 0x14, 0x20, 0x10, 0x16], []));
		}

		if (main == 6)
		{
			throw new InvalidOperationException("Unexpected play-control mutation; playlist writes start playback.");
		}

		if (main == 2 && sub == 9)
		{
			return Task.FromResult(Reply([], [0, 0x90], 1));
		}

		if (main == 1 && sub is 2 or 3 or 8)
		{
			string name = Encoding.ASCII.GetString(sub == 8 ? arguments[4..] : arguments[..12]).TrimEnd('\0');
			if (!Files.TryGetValue(name, out byte[]? content))
			{
				throw new PanelStatusException(0x9008);
			}

			int offset = sub == 8 ? 0 : 12;
			int size = BinaryPrimitives.ReadUInt16LittleEndian(arguments.AsSpan(offset));
			int index = BinaryPrimitives.ReadUInt16LittleEndian(arguments.AsSpan(offset + 2));
			byte[] metadata = new byte[8];
			BinaryPrimitives.WriteUInt16LittleEndian(metadata, (ushort)content.Length);
			BinaryPrimitives.WriteUInt16LittleEndian(metadata.AsSpan(2), (ushort)index);
			PanelFrame reply = Reply(metadata, content.Skip((index - 1) * size).Take(size).ToArray());
			return Task.FromResult(MutateRead?.Invoke(reply) ?? reply);
		}

		if (main == 2 && sub is 2 or 8)
		{
			string name = Encoding.ASCII.GetString(sub == 8 ? arguments[10..] : arguments[..12]).TrimEnd('\0');
			int offset = sub == 8 ? 0 : 12;
			int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(arguments.AsSpan(offset));
			int index = BinaryPrimitives.ReadUInt16LittleEndian(arguments.AsSpan(offset + 8));
			if (index == 1)
			{
				Writes.Add(name);
			}

			if (name == FailWrite || sub == 8 && Writes.Count(path => path != "SEQUENT.SYS") == FailMessageNumber)
			{
				throw new IOException("Injected transfer failure");
			}

			if (index == 1)
			{
				Files[name] = new byte[size];
			}

			data.CopyTo(Files[name], (index - 1) * 512);
			if (sub == 2)
			{
				ActivePath = PlaylistCodec.Decode(Files[name])[0].Path;
			}
			else if (ChangePlaylistOnStaging)
			{
				Files["SEQUENT.SYS"][6]++;
			}

			if (DropWriteAcknowledgment)
			{
				throw new TimeoutException("Injected lost acknowledgment");
			}

			return Task.FromResult(Reply([], [0, 0x90], 1));
		}

		throw new NotSupportedException($"Unexpected {main:x2}{sub:x2}");
	}

	public void Dispose()
	{
	}
}
