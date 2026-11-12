using System.Security.Cryptography;

namespace LedPanel.Core.Persistence;

internal static class ContentHash
{
	internal static string Compute(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
