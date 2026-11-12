namespace LedPanel.Core.Preview;

internal sealed class AnimationClock
{
	private double? _previous;
	private double _elapsed;
	internal bool Playing { get; private set; } = true;
	internal void Reset()
	{
		_elapsed = 0;
		_previous = null;
	}

	internal void SetPlaying(bool playing)
	{
		Playing = playing;
		_previous = null;
	}

	internal double Sample(double timestamp)
	{
		if (Playing)
		{
			if (_previous is double previous)
			{
				_elapsed += Math.Max(0, timestamp - previous);
			}

			_previous = timestamp;
		}

		return _elapsed;
	}
}
