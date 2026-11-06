using System.Security.Cryptography;
using System.Text;

namespace NvChat.Services;

/// <summary>Windows DPAPI(CurrentUser)로 문자열을 암호화/복호화한다. 다른 사용자 계정이나 다른 PC 에서는 복호화되지 않는다.</summary>
internal static class SecureText
{
    /// <summary>암호화한 base64. 암호화에 실패하면(예외적) 평문을 저장하지 않도록 null.</summary>
    public static string? Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return plain;
        try { return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser)); }
        catch { return null; }
    }

    /// <summary>복호화한 평문. 손상되었거나 다른 환경에서 저장된 값이면 null.</summary>
    public static string? Unprotect(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return cipher;
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(cipher), null, DataProtectionScope.CurrentUser)); }
        catch { return null; }
    }
}
