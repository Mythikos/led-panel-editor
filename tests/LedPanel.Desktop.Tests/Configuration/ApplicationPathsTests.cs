using System.IO;

using LedPanel.Core.Devices;
using LedPanel.Desktop.Configuration;

namespace LedPanel.Desktop.Tests.Configuration;

[Trait("Area", "Configuration")]
public sealed class ApplicationPathsTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "ledpanel-portable-" + Guid.NewGuid().ToString("N"));
	public ApplicationPathsTests() => Directory.CreateDirectory(_root);
	public void Dispose() => Directory.Delete(_root, true);

	[Fact]
	public void MissingProfileDoesNotFallBackToParentDirectory()
	{
		File.WriteAllText(Path.Combine(_root, "panel.json"), Profile());
		string application = Path.Combine(_root, "application");
		Directory.CreateDirectory(application);
		var paths = new ApplicationPaths(application);
		Assert.Equal(Path.Combine(application, "panel.json"), paths.ConfigurationPath);
		Assert.Equal(Path.Combine(application, "data"), paths.DataDirectory);
		FileNotFoundException error = Assert.Throws<FileNotFoundException>(paths.LoadConfiguration);
		Assert.Contains("panel.example.json", error.Message);
	}

	[Fact]
	public void MovingApplicationDirectoryPreservesProfileAndLocalData()
	{
		string application = Path.Combine(_root, "before");
		Directory.CreateDirectory(application);
		var before = new ApplicationPaths(application);
		File.WriteAllText(before.ConfigurationPath, Profile());
		Directory.CreateDirectory(before.DataDirectory);
		File.WriteAllText(Path.Combine(before.DataDirectory, "preserved.txt"), "Synthetic backup");
		string destination = Path.Combine(_root, "after");
		Directory.Move(application, destination);
		var after = new ApplicationPaths(destination);
		PanelConfiguration configuration = after.LoadConfiguration();
		Assert.Equal("192.0.2.1", configuration.Host);
		Assert.Equal("00:11:22:33:44:55", configuration.Mac);
		Assert.Equal("Synthetic backup", File.ReadAllText(Path.Combine(after.DataDirectory, "preserved.txt")));
		Assert.False(Directory.Exists(application), "The application must no longer depend on its previous location.");
	}

	[Fact]
	public void OutputContainsOnlyExampleProfileAndRequiredAdapterHelpers()
	{
		Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "panel.example.json")), "Build output must include the setup template.");
		Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "panel.json")), "A developer's private profile must never be copied by the project.");
		foreach (string script in new[] { "Start-Network.ps1", "Network-Session.ps1" })
		{
			Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Networking", "Scripts", script)), "The portable application needs both adapter helpers.");
		}
	}

	private static string Profile() => """
		{"host":"192.0.2.1","source":"192.0.2.2","mac":"00:11:22:33:44:55","adapter_id":"synthetic-adapter"}
		""";
}
