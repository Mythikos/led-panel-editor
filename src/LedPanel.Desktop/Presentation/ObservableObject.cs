using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LedPanel.Desktop.Presentation;

internal abstract class ObservableObject : INotifyPropertyChanged
{
	public event PropertyChangedEventHandler? PropertyChanged;
	protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
