using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

using LedPanel.Core.Fonts;
using LedPanel.Core.Messages;

namespace LedPanel.Desktop.Editor;

/// <summary>Hosts the editor and owns rendering, connection monitoring, and graceful window closure.</summary>
public partial class MainWindow : Window
{
	private readonly EditorViewModel _viewModel;
	private readonly DispatcherTimer _healthTimer = new() { Interval = TimeSpan.FromSeconds(10) };
	private bool _closing;
	private bool _closed;

	internal MainWindow(EditorViewModel viewModel)
	{
		_viewModel = viewModel;
		InitializeComponent();
		Height = Math.Min(Height, SystemParameters.WorkArea.Height - 40);
		DataContext = viewModel;
		viewModel.PropertyChanged += ViewModelChanged;
		_healthTimer.Tick += CheckHealth;
		_healthTimer.Start();
		Closing += WindowClosing;
	}

	private void CheckHealth(object? sender, EventArgs e) => _viewModel.CheckHealth();
	private void ViewModelChanged(object? sender, PropertyChangedEventArgs e)
	{
		MessageSettings? settings = _viewModel.Selected?.Settings;
		BitmapFont? font = settings is null ? null : _viewModel.PanelFonts?.GetValueOrDefault(settings.Font);
		DisplayPreview.Update(settings, font, _viewModel.Playing);
	}

	private async void WindowClosing(object? sender, CancelEventArgs e)
	{
		if (_closed)
		{
			return;
		}

		e.Cancel = true;
		if (_closing)
		{
			return;
		}

		if ((_viewModel.Dirty || _viewModel.IsBusy) && MessageBox.Show(this,
			_viewModel.IsBusy ? "Cancel the current operation and close? A sent upload may still complete; reload it next time." : "Close and discard your unsaved edits?",
			"Panel editor", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
		{
			return;
		}

		_closing = true;
		IsEnabled = false;
		_healthTimer.Stop();
		try
		{
			await _viewModel.CloseAsync();
		}
		catch (Exception error)
		{
			MessageBox.Show(this, error.Message, "Adapter cleanup", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
		finally
		{
			_healthTimer.Tick -= CheckHealth;
			_viewModel.PropertyChanged -= ViewModelChanged;
			_closed = true;
			// Cleanup can finish synchronously. Defer the second Close until WPF
			// has returned from this first Closing event.
			await Dispatcher.InvokeAsync(Close, DispatcherPriority.Background);
		}
	}
}
