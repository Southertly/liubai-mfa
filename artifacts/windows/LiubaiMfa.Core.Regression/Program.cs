using System.Security.Cryptography;
using System.Text.Json;
using LiubaiMfa.Core;
using LiubaiMfa.Core.Models;

var checks = new List<(string Name, Action Run)>
{
    ("RFC 6238 TOTP vectors", VerifyTotpVectors),
    ("otpauth URI parsing", VerifyOtpAuthParsing),
    ("mobile v2 crypto fixture", VerifyMobileCryptoFixture),
    ("backup round trip and tamper rejection", VerifyBackupRoundTrip),
    ("backup validation limits", VerifyBackupValidation),
};

foreach (var check in checks)
{
    check.Run();
    Console.WriteLine($"PASS {check.Name}");
}

Console.WriteLine($"{checks.Count} Windows core security checks passed.");

static void VerifyTotpVectors()
{
    const string secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
    var vectors = new Dictionary<long, string>
    {
        [59] = "94287082",
        [1_111_111_109] = "07081804",
        [1_111_111_111] = "14050471",
        [1_234_567_890] = "89005924",
        [2_000_000_000] = "69279037",
        [20_000_000_000] = "65353130",
    };

    foreach (var (unixSeconds, expected) in vectors)
    {
        var actual = TotpService.Generate(secret, DateTimeOffset.FromUnixTimeSeconds(unixSeconds), digits: 8);
        Assert(actual == expected, $"TOTP mismatch at {unixSeconds}: {actual}");
    }
}

static void VerifyOtpAuthParsing()
{
    const string uri = "otpauth://totp/Example%20Co:alice%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=Example%20Co";
    Assert(OtpAuthUriParser.TryParse(uri, out var parsed), "Expected URI to parse");
    if (parsed is null)
    {
        throw new InvalidOperationException("Parsed account missing");
    }
    Assert(parsed.Issuer == "Example Co", "Issuer mismatch");
    Assert(parsed.Account == "alice@example.com", "Account mismatch");
    Assert(parsed.Secret == "JBSWY3DPEHPK3PXP", "Secret mismatch");
    Assert(!OtpAuthUriParser.TryParse("https://example.com", out _), "Non-otpauth URI accepted");
}

static void VerifyMobileCryptoFixture()
{
    const string key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY";
    var iv = Enumerable.Range(0, 16).Select(value => (byte)value).ToArray();
    var envelope = BackupCrypto.Encrypt("[{\"secret\":\"JBSWY3DPEHPK3PXP\"}]", key, iv);

    Assert(envelope.Iv == "AAECAwQFBgcICQoLDA0ODw==", "Fixture IV mismatch");
    Assert(envelope.Ciphertext == "/2vKZq8zRtrA+NRkjdZ4CVLq/+awOJG63aEVul2py6s=", "Fixture ciphertext mismatch");
    Assert(envelope.Mac == "29ab61684fb57189e62a61cce22f229ecee7e1c90a8e17678cc6627b7fd63938", "Fixture MAC mismatch");
    Assert(BackupCrypto.Decrypt(envelope, key).Contains("JBSWY3DPEHPK3PXP", StringComparison.Ordinal), "Fixture decrypt failed");
}

static void VerifyBackupRoundTrip()
{
    var accounts = new[]
    {
        new MfaAccount
        {
            Id = "test-1",
            Issuer = "Example",
            Account = "alice@example.com",
            Secret = "JBSWY3DPEHPK3PXP",
            Group = "Work",
            Color = "#5B8DEF",
            CreatedAt = 1_700_000_000_000,
        },
    };

    var backup = BackupCrypto.CreateBackup(accounts, out var key);
    Assert(!backup.Contains(accounts[0].Secret, StringComparison.Ordinal), "Backup leaked plaintext secret");
    Assert(BackupCrypto.IsValidRecoveryKey(key), "Generated key invalid");
    Assert(BackupCrypto.NormalizeRecoveryKey(BackupCrypto.FormatRecoveryKey(key)) == key, "Formatted key did not round-trip");

    var restored = BackupCrypto.ReadBackup(backup, key);
    Assert(restored.Count == 1 && restored[0] == accounts[0], "Backup round-trip mismatch");
    AssertThrows<CryptographicException>(() => BackupCrypto.ReadBackup(backup, BackupCrypto.GenerateRecoveryKey()));

    var payload = JsonSerializer.Deserialize<SecureBackupPayload>(backup)!;
    var ciphertext = Convert.FromBase64String(payload.Data.Ciphertext);
    ciphertext[0] ^= 0x01;
    var tampered = payload with
    {
        Data = payload.Data with { Ciphertext = Convert.ToBase64String(ciphertext) },
    };
    var tamperedJson = JsonSerializer.Serialize(tampered);
    AssertThrows<CryptographicException>(() => BackupCrypto.ReadBackup(tamperedJson, key));
}

static void VerifyBackupValidation()
{
    var invalidAccount = new[]
    {
        new MfaAccount
        {
            Id = "invalid-1",
            Issuer = "Example",
            Account = "broken@example.com",
            Secret = "NOT-BASE32!",
            CreatedAt = 1_700_000_000_000,
        },
    };
    var backup = BackupCrypto.CreateBackup(invalidAccount, out var key);
    AssertThrows<FormatException>(() => BackupCrypto.ReadBackup(backup, key));
    AssertThrows<FormatException>(() => BackupCrypto.ReadBackup(new string('x', 5 * 1024 * 1024 + 1), key));
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertThrows<TException>(Action action) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}
