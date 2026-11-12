using System.Globalization;

using LedPanel.Core.Fonts;
using LedPanel.Core.Messages;
using LedPanel.Desktop.Presentation;

namespace LedPanel.Desktop.Editor;

internal sealed class MessageDraft(MessageSettings settings) : ObservableObject
{
	private MessageSettings _settings = settings;
	private string _pauseText = settings.Pause.ToString(CultureInfo.InvariantCulture);
	private int _position;
	internal MessageSettings Settings => _settings;
	public string Text
	{
		get => _settings.Text; set => Update(_settings with { Text = value });
	}
	public string Color
	{
		get => _settings.Color; set => Update(_settings with { Color = value });
	}
	public string Font
	{
		get => _settings.Font; set => Update(_settings with { Font = value });
	}
	public string Entry
	{
		get => _settings.Entry; set => Update(_settings with { Entry = value });
	}
	public string Exit
	{
		get => _settings.Exit; set => Update(_settings with { Exit = value });
	}
	public int Speed
	{
		get => _settings.Speed; set => Update(_settings with { Speed = value });
	}
	public string Align
	{
		get => _settings.Align; set => Update(_settings with { Align = value });
	}
	public bool Flash
	{
		get => _settings.Flash; set => Update(_settings with { Flash = value });
	}
	public string Layout
	{
		get => _settings.Layout; set => Update(_settings with { Layout = value });
	}
	public string PauseText
	{
		get => _pauseText;
		set
		{
			if (_pauseText == value)
			{
				return;
			}

			_pauseText = value;
			if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int pause))
			{
				_settings = _settings with
				{
					Pause = pause
				};
			}

			Notify(null);
		}
	}

	public string? Error => !int.TryParse(_pauseText, NumberStyles.None, CultureInfo.InvariantCulture, out int pause) || pause is < 0 or > 99
		? "Pause must be a whole number from 0 to 99." : _settings.ValidationError();
	public string Count => $"{Text.Length} / {MessageSettings.MaximumTextLength}";
	public bool IsValid => Error is null;
	public string SpeedLabel => Speed switch { 0 => "Very fast", 1 => "Fast", 2 => "Medium fast", 3 => "Medium", 4 => "Medium slow", 5 => "Slow", _ => "Very slow" };
	public bool Overflow => Layout == "line" && BitmapFont.Profiles.TryGetValue(Font, out var profile) && Text.Length * profile.Width > 128;
	public bool EffectsEnabled => !Overflow;
	public string LayoutHint => Overflow ? "Long lines scroll continuously. Choose Pages to use all effects." : "";
	public string Title => $"{_position}. " + (string.IsNullOrWhiteSpace(Text) ? "New message" : Text);
	internal void SetPosition(int position)
	{
		if (_position != position)
		{
			_position = position;
			Notify(nameof(Title));
		}
	}

	private void Update(MessageSettings value)
	{
		if (_settings == value)
		{
			return;
		}

		_settings = value;
		Notify(null);
	}
}
