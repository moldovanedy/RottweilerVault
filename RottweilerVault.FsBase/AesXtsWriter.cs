using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace RottweilerVault.FsBase;

public class AesXtsWriter
{
    public const int BLOCK_SIZE = 4096;

    private const int AES_BLOCK_SIZE = 16;

    private readonly Aes _aes1;
    private readonly Aes _aes2;

    public AesXtsWriter(byte[] key1, byte[] key2)
    {
        _aes1 = Aes.Create();
        _aes1.BlockSize = AES_BLOCK_SIZE * 8;
        _aes1.Mode = CipherMode.ECB;
        _aes1.Padding = PaddingMode.Zeros;
        _aes1.Key = key1;

        _aes2 = Aes.Create();
        _aes2.BlockSize = AES_BLOCK_SIZE * 8;
        _aes2.Mode = CipherMode.ECB;
        _aes2.Padding = PaddingMode.Zeros;
        _aes2.Key = key2;
    }

    public void EncryptLba(FileStream fs, long lbaIndex, byte[] data)
    {
        if (data.Length != BLOCK_SIZE)
        {
            throw new ArgumentException("Data block must be 4096 when encrypting");
        }

        data = Encrypt(data, lbaIndex);
        if (fs.Length % BLOCK_SIZE != 0)
        {
            throw new IOException($"File size is not divisible by {BLOCK_SIZE}. File is corrupt");
        }

        long bytePosition = lbaIndex * BLOCK_SIZE;
        if (fs.Length <= bytePosition + BLOCK_SIZE)
        {
            FillEmptyData(fs, bytePosition + BLOCK_SIZE);
        }

        fs.Seek(bytePosition, SeekOrigin.Begin);
        WriteData(fs, data);
    }

    public byte[] DecryptLba(FileStream fs, long lbaIndex)
    {
        if (fs.Length % BLOCK_SIZE != 0)
        {
            throw new IOException($"File size is not divisible by {BLOCK_SIZE}. File is corrupt");
        }

        long bytePosition = lbaIndex * BLOCK_SIZE;
        if (fs.Length <= bytePosition + BLOCK_SIZE)
        {
            FillEmptyData(fs, bytePosition + BLOCK_SIZE);
        }

        fs.Seek(bytePosition, SeekOrigin.Begin);
        byte[] cyphertext = ReadData(fs);
        return Decrypt(cyphertext, lbaIndex);
    }


    private void FillEmptyData(FileStream fs, long bytePosition)
    {
        byte[] filler = new byte[BLOCK_SIZE];
        long fillerLbaIndex = fs.Length / BLOCK_SIZE;
        fs.Seek(fs.Length, SeekOrigin.Begin);

        while (fs.Length < bytePosition)
        {
            byte[] fillerCypher = Encrypt(filler, fillerLbaIndex);
            WriteData(fs, fillerCypher);
            fillerLbaIndex++;
        }
    }

    private byte[] Encrypt(byte[] plaintext, long lbaIndex)
    {
        byte[] result = new byte[plaintext.Length];
        byte[] tweakRaw = EncryptLbaData(lbaIndex);

        for (int i = 0; i < plaintext.Length / AES_BLOCK_SIZE; i++)
        {
            if (i != 0)
            {
                MulByAlphaInPlace(tweakRaw);
            }

            Span<byte> intermediateDest =
                result.AsSpan((i * AES_BLOCK_SIZE)..(i * AES_BLOCK_SIZE + AES_BLOCK_SIZE));
            ReadOnlySpan<byte> intermediateSrc =
                plaintext.AsSpan((i * AES_BLOCK_SIZE)..(i * AES_BLOCK_SIZE + AES_BLOCK_SIZE));

            //first XOR
            for (int j = 0; j < AES_BLOCK_SIZE; j++)
            {
                intermediateDest[j] = (byte)(tweakRaw[j] ^ intermediateSrc[j]);
            }

            byte[] intermediateCypher = _aes1.EncryptEcb(intermediateDest, PaddingMode.Zeros);

            //second XOR
            for (int j = 0; j < AES_BLOCK_SIZE; j++)
            {
                intermediateDest[j] = (byte)(tweakRaw[j] ^ intermediateCypher[j]);
            }
        }

        return result;
    }

    private byte[] Decrypt(byte[] cyphertext, long lbaIndex)
    {
        byte[] result = new byte[cyphertext.Length];
        byte[] tweakRaw = EncryptLbaData(lbaIndex);

        for (int i = 0; i < cyphertext.Length / AES_BLOCK_SIZE; i++)
        {
            if (i != 0)
            {
                MulByAlphaInPlace(tweakRaw);
            }

            Span<byte> intermediateDest =
                result.AsSpan((i * AES_BLOCK_SIZE)..(i * AES_BLOCK_SIZE + AES_BLOCK_SIZE));
            ReadOnlySpan<byte> intermediateSrc =
                cyphertext.AsSpan((i * AES_BLOCK_SIZE)..(i * AES_BLOCK_SIZE + AES_BLOCK_SIZE));

            //first XOR
            for (int j = 0; j < AES_BLOCK_SIZE; j++)
            {
                intermediateDest[j] = (byte)(tweakRaw[j] ^ intermediateSrc[j]);
            }

            byte[] intermediatePlain = _aes1.DecryptEcb(intermediateDest, PaddingMode.Zeros);

            //second XOR
            for (int j = 0; j < AES_BLOCK_SIZE; j++)
            {
                intermediateDest[j] = (byte)(tweakRaw[j] ^ intermediatePlain[j]);
            }
        }

        return result;
    }

    private static void WriteData(FileStream fs, byte[] cyphertext)
    {
        fs.Write(cyphertext);
    }

    private static byte[] ReadData(FileStream fs)
    {
        byte[] result = new byte[BLOCK_SIZE];
        fs.ReadExactly(result);
        return result;
    }

    private byte[] EncryptLbaData(long lbaIndex)
    {
        byte[] lbaDataRaw = new byte[AES_BLOCK_SIZE];
        lbaDataRaw[15] = (byte)(lbaIndex & 0xff);
        lbaDataRaw[14] = (byte)((lbaIndex >> (8 * 1)) & 0xff);
        lbaDataRaw[13] = (byte)((lbaIndex >> (8 * 2)) & 0xff);
        lbaDataRaw[12] = (byte)((lbaIndex >> (8 * 3)) & 0xff);
        lbaDataRaw[11] = (byte)((lbaIndex >> (8 * 4)) & 0xff);
        lbaDataRaw[10] = (byte)((lbaIndex >> (8 * 5)) & 0xff);
        lbaDataRaw[9] = (byte)((lbaIndex >> (8 * 6)) & 0xff);
        lbaDataRaw[8] = (byte)((lbaIndex >> (8 * 7)) & 0xff);

        return _aes2.EncryptEcb(lbaDataRaw, PaddingMode.Zeros);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void MulByAlphaInPlace(byte[] tweak)
    {
        bool hasCarry = tweak[0] >> 7 == 1;

        // Shift left by 1 bit
        for (int i = 0; i < 15; i++)
        {
            tweak[i] = (byte)((tweak[i] << 1) | (tweak[i + 1] >> 7));
        }

        tweak[15] <<= 1;

        if (hasCarry)
        {
            tweak[15] ^= 0x87;
        }
    }
}