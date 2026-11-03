using System.Security.Cryptography;

namespace Ohrbob.Server.Shared.Helpers;

public static class ShareCodeGenerator
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private const int Length = 10;

    public static string Generate()
    {
        var bytes = new byte[Length];
        using (var random = RandomNumberGenerator.Create())
        {
            random.GetBytes(bytes);
        }
        var chars = new char[Length];

        for (var i = 0; i < Length; i++)
        {
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        }

        return new string(chars);
    }
}
