using System.Windows;
using System.Windows.Media;

using LedPanel.Core.Fonts;
using LedPanel.Core.Messages;
using LedPanel.Core.Preview;

namespace LedPanel.Desktop.Preview;

internal sealed class LedPreview : FrameworkElement
{
	private readonly AnimationClock _clock = new();
	private PreviewLayout? _layout;
	private byte[] _pixels = new byte[2048];
	private MessageSettings? _settings;
	private BitmapFont? _font;
	private static readonly SolidColorBrush s_background = Frozen("#101418");
	private static readonly SolidColorBrush s_unlit = Frozen("#222B30");
	private static readonly IReadOnlyDictionary<string, SolidColorBrush> s_colors = new Dictionary<string, SolidColorBrush>
	{
		["red"] = Frozen("#FF5146"),
		["green"] = Frozen("#67DC8D"),
		["amber"] = Frozen("#FFCA5C")
	};

	public LedPreview()
	{
		Loaded += OnLoaded;
		Unloaded += OnUnloaded;
	}

	private static SolidColorBrush Frozen(string color)
	{
		var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
		brush.Freeze();
		return brush;
	}

	internal void Update(MessageSettings? settings, BitmapFont? font, bool playing)
	{
		if (_clock.Playing != playing)
		{
			_clock.SetPlaying(playing);
		}

		if (_settings == settings && ReferenceEquals(_font, font))
		{
			return;
		}

		_settings = settings;
		_font = font;
		_layout = settings is not null && font is not null && settings.Text.Length <= 800 && settings.Text.All(character => character is >= ' ' and <= '~')
			? PreviewLayout.Create(settings, font) : null;
		_clock.Reset();
		_pixels = new byte[2048];
		InvalidateVisual();
	}

	private void OnLoaded(object sender, RoutedEventArgs e) => CompositionTarget.Rendering += RenderFrame;
	private void OnUnloaded(object sender, RoutedEventArgs e) => CompositionTarget.Rendering -= RenderFrame;
	private void RenderFrame(object? sender, EventArgs e)
	{
		if (e is RenderingEventArgs frame && IsVisible)
		{
			double seconds = _clock.Sample(frame.RenderingTime.TotalSeconds);
			if (_layout is not null)
			{
				_pixels = PreviewAnimation.Frame(_layout, seconds);
			}

			InvalidateVisual();
		}
	}

	protected override Size MeasureOverride(Size availableSize)
	{
		double width = double.IsInfinity(availableSize.Width) ? 960 : availableSize.Width;
		return new(width, width / 8);
	}

	protected override void OnRender(DrawingContext drawingContext)
	{
		base.OnRender(drawingContext);
		drawingContext.DrawRectangle(s_background, null, new Rect(RenderSize));
		double cellWidth = ActualWidth / 128;
		double cellHeight = ActualHeight / 16;
		SolidColorBrush color = s_colors.GetValueOrDefault(_settings?.Color ?? "red", s_colors["red"]);
		for (int row = 0; row < 16; row++)
		{
			for (int column = 0; column < 128; column++)
			{
				drawingContext.DrawEllipse(_pixels[row * 128 + column] == 1 ? color : s_unlit, null, new((column + .5) * cellWidth, (row + .5) * cellHeight), cellWidth * .32, cellHeight * .32);
			}
		}
	}
}
