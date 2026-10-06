using Microsoft.AspNetCore.DataProtection;

namespace API_Thanh_toan.Infrastructure;

public interface IDataProtectionHelper
{
    byte[] EncryptSecret(string plainSecret);
    string DecryptSecret(byte[] encryptedBytes);
}

public class DataProtectionHelper : IDataProtectionHelper
{
    private readonly IDataProtector _protector;

    public DataProtectionHelper(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("UtilityPaymentAPI.ClientSecretProtector");
    }

    public byte[] EncryptSecret(string plainSecret)
    {
        if (string.IsNullOrEmpty(plainSecret)) return Array.Empty<byte>();
        return _protector.Protect(System.Text.Encoding.UTF8.GetBytes(plainSecret));
    }

    public string DecryptSecret(byte[] encryptedBytes)
    {
        if (encryptedBytes == null || encryptedBytes.Length == 0) return string.Empty;
        var decryptedBytes = _protector.Unprotect(encryptedBytes);
        return System.Text.Encoding.UTF8.GetString(decryptedBytes);
    }
}
