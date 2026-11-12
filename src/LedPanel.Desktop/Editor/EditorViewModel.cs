using System.Collections.ObjectModel;
using System.ComponentModel;

using LedPanel.Core.Editor;
using LedPanel.Core.Fonts;
using LedPanel.Core.Messages;
using LedPanel.Desktop.Presentation;

namespace LedPanel.Desktop.Editor;

internal sealed class EditorViewModel : ObservableObject
{
	private readonly IEditorService _service;
	private readonly CancellationTokenSource _lifetime = new();
	private readonly Func<string, bool> _confirm;
	private readonly List<EditorCommand> _commands = [];
	private Task _activeOperation = Task.CompletedTask;
	private Task _healthOperation = Task.CompletedTask;
	private MessageSettings[] _baseline = [];
	private MessageDraft? _selected;
	private bool _busy;
	private bool _closing;
	private string _feedback = "Connect the panel to load its messages.";
	private bool _feedbackError;
	public ObservableCollection<MessageDraft> Messages { get; } = [];
	public IReadOnlyDictionary<string, string> Colors { get; } = new Dictionary<string, string> { ["red"] = "Red", ["green"] = "Green", ["amber"] = "Amber" };
	public IReadOnlyDictionary<string, string> Fonts
	{
		get;
	} = new Dictionary<string, string>
	{
		["bold"] = "Bold · 14 px",
		["standard"] = "Standard · 14 px",
		["tall"] = "Tall · 16 px",
		["compact"] = "Compact · 7 px"
	};
	public IReadOnlyDictionary<string, string> Alignments { get; } = new Dictionary<string, string> { ["left"] = "Left", ["center"] = "Center", ["right"] = "Right" };
	public IReadOnlyDictionary<string, string> Layouts { get; } = new Dictionary<string, string> { ["line"] = "Line", ["pages"] = "Pages" };
	public IReadOnlyDictionary<string, string> Effects { get; } = MessageCodec.Effects.Keys.ToDictionary(key => key, key => char.ToUpperInvariant(key[0]) + key[1..].Replace('-', ' '));
	public IReadOnlyDictionary<string, BitmapFont>? PanelFonts => _service.Fonts;
	public bool Connected => _service.Connected;
	public bool Editable => !_busy && !_closing;
	public bool HasSelection => Editable && Selected is not null;
	public bool QueueEditable => Editable && Messages.Count > 0;
	public bool IsBusy => _busy;
	public string ConnectionStatus => Connected ? "Panel connected" : "Disconnected";
	public string ConnectLabel => Connected ? "Disconnect" : "Connect panel";
	public string Feedback => _feedback;
	public bool FeedbackError => _feedbackError;
	public bool Dirty => Messages.Any(message => message.Error is not null) || !Messages.Select(message => message.Settings).SequenceEqual(_baseline);
	public string DirtyLabel => Dirty ? "Changes not uploaded" : "";
	public bool Playing { get; private set; } = true;
	public string PlayLabel => Playing ? "Pause" : "Play";
	public MessageDraft? Selected
	{
		get => _selected;
		set
		{
			if (_selected == value)
			{
				return;
			}

			_selected = value;
			Refresh();
		}
	}

	public EditorCommand ConnectionCommand
	{
		get;
	}
	public EditorCommand PreviewCommand
	{
		get;
	}
	public EditorCommand UploadCommand
	{
		get;
	}
	public EditorCommand RestoreCommand
	{
		get;
	}
	public EditorCommand ReloadCommand
	{
		get;
	}
	public EditorCommand AddCommand
	{
		get;
	}
	public EditorCommand RemoveCommand
	{
		get;
	}
	public EditorCommand MoveUpCommand
	{
		get;
	}
	public EditorCommand MoveDownCommand
	{
		get;
	}
	public EditorCommand PlayCommand
	{
		get;
	}

	internal EditorViewModel(IEditorService service, Func<string, bool> confirm)
	{
		_service = service;
		_confirm = confirm;
		ConnectionCommand = Command(() =>
		{
			if (Connected)
			{
				StartOperation("Disconnecting…", async () =>
				{
					await _service.DisconnectAsync(_lifetime.Token);
					return "Disconnected; adapter settings restored.";
				});
			}
			else
			{
				LoadPanel();
			}
		}, () => Editable);
		PreviewCommand = Command(() =>
		{
			if (Selected is MessageDraft selected)
			{
				StartOperation("Sending preview…", () => _service.PreviewAsync(selected.Settings, _lifetime.Token));
			}
		}, () => Connected && HasSelection && Selected?.Error is null);
		UploadCommand = Command(() =>
		{
			MessageSettings[] queue = Messages.Select(message => message.Settings).ToArray();
			StartOperation("Uploading and verifying queue…", async () =>
			{
				string result = await _service.UploadAsync(queue, _lifetime.Token, new Progress<string>(text => SetFeedback(text)));
				AdoptQueue();
				return result;
			});
		}, () => Connected && Editable && _service.CanUpload && Messages.Count > 0 && Messages.All(message => message.Error is null));
		RestoreCommand = Command(() =>
		{
			if (_confirm("Replace the panel's queue with its original message?"))
			{
				StartOperation("Restoring original…", async () => { string result = await _service.RestoreAsync(_lifetime.Token); AdoptQueue(); return result; });
			}
		}, () => Connected && Editable && _service.CanRestore);
		ReloadCommand = Command(LoadPanel, () => Connected && Editable);
		AddCommand = Command(Add, () => QueueEditable && Messages.Count < MessageSettings.MaximumMessages);
		RemoveCommand = Command(Remove, () => HasSelection && Messages.Count > 1);
		MoveUpCommand = Command(() => Move(-1), () => HasSelection && Selected is MessageDraft selected && Messages.IndexOf(selected) > 0);
		MoveDownCommand = Command(() => Move(1), () => HasSelection && Selected is MessageDraft selected && Messages.IndexOf(selected) < Messages.Count - 1);
		PlayCommand = Command(() => { Playing = !Playing; Refresh(); }, () => !_closing);
	}

	private void LoadPanel()
	{
		if (!Dirty || _confirm("Reload the panel and discard your unsaved edits?"))
		{
			StartOperation(Connected ? "Reloading…" : "Connecting…", async () =>
			{
				string result = await _service.ConnectAsync(_lifetime.Token);
				AdoptQueue();
				return result;
			});
		}
	}

	private EditorCommand Command(Action action, Func<bool> enabled)
	{
		var command = new EditorCommand(action, enabled);
		_commands.Add(command);
		return command;
	}

	private void Changed(object? sender, PropertyChangedEventArgs args)
	{
		if (args.PropertyName != nameof(MessageDraft.Title))
		{
			Refresh();
		}
	}
	private void Refresh()
	{
		for (int index = 0; index < Messages.Count; index++)
		{
			Messages[index].SetPosition(index + 1);
		}

		Notify(null);
		foreach (EditorCommand command in _commands)
		{
			command.Refresh();
		}
	}

	private void Add()
	{
		var message = new MessageDraft(new()
		{
			Text = "New message",
			Layout = "pages",
			Entry = "appear",
			Exit = "appear",
			Pause = 3,
			Align = "center"
		});
		message.PropertyChanged += Changed;
		Messages.Add(message);
		Selected = message;
	}

	private void Remove()
	{
		if (Selected is not MessageDraft selected)
		{
			return;
		}

		int index = Messages.IndexOf(selected);
		selected.PropertyChanged -= Changed;
		Messages.Remove(selected);
		Selected = Messages[Math.Min(index, Messages.Count - 1)];
	}

	private void Move(int step)
	{
		if (Selected is MessageDraft selected)
		{
			int index = Messages.IndexOf(selected);
			Messages.Move(index, index + step);
			Refresh();
		}
	}

	private void AdoptQueue()
	{
		int index = Selected is MessageDraft selected ? Messages.IndexOf(selected) : 0;
		foreach (MessageDraft message in Messages)
		{
			message.PropertyChanged -= Changed;
		}

		Messages.Clear();
		_baseline = _service.Queue.ToArray();
		foreach (MessageSettings settings in _baseline)
		{
			var message = new MessageDraft(settings);
			message.PropertyChanged += Changed;
			Messages.Add(message);
		}

		Selected = Messages.Count == 0 ? null : Messages[Math.Clamp(index, 0, Messages.Count - 1)];
		Refresh();
	}

	private void SetFeedback(string text, bool error = false)
	{
		_feedback = text;
		_feedbackError = error;
		Refresh();
	}

	private void StartOperation(string message, Func<Task<string>> action) => _activeOperation = RunAsync(message, action);

	private async Task RunAsync(string message, Func<Task<string>> action)
	{
		_busy = true;
		SetFeedback(message);
		try
		{
			await _healthOperation;
			SetFeedback(await action());
		}
		catch (OperationCanceledException)
		{
			SetFeedback("Operation canceled. Reload the panel before another upload.");
		}
		catch (Exception error)
		{
			// UI command boundary owns and reports the operation's failure.
			SetFeedback(error.Message, true);
		}
		finally
		{
			_busy = false;
			Refresh();
		}
	}

	internal void CheckHealth()
	{
		if (!_busy && !_closing && _healthOperation.IsCompleted)
		{
			_healthOperation = CheckHealthAsync();
		}
	}

	private async Task CheckHealthAsync()
	{
		bool before = Connected;
		try
		{
			bool connected = await _service.HealthAsync(_lifetime.Token);
			if (connected != before)
			{
				if (connected && !Dirty)
				{
					AdoptQueue();
				}

				SetFeedback(connected ? "Panel reconnected." : "Panel disconnected. Reconnect the cable to continue.", !connected);
			}
		}
		catch (OperationCanceledException) when (_closing)
		{
			// Window closure owns cancellation and awaits this task.
		}
		catch (Exception error)
		{
			SetFeedback(error.Message, true);
		}
	}

	internal async Task CloseAsync()
	{
		_closing = true;
		_lifetime.Cancel();
		await Task.WhenAll(_activeOperation, _healthOperation);
		try
		{
			await _service.DisconnectAsync(CancellationToken.None);
		}
		finally
		{
			foreach (MessageDraft message in Messages)
			{
				message.PropertyChanged -= Changed;
			}

			_service.Dispose();
			_lifetime.Dispose();
		}
	}

	internal Task PendingOperation => _activeOperation;
}
