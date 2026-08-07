using System.Security.Cryptography;

namespace Biozin_Royale_Backend.Utilidades;

public static class ReceiptGenerator
{
    private const string Chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string Generate()
    {
        var date = DateTime.UtcNow.ToString("yyyyMMdd");
        Span<byte> bytes = stackalloc byte[6];
        RandomNumberGenerator.Fill(bytes);

        var suffix = new char[6];
        for (int i = 0; i < 6; i++)
            suffix[i] = Chars[bytes[i] % Chars.Length];

        return $"BZR-{date}-{new string(suffix)}";
    }
}
