using System.Security.Cryptography;

namespace CeroLicenseManager.Services;

public class KeyManagementService(IWebHostEnvironment environment)
{
    private readonly string _keysPath = Path.Combine(environment.ContentRootPath, "keys");
    private string PrivateKeyPath => Path.Combine(_keysPath, "private_key.pem");
    private string PublicKeyPath => Path.Combine(_keysPath, "public_key.pem");

    public bool KeysExist() => File.Exists(PrivateKeyPath) && File.Exists(PublicKeyPath);

    public KeyStatus GetStatus()
    {
        if (!KeysExist()) return new KeyStatus(false, null, null, null, false, null);

        try
        {
            using var publicRsa = RSA.Create();
            publicRsa.ImportFromPem(File.ReadAllText(PublicKeyPath));
            var publicParameters = publicRsa.ExportParameters(false);
            var publicKeyPem = File.ReadAllText(PublicKeyPath);
            var fingerprint = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(publicKeyPem)));

            using var privateRsa = RSA.Create();
            privateRsa.ImportFromPem(File.ReadAllText(PrivateKeyPath));
            var privatePublicParameters = privateRsa.ExportParameters(false);
            var keysMatch =
                publicParameters.Modulus.AsSpan().SequenceEqual(privatePublicParameters.Modulus) &&
                publicParameters.Exponent.AsSpan().SequenceEqual(privatePublicParameters.Exponent);

            return new KeyStatus(true, publicRsa.KeySize, privateRsa.KeySize, fingerprint, keysMatch, null);
        }
        catch (Exception ex)
        {
            return new KeyStatus(true, null, null, null, false, ex.Message);
        }
    }

    public bool GenerateKeys()
    {
        if (KeysExist()) return false;
        Directory.CreateDirectory(_keysPath);
        using var rsa = RSA.Create(4096);
        File.WriteAllText(PrivateKeyPath, rsa.ExportRSAPrivateKeyPem());
        File.WriteAllText(PublicKeyPath, rsa.ExportRSAPublicKeyPem());
        return true;
    }

    public string GetPublicKey()
    {
        if (!File.Exists(PublicKeyPath)) throw new FileNotFoundException("No existe public_key.pem.");
        return File.ReadAllText(PublicKeyPath);
    }

    public string GetPrivateKeyPath()
    {
        if (!File.Exists(PrivateKeyPath)) throw new FileNotFoundException("No existe private_key.pem.");
        return PrivateKeyPath;
    }
}

public record KeyStatus(
    bool Exists,
    int? PublicKeyBits,
    int? PrivateKeyBits,
    string? PublicKeySha256,
    bool KeysMatch,
    string? Error);
