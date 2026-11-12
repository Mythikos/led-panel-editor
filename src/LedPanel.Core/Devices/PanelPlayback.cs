using System.Text;

using LedPanel.Core.Protocol;
using LedPanel.Core.Transport;

namespace LedPanel.Core.Devices;

internal sealed class PanelPlayback(IPanelConnection connection, TimeProvider timeProvider)
{
	internal async Task WaitScheduledAsync(CancellationToken cancellationToken, IProgress<string>? progress = null)
	{
		long started = timeProvider.GetTimestamp();
		while (true)
		{
			PanelFrame reply = await connection.RequestAsync(1, 11, [], [], cancellationToken);
			if (reply.Arguments.Length != 8 || reply.Status is not null || reply.Arguments[0] > 1)
			{
				throw new InvalidDataException("Panel is not in a supported playback mode.");
			}

			if (reply.Arguments[0] == 0)
			{
				return;
			}

			if (timeProvider.GetElapsedTime(started).TotalSeconds >= 45)
			{
				throw new TimeoutException("Preview is still active. No files were changed; wait for it to finish.");
			}

			progress?.Report("Waiting for the preview to finish…");
			await Task.Delay(TimeSpan.FromSeconds(1), timeProvider, cancellationToken);
		}
	}

	internal async Task WaitFirstAsync(string path, CancellationToken cancellationToken)
	{
		byte[] expected = Encoding.ASCII.GetBytes(path);
		long started = timeProvider.GetTimestamp();
		while (true)
		{
			try
			{
				PanelFrame status = await connection.RequestAsync(1, 11, [], [], cancellationToken);
				PanelFrame current = await connection.RequestAsync(6, 13, [1, 0, 0, 0, 0, 2, 1, 0], [], cancellationToken);
				if (status.Arguments.Length == 8 && status.Arguments[0] == 0 && current.Arguments.Length >= 4 + expected.Length
					&& current.Arguments.AsSpan(0, 4).SequenceEqual(new byte[4]) && current.Arguments.AsSpan(4, expected.Length).SequenceEqual(expected))
				{
					return;
				}
			}
			catch (PanelStatusException error) when (error.Status == 0x6701)
			{
				// The file switch may briefly report no current file.
			}

			if (timeProvider.GetElapsedTime(started).TotalSeconds >= 5)
			{
				throw new InvalidDataException("Files were saved, but playback of message #1 could not be confirmed. Reload the panel.");
			}

			await Task.Delay(TimeSpan.FromMilliseconds(50), timeProvider, cancellationToken);
		}
	}
}
