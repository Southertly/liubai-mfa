using System.Text.Json.Serialization;

namespace LiubaiMfa.Core.Models;

public sealed record SecureEnvelope
{
    public const string AlgorithmName = "A256CBC-HS256";

    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;

    [JsonPropertyName("algorithm")]
    public string Algorithm { get; init; } = AlgorithmName;

    [JsonPropertyName("iv")]
    public required string Iv { get; init; }

    [JsonPropertyName("ciphertext")]
    public required string Ciphertext { get; init; }

    [JsonPropertyName("mac")]
    public required string Mac { get; init; }
}
