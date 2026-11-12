using System.IO;

using LedPanel.Core.Devices;

namespace LedPanel.Desktop.Configuration;

internal sealed class ApplicationPaths
{
	internal string ConfigurationPath
	{
		get;
	}
	internal string DataDirectory
	{
		get;
	}

	internal ApplicationPaths(string applicationDirectory)
	{
		string directory = Path.GetFullPath(applicationDirectory);
		ConfigurationPath = Path.Combine(directory, "panel.json");
		DataDirectory = Path.Combine(directory, "data");
	}

	internal PanelConfiguration LoadConfiguration()
	{
		if (!File.Exists(ConfigurationPath))
		{
			throw new FileNotFoundException(
				"Create panel.json beside the executable using panel.example.json, then enter your panel and adapter details before opening the editor.", ConfigurationPath);
		}

		return PanelConfiguration.Load(ConfigurationPath);
	}
}
