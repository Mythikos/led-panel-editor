using LedPanel.Core.Preview;

namespace LedPanel.Core.Tests.Preview;

[Trait("Area", "Preview")]
public sealed class AnimationClockTests
{
	[Fact]
	public void EditingDuringAFrameCannotProduceNegativeTimeAndPausePreservesPosition()
	{
		var clock = new AnimationClock();
		Assert.Equal(0, clock.Sample(.1));
		Assert.Equal(.15, clock.Sample(.25), 8);
		clock.Reset();
		Assert.Equal(0, clock.Sample(.24));
		Assert.Equal(.02, clock.Sample(.26), 8);
		clock.SetPlaying(false);
		Assert.Equal(.02, clock.Sample(100), 8);
		clock.SetPlaying(true);
		Assert.Equal(.02, clock.Sample(101), 8);
		Assert.Equal(1.02, clock.Sample(102), 8);
	}
}
