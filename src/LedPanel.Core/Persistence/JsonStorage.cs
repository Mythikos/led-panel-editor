using System.Text.Json;

namespace LedPanel.Core.Persistence;

internal static class JsonStorage
{
	internal static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true };

	internal static void Save<TValue>(string path, TValue value)
	{
		string temporary = path + ".tmp";
		using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
		{
			JsonSerializer.Serialize(stream, value, Options);
			stream.Flush(true);
		}

		File.Move(temporary, path, true);
	}
}
