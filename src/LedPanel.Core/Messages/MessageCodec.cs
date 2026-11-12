using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LedPanel.Core.Messages;

internal static class MessageCodec
{
	internal static readonly IReadOnlyDictionary<string, string> Colors = Choices(("red", "1"), ("green", "2"), ("amber", "3"));
	internal static readonly IReadOnlyDictionary<string, string> Fonts = Choices(("bold", "N"), ("standard", "2"), ("tall", "4"), ("compact", "1"));
	internal static readonly IReadOnlyDictionary<string, string> Alignments = Choices(("left", "1"), ("center", "0"), ("right", "2"));
	internal static readonly IReadOnlyDictionary<string, string> Layouts = Choices(("line", "a"), ("pages", "b"));
	internal static readonly IReadOnlyDictionary<string, string> Effects = Choices(("move-left", "1"), ("move-right", "2"), ("move-up", "5"), ("move-down", "6"),
		("reveal-left", "3"), ("reveal-right", "4"), ("appear", "0"), ("random", "/"));

	private static ReadOnlyDictionary<string, string> Choices(params (string Name, string Code)[] items) => new(items.ToDictionary(item => item.Name, item => item.Code));

	internal static (int Start, int End) TextBounds(byte[] data)
	{
		if (!data.AsSpan().StartsWith(new byte[] { 1, 90, 48, 48, 2, 65, 15 }))
		{
			throw new InvalidDataException("Unsupported legacy message header.");
		}

		int end = Array.IndexOf(data, (byte)4, 7) - 1;
		if (end < 7 || data[end] != 13)
		{
			throw new InvalidDataException("Missing message terminator.");
		}

		int index = 7;
		while (index < end && data[index] < 32)
		{
			int size = data[index] switch
			{
				7 or 8 or 15 or 26 or 28 or 29 or 30 or 31 => 2,
				10 or 27 => 3,
				14 when index + 1 < end => data[index + 1] switch { 50 or 51 => 6, 48 or 49 => 4, _ => 0 },
				_ => 0
			};
			if (size == 0 || index + size > end || data.AsSpan(index + 1, size - 1).ContainsAnyExceptInRange((byte)32, (byte)126)
				|| (data[index] is 28 or 29 && data[index + 1] == '/'))
			{
				throw new InvalidDataException("Unsupported or truncated message controls.");
			}

			index += size;
		}

		if (index == end || data.AsSpan(index, end - index).ContainsAnyExceptInRange((byte)32, (byte)126))
		{
			throw new InvalidDataException("Only a single printable ASCII text run is supported.");
		}

		return (index, end);
	}

	internal static byte[] Build(byte[] template, MessageSettings settings)
	{
		settings.Validate();
		(int start, int end) = TextBounds(template);
		string prefix = Encoding.Latin1.GetString(template, 7, start - 7);
		(string Pattern, string Value)[] controls =
		[
			("\u001b0[ab]", "\u001b0" + Layouts[settings.Layout]), ("\u001c.", "\u001c" + Colors[settings.Color]),
			("\u001a.", "\u001a" + Fonts[settings.Font]), ("\nI.", "\nI" + Effects[settings.Entry]), ("\nO.", "\nO" + Effects[settings.Exit]),
			("\u000f.", "\u000f" + settings.Speed), ("\u000e(?:[01][0-9]{2}|[23][0-9]{4})", "\u000e2" + settings.Pause.ToString("D4", CultureInfo.InvariantCulture)),
			("\u001e.", "\u001e" + Alignments[settings.Align]), ("\u0007.", "\u0007" + (settings.Flash ? "1" : "0"))
		];
		foreach ((string pattern, string value) in controls)
		{
			if (Regex.Matches(prefix, pattern).Count != 1)
			{
				throw new InvalidDataException("Template must contain exactly one of each display control.");
			}

			prefix = Regex.Replace(prefix, pattern, _ => value);
		}

		byte[] result = [.. template.AsSpan(0, 7), .. Encoding.Latin1.GetBytes(prefix), .. Encoding.ASCII.GetBytes(settings.Text), .. template.AsSpan(end)];
		if (result.Length > 1000)
		{
			throw new InvalidDataException("Message and formatting exceed the panel's 1000-byte limit.");
		}

		return result;
	}

	internal static MessageSettings Parse(byte[] data)
	{
		(int start, int end) = TextBounds(data);
		string prefix = Encoding.Latin1.GetString(data, 0, start);
		string Value(string pattern)
		{
			Match match = Regex.Match(prefix, pattern);
			return match.Success ? match.Groups[1].Value : throw new InvalidDataException("Unsupported message formatting.");
		}

		string Choice(string pattern, IReadOnlyDictionary<string, string> choices) => choices.FirstOrDefault(pair => pair.Value == Value(pattern)).Key
			?? throw new InvalidDataException("Unsupported message setting.");
		var settings = new MessageSettings
		{
			Text = Encoding.ASCII.GetString(data, start, end - start),
			Layout = Choice("\u001b0(.)", Layouts),
			Color = Choice("\u001c(.)", Colors),
			Font = Choice("\u001a(.)", Fonts),
			Entry = Choice("\nI(.)", Effects),
			Exit = Choice("\nO(.)", Effects),
			Align = Choice("\u001e(.)", Alignments),
			Speed = int.Parse(Value("\u000f([0-6])"), CultureInfo.InvariantCulture),
			Pause = int.Parse(Value("\u000e2([0-9]{4})"), CultureInfo.InvariantCulture),
			Flash = Value("\u0007([01])") == "1"
		};
		settings.Validate();
		return settings;
	}
}
