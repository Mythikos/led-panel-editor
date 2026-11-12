using System.Buffers.Binary;
using System.Net;
using System.Text.Json;

using LedPanel.Core.Devices;
using LedPanel.Core.Fonts;
using LedPanel.Core.Messages;
using LedPanel.Core.Persistence;
using LedPanel.Core.Protocol;
using LedPanel.Core.Transport;

namespace LedPanel.Core.Editor;

internal sealed class EditorService : IEditorService
{
	private readonly string _dataDirectory;
	private readonly string _original;
	private readonly PanelConfiguration _configuration;
	private readonly IAdapterSession _network;
	private readonly Func<string, IPanelConnection> _connectionFactory;
	private readonly SemaphoreSlim _operation = new(1, 1);
	private byte[]? _playlist;
	public bool Connected
	{
		get; private set;
	}
	public bool CanUpload => Connected && _playlist is not null;
	public bool CanRestore => File.Exists(Path.Combine(_original, "backup.json"));
	public IReadOnlyList<MessageSettings> Queue { get; private set; } = Array.Empty<MessageSettings>();
	public IReadOnlyDictionary<string, BitmapFont>? Fonts
	{
		get; private set;
	}

	internal EditorService(string dataDirectory, PanelConfiguration configuration, IAdapterSession network, Func<string, IPanelConnection>? connectionFactory = null)
	{
		_dataDirectory = Path.GetFullPath(dataDirectory);
		_original = Path.Combine(_dataDirectory, "original");
		_configuration = configuration;
		_network = network;
		_connectionFactory = connectionFactory ?? (directory => new UdpPanelConnection(IPAddress.Parse(configuration.Source), new IPEndPoint(IPAddress.Parse(configuration.Host), 3001), directory));
		Directory.CreateDirectory(_dataDirectory);
	}

	private async Task<TResult> OperateAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken)
	{
		if (!await _operation.WaitAsync(0, cancellationToken))
		{
			throw new InvalidOperationException("Another panel operation is running.");
		}

		try
		{
			return await action();
		}
		finally
		{
			_operation.Release();
		}
	}

	private (IPanelConnection Connection, string Directory) Open()
	{
		string directory = Path.Combine(_dataDirectory, "desktop-session-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-ffffff") + "-" + Guid.NewGuid().ToString("N")[..6]);
		Directory.CreateDirectory(directory);
		return (_connectionFactory(directory), directory);
	}

	private async Task IdentifyAsync(IPanelConnection connection, CancellationToken cancellationToken)
	{
		PanelIdentity identity = await PanelIdentity.AuthenticateAsync(connection, _configuration.Mac, cancellationToken);
		if (connection is UdpPanelConnection udp)
		{
			udp.Group = identity.Group;
			udp.Unit = identity.Unit;
		}
	}

	private byte[] Template() => BackupStore.Load(_original).Where(pair => pair.Key is not ("CONFIG.SYS" or "SEQUENT.SYS")).Single().Value;

	public Task<string> ConnectAsync(CancellationToken cancellationToken) => OperateAsync(async () =>
	{
		Connected = false;
		_playlist = null;
		await _network.EnsureAsync(cancellationToken);
		try
		{
			(IPanelConnection connection, string directory) = Open();
			using (connection)
			{
				await IdentifyAsync(connection, cancellationToken);
				string fontWarning = await LoadFontsAsync(new(connection), directory, cancellationToken);
				try
				{
					BackupManifest manifest = await BackupStore.CaptureAsync(new(connection), directory, cancellationToken);
					MessageSettings[] queue = LoadQueue(directory, manifest);
					byte[] playlist = await File.ReadAllBytesAsync(Path.Combine(directory, "sequent.sys"), cancellationToken);
					if (!Directory.Exists(_original))
					{
						string staging = Path.Combine(_dataDirectory, "original-" + Guid.NewGuid().ToString("N") + ".tmp");
						Directory.CreateDirectory(staging);
						foreach (BackupFile row in manifest.Files)
						{
							File.Copy(Path.Combine(directory, row.LocalFile), Path.Combine(staging, row.LocalFile));
						}

						File.Copy(Path.Combine(directory, "backup.json"), Path.Combine(staging, "backup.json"));
						Directory.Move(staging, _original);
					}

					BackupStore.Load(_original);
					Queue = Array.AsReadOnly(queue);
					_playlist = playlist;
				}
				catch (Exception error) when (CanRestore && error is InvalidDataException or PanelStatusException)
				{
					BackupStore.Load(_original);
					Connected = true;
					Queue = Array.Empty<MessageSettings>();
					return "Panel connected, but its queue could not be loaded. Restore original is available. " + error.Message;
				}

				Connected = true;
				return "Panel connected" + fontWarning;
			}
		}
		catch
		{
			await _network.CloseAsync(CancellationToken.None);
			throw;
		}
	}, cancellationToken);

	private async Task<string> LoadFontsAsync(PanelFiles files, string directory, CancellationToken cancellationToken)
	{
		string path = Path.Combine(_dataDirectory, "panel-fonts.json");
		try
		{
			if (Fonts is null && File.Exists(path))
			{
				try
				{
					Fonts = FontCache.Load(path, _configuration.Mac);
				}
				catch (Exception error) when (error is IOException or InvalidDataException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
				{
					// A corrupt cache is repaired from the identified panel, never trusted.
				}
			}

			if (Fonts is null)
			{
				IReadOnlyDictionary<string, CapturedFont> captures = await PanelFontReader.CaptureAsync(files, directory, cancellationToken);
				FontCache.Save(path, _configuration.Mac, captures);
				Fonts = FontCache.Load(path, _configuration.Mac);
			}

			return "";
		}
		catch (Exception error) when (error is IOException or InvalidDataException or TimeoutException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
		{
			return ". Display preview unavailable: " + error.Message;
		}
	}

	private static MessageSettings[] LoadQueue(string directory, BackupManifest manifest)
	{
		if (manifest.Files.Count - 2 is < 1 or > MessageSettings.MaximumMessages)
		{
			throw new InvalidDataException("Editor supports 1–20 messages.");
		}

		return manifest.Files.Skip(2).Select(row => MessageCodec.Parse(File.ReadAllBytes(Path.Combine(directory, row.LocalFile)))).ToArray();
	}

	private Task<string> ChangeAsync(Func<IPanelConnection, string, Task<string>> action, CancellationToken cancellationToken) => OperateAsync(async () =>
	{
		if (!Connected || !_network.IsReady)
		{
			throw new InvalidOperationException("Connect to the panel first.");
		}

		try
		{
			(IPanelConnection connection, string directory) = Open();
			using (connection)
			{
				await IdentifyAsync(connection, cancellationToken);
				return await action(connection, directory);
			}
		}
		catch
		{
			Connected = false;
			_playlist = null;
			throw;
		}
	}, cancellationToken);

	public Task<string> PreviewAsync(MessageSettings message, CancellationToken cancellationToken)
	{
		byte[] payload = MessageCodec.Build(Template(), message);
		return ChangeAsync(async (connection, _) =>
		{
			byte[] arguments = new byte[4];
			BinaryPrimitives.WriteUInt16LittleEndian(arguments, 30);
			try
			{
				PanelFrame reply = await connection.RequestAsync(2, 9, arguments, payload, cancellationToken);
				if (reply.Status != 0x9000)
				{
					throw new InvalidDataException("Unexpected preview acknowledgment.");
				}
			}
			catch (TimeoutException)
			{
				return "Preview sent without acknowledgment. Check the panel before retrying.";
			}

			return "30-second preview sent.";
		}, cancellationToken);
	}

	public Task<string> UploadAsync(IReadOnlyList<MessageSettings> queue, CancellationToken cancellationToken, IProgress<string>? progress = null)
	{
		if (queue.Count is < 1 or > MessageSettings.MaximumMessages)
		{
			throw new InvalidDataException("Queue must contain 1–20 messages.");
		}

		MessageSettings[] snapshot = queue.ToArray();
		byte[] template = Template();
		byte[][] payloads = snapshot.Select(message => MessageCodec.Build(template, message)).ToArray();
		return ChangeAsync(async (connection, directory) =>
		{
			byte[] expected = _playlist ?? throw new InvalidOperationException("Reload or restore the panel before uploading.");
			_playlist = await new QueueUploader(connection, directory, TimeProvider.System).UploadAsync(payloads, expected, cancellationToken, progress);
			Queue = Array.AsReadOnly(snapshot);
			return $"{snapshot.Length} message{(snapshot.Length == 1 ? "" : "s")} saved. Playing from #1.";
		}, cancellationToken);
	}

	public Task<string> RestoreAsync(CancellationToken cancellationToken) => ChangeAsync(async (connection, directory) =>
	{
		Dictionary<string, byte[]> original = BackupStore.Load(_original);
		_playlist = await new QueueUploader(connection, directory, TimeProvider.System).RestoreAsync(original, cancellationToken);
		Queue = Array.AsReadOnly(PlaylistCodec.Decode(_playlist).Select(entry => MessageCodec.Parse(original[entry.Path])).ToArray());
		return "Original message restored.";
	}, cancellationToken);

	public async Task DisconnectAsync(CancellationToken cancellationToken)
	{
		await OperateAsync(async () =>
		{
			await _network.CloseAsync(cancellationToken);
			Connected = false;
			_playlist = null;
			return true;
		}, cancellationToken);
	}

	public async Task<bool> HealthAsync(CancellationToken cancellationToken)
	{
		if (!_network.IsReady)
		{
			Connected = false;
			_playlist = null;
			return false;
		}

		if (!await _operation.WaitAsync(0, cancellationToken))
		{
			return Connected;
		}

		try
		{
			(IPanelConnection connection, string directory) = Open();
			using (connection)
			{
				await IdentifyAsync(connection, cancellationToken);
				if (!Connected)
				{
					BackupManifest manifest = await BackupStore.CaptureAsync(new(connection), directory, cancellationToken);
					MessageSettings[] queue = LoadQueue(directory, manifest);
					_playlist = await File.ReadAllBytesAsync(Path.Combine(directory, "sequent.sys"), cancellationToken);
					Queue = Array.AsReadOnly(queue);
				}

				Connected = true;
			}
		}
		catch (Exception error) when (error is IOException or InvalidDataException or TimeoutException or System.Net.Sockets.SocketException)
		{
			Connected = false;
			_playlist = null;
		}
		finally
		{
			_operation.Release();
		}

		return Connected;
	}

	public void Dispose() => _operation.Dispose();
}
