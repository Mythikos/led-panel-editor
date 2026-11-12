using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using LedPanel.Desktop.Editor;
using LedPanel.Desktop.Presentation;
using LedPanel.Desktop.Tests.Support;

namespace LedPanel.Desktop.Tests.Editor;

[Trait("Area", "RenderedUI")]
public sealed class MainWindowTests
{
	[Fact]
	public async Task NativeWindowLoadsBindingsEditsMessagesAndRendersAtTwoSizes()
	{
		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var thread = new Thread(() =>
		{
			Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
			SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
			dispatcher.InvokeAsync(async () =>
			{
				try
				{
					await ExerciseWindowAsync();
					completion.TrySetResult();
				}
				catch (Exception error)
				{
					completion.TrySetException(error);
				}
				finally
				{
					dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
				}
			});
			Dispatcher.Run();
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		await completion.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
	}

	private static async Task ExerciseWindowAsync()
	{
		var application = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
		application.InitializeComponent();
		var errors = new BindingErrors();
		PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
		var service = new FakeEditorService();
		var editor = new EditorViewModel(service, _ => true);
		var window = new MainWindow(editor) { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -2000, Top = 0 };
		try
		{
			window.Show();
			var connection = Assert.IsType<Button>(window.FindName("ConnectionButton"));
			var reload = Assert.IsType<Button>(window.FindName("ReloadButton"));
			Assert.Equal("Connect panel", connection.Content);
			Assert.Equal(Visibility.Collapsed, reload.Visibility);
			Assert.IsType<EditorCommand>(connection.Command).Execute(null);
			await editor.PendingOperation;
			await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
			Assert.Equal("Disconnect", connection.Content);
			Assert.Equal(Visibility.Visible, reload.Visibility);
			Assert.IsType<EditorCommand>(connection.Command).Execute(null);
			await editor.PendingOperation;
			await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
			Assert.Equal("Connect panel", connection.Content);
			Assert.Equal(Visibility.Collapsed, reload.Visibility);
			Assert.IsType<EditorCommand>(connection.Command).Execute(null);
			await editor.PendingOperation;
			var text = Assert.IsType<TextBox>(window.FindName("MessageText"));
			text.Text = "A AA AAA";
			Assert.Equal("A AA AAA", editor.Selected?.Text);
			editor.AddCommand.Execute(null);
			text.Text = "A AAAAAA AAAAAAA AAAAAA";
			var layout = Assert.IsType<ComboBox>(window.FindName("LayoutChoice"));
			layout.SelectedValue = "pages";
			Assert.Equal("pages", editor.Selected?.Layout);
			Assert.Equal(2, editor.Messages.Count);
			string output = Path.Combine(Path.GetTempPath(), "ledpanel-wpf-render-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(output);
			foreach ((int width, int height) in new[] { (1080, 980), (760, 650) })
			{
				window.Width = width;
				window.Height = height;
				await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
				var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
				bitmap.Render(window);
				var encoder = new PngBitmapEncoder();
				encoder.Frames.Add(BitmapFrame.Create(bitmap));
				using var stream = File.Create(Path.Combine(output, $"editor-{width}.png"));
				encoder.Save(stream);
				Assert.True(text.ActualWidth > 500, "Message field must fit the native window without horizontal clipping.");
			}

			Console.WriteLine("WPF render evidence: " + output);
			Assert.Empty(errors.Errors);
			editor.UploadCommand.Execute(null);
			await editor.PendingOperation;
			Assert.Equal(2, service.Queue.Count);
			window.Close();
			await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
			Assert.True(service.DisposedConnection, "Closing the real WPF window must disconnect the adapter.");
		}
		finally
		{
			PresentationTraceSources.DataBindingSource.Listeners.Remove(errors);
			application.Shutdown();
		}
	}

	private sealed class BindingErrors : TraceListener
	{
		internal List<string> Errors { get; } = [];
		public override void Write(string? message)
		{
			if (message is not null)
			{
				Errors.Add(message);
			}
		}
		public override void WriteLine(string? message) => Write(message);
	}
}
