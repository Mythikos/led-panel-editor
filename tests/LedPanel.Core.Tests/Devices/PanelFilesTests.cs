using LedPanel.Core.Devices;
using LedPanel.Core.Tests.Support;

namespace LedPanel.Core.Tests.Devices;

[Trait("Area", "Protocol")]
public sealed class PanelFilesTests
{
	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	public async Task MalformedReadbackCannotBecomeABackup(int fault)
	{
		using var panel = new SimulatedPanel
		{
			MutateRead = reply => fault switch
			{
				0 => reply with { Arguments = [] },
				1 => reply with { Data = [] },
				2 => reply with { Arguments = [1, 0, 9, 0, 0, 0, 0, 0] },
				_ => reply with { Arguments = [1, 0, 1, 0, 255, 255, 255, 255] }
			}
		};
		await Assert.ThrowsAsync<InvalidDataException>(() => new PanelFiles(panel).ReadAsync("CONFIG.SYS", 2, TestContext.Current.CancellationToken));
	}

	[Theory]
	[InlineData("CONFIG.SYS", true)]
	[InlineData("PROGRAM.CPU", true)]
	[InlineData("D:\\T\\DEMO.Nmg", false)]
	[InlineData("D:\\T\\EDABCDEF.Nmg", false)]
	public async Task ProtectedFilesCannotBeOverwritten(string path, bool system)
	{
		using var panel = new SimulatedPanel();
		await Assert.ThrowsAsync<InvalidDataException>(() => new PanelFiles(panel).WriteAsync(path, ReferenceFixtures.Playlist, system, TestContext.Current.CancellationToken));
		Assert.Empty(panel.Commands);
	}
}
