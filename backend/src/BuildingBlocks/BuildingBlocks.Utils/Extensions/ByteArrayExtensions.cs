using System.Text;

namespace BuildingBlocks.Utils.Extensions;

public static class ByteArrayExtensions
{
    public static string ToBase64String(this byte[] input)
    {
        return Convert.ToBase64String(input);
    }

    public static string ToUrlSuitable(this byte[] input)
    {
        return input.ToBase64String().Replace("+", "-").Replace("/", "_").Replace("=", "%3d");
    }

    public static string ToHexString(this byte[] bytes)
    {
        var hex = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) hex.Append($"{b:x2}");

        return hex.ToString();
    }
}