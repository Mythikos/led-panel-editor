using LedPanel.Core.Messages;

namespace LedPanel.Core.Preview;



internal sealed class PreviewAnimation
{
	private readonly PreviewTransition _entry;
	private readonly PreviewTransition _exit;
	private readonly double _target;
	private readonly int _pause;
	internal double Duration => _entry.Duration + _pause + _exit.Duration;

	internal PreviewAnimation(MessageSettings settings, int width)
	{
		double medium = (128 + 120) / 4.0;
		double slow = (128 + 140) / 7.0;
		double[] rates = [64, 48, 36, 27];
		double velocity = settings.Speed <= 3 ? rates[Math.Clamp(settings.Speed, 0, 3)] / 27 * medium
			: 1 / (1 / medium + (1 / slow - 1 / medium) * (settings.Speed - 3) / 3);
		_target = settings.Align == "center" ? (128 - width) / 2.0 : settings.Align == "right" ? 128 - width : 0;
		_entry = Transition(settings.Entry == "random" ? "move-left" : settings.Entry, true, width, velocity);
		_exit = Transition(settings.Exit == "random" ? "move-up" : settings.Exit, false, width, velocity);
		_pause = Math.Max(0, settings.Pause);
	}

	private PreviewTransition Transition(string effect, bool entering, int width, double velocity)
	{
		var rest = new PreviewPosition(_target, 0, 128, effect);
		PreviewPosition edge = effect switch
		{
			"move-left" => rest with { X = entering ? 128 : -width },
			"move-right" => rest with { X = entering ? -width : 128 },
			"move-up" => rest with { Y = entering ? 16 : -16 },
			"move-down" => rest with { Y = entering ? -16 : 16 },
			"reveal-left" or "reveal-right" => rest with { Clip = 0 },
			_ => rest
		};
		double distance = Math.Max(Math.Abs(edge.X - rest.X), Math.Max(Math.Abs(edge.Y - rest.Y), Math.Abs(edge.Clip - rest.Clip)));
		return new(entering ? edge : rest, entering ? rest : edge, effect == "appear" ? .2 : distance / velocity, effect);
	}

	internal PreviewPosition PositionAt(double seconds)
	{
		double phase = Math.Max(0, seconds) % Duration;
		if (phase >= _entry.Duration && phase < _entry.Duration + _pause)
		{
			return new(_target, 0, 128, null);
		}

		PreviewTransition transition = phase < _entry.Duration ? _entry : _exit;
		double elapsed = phase < _entry.Duration ? phase : phase - _entry.Duration - _pause;
		double progress = transition.Duration == 0 ? 1 : Math.Min(1, elapsed / transition.Duration);
		return new(transition.From.X + (transition.To.X - transition.From.X) * progress, transition.From.Y + (transition.To.Y - transition.From.Y) * progress,
			transition.From.Clip + (transition.To.Clip - transition.From.Clip) * progress, transition.Effect);
	}

	internal static byte[] Frame(PreviewLayout layout, double seconds)
	{
		var animation = new PreviewAnimation(layout.Settings, layout.Pages[0].Width);
		Raster raster = layout.Pages[(int)(Math.Max(0, seconds) / animation.Duration % layout.Pages.Count)];
		PreviewPosition position = animation.PositionAt(seconds);
		int left = (int)Math.Floor(position.X + .5);
		int top = (int)Math.Floor(position.Y + .5);
		bool visible = !layout.Settings.Flash || (int)(seconds * 3) % 2 == 0;
		byte[] pixels = new byte[128 * 16];
		for (int row = 0; row < 16; row++)
		{
			for (int column = 0; column < 128; column++)
			{
				int sourceX = column - left;
				int sourceY = row - top;
				bool lit = visible && sourceX >= 0 && sourceX < raster.Width && sourceY is >= 0 and < 16 && raster.Pixels[sourceY * raster.Width + sourceX] == 1;
				lit &= position.Effect switch
				{
					"reveal-right" => column >= 128 - position.Clip,
					"reveal-left" => column < position.Clip,
					_ => true
				};
				pixels[row * 128 + column] = lit ? (byte)1 : (byte)0;
			}
		}

		return pixels;
	}

	private sealed record PreviewTransition(PreviewPosition From, PreviewPosition To, double Duration, string Effect);
}
