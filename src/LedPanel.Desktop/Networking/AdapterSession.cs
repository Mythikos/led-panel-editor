using System.Diagnostics;
using System.IO;
using System.Text.Json;

using LedPanel.Core.Devices;

namespace LedPanel.Desktop.Networking;

internal sealed class AdapterSession(string dataDirectory, string configurationPath) : IAdapterSession
{
	private string? _statusPath;
	private string? _stopPath;
	public bool IsReady => Report().GetValueOrDefault("State") == "ready";

	private Dictionary<string, string> Report()
	{
		if (_statusPath is null || !File.Exists(_statusPath))
		{
			return [];
		}

		try
		{
			using JsonDocument document = JsonDocument.Parse(File.ReadAllText(_statusPath));
			return document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.ToString());
		}
		catch (Exception error) when (error is IOException or JsonException)
		{
			// The watchdog atomically owns state; a partial report is retried on the next poll.
			return [];
		}
	}

	public async Task EnsureAsync(CancellationToken cancellationToken)
	{
		if (IsReady)
		{
			return;
		}

		await CloseAsync(cancellationToken);
		string name = "desktop-network-" + Guid.NewGuid().ToString("N");
		_statusPath = Path.Combine(dataDirectory, name + ".json");
		_stopPath = Path.Combine(dataDirectory, name + ".stop");
		var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
		foreach (string argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(AppContext.BaseDirectory, "Networking", "Scripts", "Start-Network.ps1"),
			"-ConfigPath", configurationPath, "-OwnerId", Environment.ProcessId.ToString(), "-StopFile", _stopPath, "-StatusFile", _statusPath })
		{
			start.ArgumentList.Add(argument);
		}

		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(TimeSpan.FromSeconds(180));
		using Process process = Process.Start(start) ?? throw new IOException("Could not start adapter setup.");
		Task<string> errors = process.StandardError.ReadToEndAsync(deadline.Token);
		Task<string> output = process.StandardOutput.ReadToEndAsync(deadline.Token);
		try
		{
			await process.WaitForExitAsync(deadline.Token);
			string error = await errors;
			await output;
			if (process.ExitCode != 0)
			{
				throw new IOException("Windows did not authorize adapter setup. " + error.Trim());
			}

			for (int attempt = 0; attempt < 70; attempt++)
			{
				Dictionary<string, string> report = Report();
				if (report.GetValueOrDefault("State") == "ready")
				{
					return;
				}

				if (report.GetValueOrDefault("State") is "error" or "closed")
				{
					throw new IOException(report.GetValueOrDefault("Error") ?? report.GetValueOrDefault("CleanupError") ?? "Adapter setup closed.");
				}

				await Task.Delay(500, deadline.Token);
			}

			throw new TimeoutException("Adapter setup timed out.");
		}
		catch
		{
			// A UAC dialog accepted after cancellation must not orphan a network change.
			await File.WriteAllTextAsync(_stopPath, "", CancellationToken.None);
			if (!process.HasExited)
			{
				process.Kill();
			}

			try
			{
				await Task.WhenAll(errors, output);
			}
			catch (OperationCanceledException)
			{
				// Both redirected readers belong to the canceled setup operation.
			}

			throw;
		}
	}

	public async Task CloseAsync(CancellationToken cancellationToken)
	{
		if (_stopPath is null)
		{
			return;
		}

		await File.WriteAllTextAsync(_stopPath, "", cancellationToken);
		if (_statusPath is null || !File.Exists(_statusPath))
		{
			return;
		}

		for (int attempt = 0; attempt < 20; attempt++)
		{
			Dictionary<string, string> report = Report();
			if (report.GetValueOrDefault("State") is "closed" or "error")
			{
				if (report.TryGetValue("CleanupError", out string? error))
				{
					throw new IOException("Adapter restoration failed: " + error);
				}

				return;
			}

			await Task.Delay(500, cancellationToken);
		}

		throw new IOException("Adapter restoration is still pending. Check the adapter connection.");
	}
}
