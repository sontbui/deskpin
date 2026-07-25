using System.Runtime.InteropServices;
using System.Security;

namespace RdpManager.Infrastructure.Credentials;

/// <summary>
/// Converts between <see cref="SecureString"/> and UTF-16 bytes, zeroing intermediate buffers.
/// Kept tiny and in one place because mishandling here is a credential leak.
/// </summary>
internal static class SecureStringBytes
{
    public static byte[] ToUtf16Bytes(SecureString secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        IntPtr bstr = IntPtr.Zero;
        try
        {
            bstr = Marshal.SecureStringToBSTR(secret);
            var length = Marshal.ReadInt32(bstr, -4); // BSTR length prefix, in bytes
            var bytes = new byte[length];
            Marshal.Copy(bstr, bytes, 0, length);
            return bytes;
        }
        finally
        {
            if (bstr != IntPtr.Zero) Marshal.ZeroFreeBSTR(bstr);
        }
    }

    public static SecureString FromUtf16Bytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var ss = new SecureString();
        for (var i = 0; i + 1 < bytes.Length; i += 2)
            ss.AppendChar((char)(bytes[i] | (bytes[i + 1] << 8)));
        ss.MakeReadOnly();
        return ss;
    }

    public static void ZeroFill(byte[]? bytes)
    {
        if (bytes is not null) Array.Clear(bytes);
    }
}
