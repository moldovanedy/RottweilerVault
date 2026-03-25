using System.Text;

namespace RottweilerVault.FsBase.Utils;

public static class BinaryUtils
{
    #region Value to bytes

    public static byte[] ConvertIntToBytes(int variable)
    {
        byte[] bytes = new byte[4];
        bytes[3] = (byte)(variable >> 24);
        bytes[2] = (byte)(variable >> 16);
        bytes[1] = (byte)(variable >> 8);
        bytes[0] = (byte)variable;
        return bytes;
    }

    public static byte[] ConvertUintToBytes(uint variable)
    {
        byte[] bytes = new byte[4];
        bytes[3] = (byte)(variable >> 24);
        bytes[2] = (byte)(variable >> 16);
        bytes[1] = (byte)(variable >> 8);
        bytes[0] = (byte)variable;
        return bytes;
    }

    public static byte[] ConvertShortToBytes(int variable)
    {
        byte[] bytes = new byte[2];
        bytes[1] = (byte)(variable >> 8);
        bytes[0] = (byte)variable;
        return bytes;
    }

    public static byte[] ConvertUshortToBytes(uint variable)
    {
        byte[] bytes = new byte[2];
        bytes[1] = (byte)(variable >> 8);
        bytes[0] = (byte)variable;
        return bytes;
    }

    public static byte[] ConvertBoolToBytes(bool variable)
    {
        return [(byte)(variable ? 1 : 0)];
    }

    public static byte[] ConvertCharToBytes(char variable)
    {
        return [(byte)variable];
    }

    public static byte[] ConvertStringToBytes(string variable)
    {
        byte[] buffer = new byte[variable.Length + 1];
        Encoding.ASCII.GetBytes(variable).CopyTo(buffer, 0);
        //string terminator
        buffer[variable.Length] = 0x00;

        return buffer;
    }

    public static byte[] ConvertLongToBytes(long variable)
    {
        byte[] bytes = new byte[8];
        bytes[7] = (byte)(variable >> 56);
        bytes[6] = (byte)(variable >> 48);
        bytes[5] = (byte)(variable >> 40);
        bytes[4] = (byte)(variable >> 32);
        bytes[3] = (byte)(variable >> 24);
        bytes[2] = (byte)(variable >> 16);
        bytes[1] = (byte)(variable >> 8);
        bytes[0] = (byte)variable;
        return bytes;
    }


    /// <summary>
    /// Converts an int to bytes without allocating and returning a new byte array. Instead, it modifies the passed byte array.
    /// </summary>
    /// <param name="variable">The variable to convert.</param>
    /// <param name="bytes">The byte array to modify.</param>
    /// <param name="startIndex">The index from which to start the modification (will modify 4 bytes).</param>
    public static void ConvertIntToBytes(int variable, byte[] bytes, int startIndex)
    {
        bytes[startIndex + 3] = (byte)(variable >> 24);
        bytes[startIndex + 2] = (byte)(variable >> 16);
        bytes[startIndex + 1] = (byte)(variable >> 8);
        bytes[startIndex + 0] = (byte)variable;
    }

    public static void ConvertUintToBytes(uint variable, byte[] bytes, int startIndex)
    {
        bytes[startIndex + 3] = (byte)(variable >> 24);
        bytes[startIndex + 2] = (byte)(variable >> 16);
        bytes[startIndex + 1] = (byte)(variable >> 8);
        bytes[startIndex + 0] = (byte)variable;
    }

    public static void ConvertShortToBytes(int variable, byte[] bytes, int startIndex)
    {
        bytes[startIndex + 1] = (byte)(variable >> 8);
        bytes[startIndex + 0] = (byte)variable;
    }

    public static void ConvertUshortToBytes(uint variable, byte[] bytes, int startIndex)
    {
        bytes[startIndex + 1] = (byte)(variable >> 8);
        bytes[startIndex + 0] = (byte)variable;
    }

    /// <summary>
    /// Converts a bool to bytes without allocating and returning a new byte array. Instead, it modifies the passed byte array.
    /// </summary>
    /// <param name="variable">The variable to convert.</param>
    /// <param name="bytes">The byte array to modify.</param>
    /// <param name="startIndex">The index from which to start the modification (will modify 1 byte).</param>
    public static void ConvertBoolToBytes(bool variable, byte[] bytes, int startIndex)
    {
        bytes[startIndex] = (byte)(variable ? 1 : 0);
    }

    /// <summary>
    /// Converts a char to bytes (as ASCII) without allocating and returning a new byte array. Instead, it modifies the passed byte array.
    /// </summary>
    /// <param name="variable">The variable to convert.</param>
    /// <param name="bytes">The byte array to modify.</param>
    /// <param name="startIndex">The index from which to start the modification (will modify 1 byte).</param>
    public static void ConvertCharToBytes(char variable, byte[] bytes, int startIndex)
    {
        //bytes[startIndex] = Encoding.ASCII.GetBytes(new char[] { variable })[0];
        bytes[startIndex] = (byte)variable;
    }

    /// <summary>
    /// Converts a string to bytes (as ASCII) without allocating and returning a new byte array. Instead, it modifies the passed byte array.
    /// Will require a number bytes equal to the number of string characters plus one for the string terminator.
    /// </summary>
    /// <param name="variable">The variable to convert.</param>
    /// <param name="bytes">The byte array to modify.</param>
    /// <param name="startIndex">The index from which to start the modification (will modify a number bytes equal to the number of string characters plus one for the string terminator).</param>
    public static void ConvertStringToBytes(string variable, byte[] bytes, int startIndex)
    {
        Encoding.ASCII.GetBytes(variable).CopyTo(bytes, startIndex);
        bytes[variable.Length] = 0x00;
    }

    public static void ConvertLongToBytes(long variable, byte[] bytes, int startIndex)
    {
        bytes[startIndex + 7] = (byte)(variable >> 56);
        bytes[startIndex + 6] = (byte)(variable >> 48);
        bytes[startIndex + 5] = (byte)(variable >> 40);
        bytes[startIndex + 4] = (byte)(variable >> 32);
        bytes[startIndex + 3] = (byte)(variable >> 24);
        bytes[startIndex + 2] = (byte)(variable >> 16);
        bytes[startIndex + 1] = (byte)(variable >> 8);
        bytes[startIndex + 0] = (byte)variable;
    }

    #endregion

    #region Bytes to value

    public static int ConvertBytesToInt(byte[] bytes, int startIndex)
    {
        return
            (bytes[startIndex + 3] << 24) |
            (bytes[startIndex + 2] << 16) |
            (bytes[startIndex + 1] << 8) |
            bytes[startIndex + 0];
    }

    public static uint ConvertBytesToUint(byte[] bytes, int startIndex)
    {
        return
            (uint)((bytes[startIndex + 3] << 24) |
                   (bytes[startIndex + 2] << 16) |
                   (bytes[startIndex + 1] << 8) |
                   bytes[startIndex + 0]);
    }

    public static short ConvertBytesToShort(byte[] bytes, int startIndex)
    {
        return
            (short)((bytes[startIndex + 1] << 8) |
                    bytes[startIndex + 0]);
    }

    public static ushort ConvertBytesToUshort(byte[] bytes, int startIndex)
    {
        return
            (ushort)((bytes[startIndex + 1] << 8) |
                     bytes[startIndex + 0]);
    }

    public static bool ConvertBytesToBool(byte[] bytes, int startIndex)
    {
        return bytes[startIndex] != 0;
    }

    public static char ConvertBytesToChar(byte[] bytes, int startIndex)
    {
        return (char)bytes[startIndex];
    }

    public static string ConvertBytesToString(byte[] bytes, int startIndex)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        int length = 0;
        while (bytes[startIndex] != 0)
        {
            startIndex++;
            length++;
        }

        return Encoding.ASCII.GetString(bytes, startIndex, length);
    }

    public static long ConvertBytesToLong(byte[] bytes, int startIndex)
    {
        return
            ((long)bytes[startIndex + 7] << 56) |
            ((long)bytes[startIndex + 6] << 48) |
            ((long)bytes[startIndex + 5] << 40) |
            ((long)bytes[startIndex + 4] << 32) |
            ((long)bytes[startIndex + 3] << 24) |
            ((long)bytes[startIndex + 2] << 16) |
            ((long)bytes[startIndex + 1] << 8) |
            bytes[startIndex + 0];
    }

    /// <summary>
    /// Converts an array of bytes to a string. Does not perform allocations (except string).
    /// </summary>
    /// <param name="bytes"></param>
    /// <param name="startIndex"></param>
    /// <param name="length">The length of the string (should not include string terminator (0x00)).</param>
    /// <returns></returns>
    public static string ConvertBytesToString(byte[] bytes, int startIndex, int length)
    {
        return Encoding.ASCII.GetString(bytes, startIndex, length);
    }

    #endregion
}