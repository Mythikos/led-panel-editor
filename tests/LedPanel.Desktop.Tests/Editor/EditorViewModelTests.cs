using LedPanel.Desktop.Editor;
using LedPanel.Desktop.Tests.Support;

namespace LedPanel.Desktop.Tests.Editor;

[Trait("Area", "Editor")]
public sealed class EditorViewModelTests
{
	[Fact]
	public async Task QueueEditingAndSelectedPreviewPreserveBehavior()
	{
		var service = new FakeEditorService();
		var editor = new EditorViewModel(service, _ => true);
		editor.ConnectionCommand.Execute(null);
		await editor.PendingOperation;
		editor.AddCommand.Execute(null);
		Assert.Equal(2, editor.Messages.Count);
		Assert.Equal(editor.Messages[1], editor.Selected);
		MessageDraft selected = Assert.IsType<MessageDraft>(editor.Selected);
		selected.Text = "Second";
		editor.MoveUpCommand.Execute(null);
		Assert.Equal(selected, editor.Messages[0]);
		Assert.StartsWith("1. ", selected.Title);
		editor.PreviewCommand.Execute(null);
		await editor.PendingOperation;
		Assert.Equal("Second", service.Preview?.Text);
		Assert.Single(service.Queue);
		editor.UploadCommand.Execute(null);
		await editor.PendingOperation;
		Assert.Equal("Second", service.Queue[0].Text);
		Assert.False(editor.Dirty, "Successful uploads adopt the verified queue as the edit baseline.");
		Assert.Equal("Second", editor.Selected?.Text);
		editor.PlayCommand.Execute(null);
		Assert.False(editor.Playing, "The preview pause button freezes the selected-message simulation.");
		await editor.CloseAsync();
		Assert.True(service.DisposedConnection, "Closing must release adapter ownership.");
	}

	[Fact]
	public async Task InvalidUnselectedMessageBlocksUploadButNotValidSelectedPreview()
	{
		var editor = new EditorViewModel(new FakeEditorService(), _ => true);
		editor.ConnectionCommand.Execute(null);
		await editor.PendingOperation;
		editor.AddCommand.Execute(null);
		editor.Messages[0].Text = "";
		Assert.False(editor.UploadCommand.CanExecute(null), "Every queue entry must be valid before upload.");
		Assert.True(editor.PreviewCommand.CanExecute(null), "A valid selected message can still be previewed.");
		MessageDraft selected = Assert.IsType<MessageDraft>(editor.Selected);
		selected.PauseText = "1.5";
		Assert.False(editor.PreviewCommand.CanExecute(null), "Fractional pauses must not silently truncate.");
		selected.PauseText = "3";
		selected.Text = new string('A', 20);
		selected.Layout = "line";
		Assert.False(selected.EffectsEnabled, "Long lines override alignment, pause, flash and exit.");
		selected.Layout = "pages";
		Assert.True(selected.EffectsEnabled, "Page layout restores the effect controls.");
		await editor.CloseAsync();
	}

	[Fact]
	public async Task PendingUploadPreventsDuplicateCommandsAndFailureKeepsEdits()
	{
		var service = new FakeEditorService { UploadGate = new(), FailUpload = true };
		var editor = new EditorViewModel(service, _ => true);
		editor.ConnectionCommand.Execute(null);
		await editor.PendingOperation;
		Assert.IsType<MessageDraft>(editor.Selected).Text = "Keep these edits";
		editor.UploadCommand.Execute(null);
		Assert.False(editor.Editable, "Edits are disabled during a write transaction.");
		Assert.False(editor.ConnectionCommand.CanExecute(null), "Disconnect cannot interrupt a write transaction.");
		Assert.False(editor.ReloadCommand.CanExecute(null), "Reload cannot replace a queue during upload.");
		editor.UploadCommand.Execute(null);
		Assert.Equal(1, service.UploadCount);
		service.UploadGate.SetResult();
		await editor.PendingOperation;
		Assert.True(editor.Dirty, "A failed upload must preserve the draft for recovery.");
		Assert.Equal("Keep these edits", editor.Selected?.Text);
		Assert.True(editor.FeedbackError, "An actionable upload failure is shown in the status region.");
		await editor.CloseAsync();
	}

	[Fact]
	public async Task DisconnectKeepsDraftEditableAndCloseCancelsOwnedWork()
	{
		var service = new FakeEditorService();
		int confirmations = 0;
		var editor = new EditorViewModel(service, _ => { confirmations++; return false; });
		Assert.Equal("Connect panel", editor.ConnectLabel);
		editor.ConnectionCommand.Execute(null);
		await editor.PendingOperation;
		Assert.Equal("Disconnect", editor.ConnectLabel);
		Assert.IsType<MessageDraft>(editor.Selected).Text = "Offline draft";
		editor.ConnectionCommand.Execute(null);
		await editor.PendingOperation;
		Assert.False(editor.Connected, "The same header action must disconnect after connecting.");
		Assert.Equal("Connect panel", editor.ConnectLabel);
		Assert.Equal(0, confirmations);
		Assert.Equal("Offline draft", editor.Selected?.Text);
		Assert.False(editor.ReloadCommand.CanExecute(null), "Reload requires an active connection.");
		Assert.True(editor.HasSelection, "Disconnected users can continue editing their loaded queue.");
		Assert.False(editor.UploadCommand.CanExecute(null), "Offline editing must not permit device writes.");
		editor.ConnectionCommand.Execute(null);
		await editor.PendingOperation;
		Assert.Equal("Offline draft", editor.Selected?.Text);
		Assert.False(editor.Connected, "Canceling reconnect must preserve the offline draft.");
		Assert.Equal(1, confirmations);
		await editor.CloseAsync();
	}

	[Fact]
	public async Task ReloadRequiresConfirmationBeforeReplacingDirtyQueue()
	{
		bool allowReload = false;
		var editor = new EditorViewModel(new FakeEditorService(), _ => allowReload);
		editor.ConnectionCommand.Execute(null);
		await editor.PendingOperation;
		string? original = editor.Selected?.Text;
		Assert.IsType<MessageDraft>(editor.Selected).Text = "Unsaved draft";
		editor.ReloadCommand.Execute(null);
		await editor.PendingOperation;
		Assert.Equal("Unsaved draft", editor.Selected?.Text);
		Assert.True(editor.Connected, "Canceling reload must retain the connection.");
		allowReload = true;
		editor.ReloadCommand.Execute(null);
		await editor.PendingOperation;
		Assert.Equal(original, editor.Selected?.Text);
		Assert.False(editor.Dirty, "An approved reload adopts the current panel queue.");
		await editor.CloseAsync();
	}

	[Fact]
	public async Task ClosingCancelsPendingUploadAndWaitsForCleanup()
	{
		var service = new FakeEditorService { UploadGate = new() };
		var editor = new EditorViewModel(service, _ => true);
		editor.ConnectionCommand.Execute(null);
		await editor.PendingOperation;
		editor.UploadCommand.Execute(null);
		await editor.CloseAsync();
		Assert.True(editor.PendingOperation.IsCompleted, "Window closure owns and awaits the upload task.");
		Assert.True(service.DisposedConnection, "Cancellation must still clean up the adapter session.");
	}
}
