using System;
using System.Security.Cryptography;
using System.Text;

namespace NvChat.Services
{
    /// <summary>
    /// Windows DPAPI(CurrentUser)로 문자열을 암호화/복호화한다.
    /// 다른 사용자 계정이나 다른 PC 에서는 복호화되지 않는다.
    /// </summary>
    internal static class SecureText
    {
        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain))
                return plain;

            try
            {
                var bytes = Encoding.UTF8.GetBytes(plain);
                var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encrypted);
            }
            catch
            {
                // 암호화 실패 시(예외적) 평문을 저장하지 않고 빈 값으로 처리.
                return null;
            }
        }

        public static string Unprotect(string cipher)
        {
            if (string.IsNullOrEmpty(cipher))
                return cipher;

            try
            {
                var encrypted = Convert.FromBase64String(cipher);
                var bytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                // 손상되었거나 다른 환경에서 저장된 값이면 복호화 불가.
                return null;
            }
        }
    }
}
