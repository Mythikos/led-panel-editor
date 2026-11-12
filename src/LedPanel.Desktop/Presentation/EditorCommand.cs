using System.Windows.Input;

namespace LedPanel.Desktop.Presentation;

internal sealed class EditorCommand(Action execute, Func<bool> canExecute) : ICommand
{
	public event EventHandler? CanExecuteChanged;
	public bool CanExecute(object? parameter) => canExecute();
	public void Execute(object? parameter)
	{
		if (canExecute())
		{
			execute();
		}
	}

	internal void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
