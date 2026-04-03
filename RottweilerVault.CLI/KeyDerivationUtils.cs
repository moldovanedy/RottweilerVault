using System.Security.Cryptography;

namespace RottweilerVault.CLI;

public static class KeyDerivationUtils
{
    public static (byte[] Key1, byte[] Key2) DeriveFromPlainPassword(string password, byte[] salt)
    {
        byte[] key1 = Rfc2898DeriveBytes.Pbkdf2(password, salt[..8], 10_000, HashAlgorithmName.SHA256, 32);
        byte[] key2 = Rfc2898DeriveBytes.Pbkdf2(password, salt[9..], 10_000, HashAlgorithmName.SHA256, 32);
        return (key1, key2);
    }

    public static byte[] GetRandomSalt()
    {
        byte[] salt = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(salt);
        return salt;
    }
}