using System.Globalization;
using System.Windows.Data;

namespace LedPanel.Desktop.Editor;

internal sealed class EntryOptionEnabledConverter : IMultiValueConverter
{
	public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) => values.Length != 2 || values[1] is not true || values[0] is "move-left" or "move-right";
	public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
