using System.Net;
using System.Net.Sockets;
using System.Text.Json;

using LedPanel.Core.Persistence;

namespace LedPanel.Core.Devices;

internal sealed record PanelConfiguration(string Host, string Source, string Mac, string AdapterId)
{
	internal static PanelConfiguration Load(string path)
	{
		PanelConfiguration value = JsonSerializer.Deserialize<PanelConfiguration>(File.ReadAllText(path), JsonStorage.Options)
			?? throw new InvalidDataException("Panel configuration is empty.");
		if (!IPAddress.TryParse(value.Host, out IPAddress? host) || host.AddressFamily != AddressFamily.InterNetwork
			|| !IPAddress.TryParse(value.Source, out IPAddress? source) || source.AddressFamily != AddressFamily.InterNetwork
			|| host.Equals(source) || !host.GetAddressBytes().AsSpan(0, 3).SequenceEqual(source.GetAddressBytes().AsSpan(0, 3))
			|| string.IsNullOrWhiteSpace(value.AdapterId) || value.Mac is null || value.Mac.Replace(":", "").Replace("-", "").Length != 12)
		{
			throw new InvalidDataException("Panel profile must identify one adapter, a MAC, and distinct IPv4 addresses on the same /24 subnet.");
		}

		return value with
		{
			Mac = string.Join(":", Convert.FromHexString(value.Mac.Replace(":", "").Replace("-", "")).Select(item => item.ToString("x2")))
		};
	}
}
