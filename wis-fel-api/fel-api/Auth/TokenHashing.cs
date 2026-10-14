using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace fel_api.Auth;

public static class TokenHashing
{
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static bool Matches(string value, string expectedHash)
    {
        var actual = Convert.FromHexString(Hash(value));
        var expected = Convert.FromHexString(expectedHash);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static string CreateSecret() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
}