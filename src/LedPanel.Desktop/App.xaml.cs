using System.Windows;

using LedPanel.Core.Devices;
using LedPanel.Core.Editor;
using LedPanel.Desktop.Configuration;
using LedPanel.Desktop.Editor;
using LedPanel.Desktop.Networking;

namespace LedPanel.Desktop;

/// <summary>Owns the desktop window and one application instance; hardware work remains in the editor service.</summary>
public partial class App : Application
{
	private Mutex? _instance;
	private bool _ownsInstance;

	/// <inheritdoc />
	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		try
		{
			_instance = new Mutex(true, @"Local\LedPanel.Desktop", out _ownsInstance);
			if (!_ownsInstance)
			{
				MessageBox.Show("Panel editor is already open.", "Panel editor", MessageBoxButton.OK, MessageBoxImage.Information);
				Shutdown();
				return;
			}

			if (e.Args.Length != 0)
			{
				throw new ArgumentException("Launch LedPanel.Desktop.exe without arguments. Configuration is stored beside the executable.");
			}

			var paths = new ApplicationPaths(AppContext.BaseDirectory);
			PanelConfiguration configuration = paths.LoadConfiguration();
			var network = new AdapterSession(paths.DataDirectory, paths.ConfigurationPath);
			var service = new EditorService(paths.DataDirectory, configuration, network);
			var viewModel = new EditorViewModel(service, question => MessageBox.Show(question, "Panel editor", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
			var window = new MainWindow(viewModel);
			MainWindow = window;
			window.Show();
		}
		catch (Exception error)
		{
			MessageBox.Show(error.Message, "Panel editor could not start", MessageBoxButton.OK, MessageBoxImage.Error);
			Shutdown(1);
		}
	}

	/// <inheritdoc />
	protected override void OnExit(ExitEventArgs e)
	{
		if (_ownsInstance)
		{
			_instance?.ReleaseMutex();
		}

		_instance?.Dispose();
		base.OnExit(e);
	}
}
