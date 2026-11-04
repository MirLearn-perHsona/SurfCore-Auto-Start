using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace SurfCoreAutoStart;

internal static class Native
{
    public const int WM_NCLBUTTONDOWN = 0xA1;
    public const int HTCAPTION = 0x2;

    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string szDataDescr,
        ref DATA_BLOB pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr,
        ref DATA_BLOB pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SurfCore.AutoStart.v1");

    public static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var plain = Encoding.UTF8.GetBytes(value);
        var entropy = Entropy;
        var inBlob = new DATA_BLOB { cbData = plain.Length, pbData = Marshal.AllocHGlobal(plain.Length) };
        var entBlob = new DATA_BLOB { cbData = entropy.Length, pbData = Marshal.AllocHGlobal(entropy.Length) };
        var outBlob = new DATA_BLOB();
        try
        {
            Marshal.Copy(plain, 0, inBlob.pbData, plain.Length);
            Marshal.Copy(entropy, 0, entBlob.pbData, entropy.Length);
            if (!CryptProtectData(ref inBlob, "SurfCore Auto-start", ref entBlob, IntPtr.Zero, IntPtr.Zero, 0x1, ref outBlob))
                throw new CryptographicException(Marshal.GetLastWin32Error());
            var result = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, result, 0, result.Length);
            return Convert.ToBase64String(result);
        }
        finally
        {
            if (inBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(inBlob.pbData);
            if (entBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(entBlob.pbData);
            if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
        }
    }

    public static string Unprotect(string protectedBase64)
    {
        if (string.IsNullOrWhiteSpace(protectedBase64)) return "";
        byte[] encrypted;
        try { encrypted = Convert.FromBase64String(protectedBase64); }
        catch { return ""; }
        var entropy = Entropy;
        var inBlob = new DATA_BLOB { cbData = encrypted.Length, pbData = Marshal.AllocHGlobal(encrypted.Length) };
        var entBlob = new DATA_BLOB { cbData = entropy.Length, pbData = Marshal.AllocHGlobal(entropy.Length) };
        var outBlob = new DATA_BLOB();
        try
        {
            Marshal.Copy(encrypted, 0, inBlob.pbData, encrypted.Length);
            Marshal.Copy(entropy, 0, entBlob.pbData, entropy.Length);
            if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entBlob, IntPtr.Zero, IntPtr.Zero, 0x1, ref outBlob))
                return "";
            var result = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, result, 0, result.Length);
            return Encoding.UTF8.GetString(result);
        }
        finally
        {
            if (inBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(inBlob.pbData);
            if (entBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(entBlob.pbData);
            if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
        }
    }

    // 用令牌完整性级别判断“真正的管理员”。在 UAC 过滤令牌下，
    // WindowsPrincipal.IsInRole(Administrator) 仍可能返回 true，但进程只有 Medium，
    // 无法写 HKCU Run、AppData 或创建计划任务。
    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenElevation = 20;

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_ELEVATION
    {
        public int TokenIsElevated;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass,
        IntPtr tokenInformation, int tokenInformationLength, out int returnLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    public static bool IsHighIntegrity()
    {
        IntPtr token = IntPtr.Zero;
        IntPtr info = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out token)) return false;
            info = Marshal.AllocHGlobal(sizeof(int));
            if (!GetTokenInformation(token, TokenElevation, info, sizeof(int), out _)) return false;
            return Marshal.PtrToStructure<TOKEN_ELEVATION>(info).TokenIsElevated != 0;
        }
        catch { return false; }
        finally
        {
            if (info != IntPtr.Zero) Marshal.FreeHGlobal(info);
            if (token != IntPtr.Zero) CloseHandle(token);
        }
    }
}
