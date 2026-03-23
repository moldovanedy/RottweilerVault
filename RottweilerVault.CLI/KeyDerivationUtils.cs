using System.Security.Cryptography;

namespace RottweilerVault.CLI;

public static class KeyDerivationUtils
{
    public static (byte[] Key1, byte[] Key2) DeriveFromPlainPassword(string password)
    {
        byte[] salt = new byte[8];
        using var rng = RandomNumberGenerator.Create();

        rng.GetBytes(salt);
        byte[] key1 = Rfc2898DeriveBytes.Pbkdf2(password, salt, 10_000, HashAlgorithmName.SHA256, 32);

        rng.GetBytes(salt);
        byte[] key2 = Rfc2898DeriveBytes.Pbkdf2(password, salt, 10_000, HashAlgorithmName.SHA256, 32);

        return (key1, key2);
    }
}