using System.Text.Json.Serialization;

namespace LiubaiMfa.Core.Models;

public sealed record SecureBackupPayload
{
    [JsonPropertyName("version")]
    public int Version { get; init; } = 2;

    [JsonPropertyName("created")]
    public long Created { get; init; }

    [JsonPropertyName("encrypted")]
    public bool Encrypted { get; init; } = true;

    [JsonPropertyName("keyFormat")]
    public string KeyFormat { get; init; } = "base64url-256";

    [JsonPropertyName("data")]
    public required SecureEnvelope Data { get; init; }
}
