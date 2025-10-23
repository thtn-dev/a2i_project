using System.Security.Cryptography;
using BuildingBlocks.Utils.Extensions;

namespace BuildingBlocks.Utils.Helpers;

public class CryptoRandomHelper
{
    private static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();

    public static byte[] CreateRandomBytes(int length)
    {
        var bytes = new byte[length];
        Rng.GetBytes(bytes);

        return bytes;
    }

    public static string CreateRandomKey(int length)
    {
        var bytes = new byte[length];
        Rng.GetBytes(bytes);

        return Convert.ToBase64String(CreateRandomBytes(length));
    }

    public static string CreateUniqueKey(int length = 8)
    {
        return CreateRandomBytes(length).ToHexString();
    }

    public static string CreateSeriesNumber(string prefix = "")
    {
        return $"{prefix}{DateTime.Now:yyyyMMddHHmmss}{CreateUniqueKey()}";
    }
}