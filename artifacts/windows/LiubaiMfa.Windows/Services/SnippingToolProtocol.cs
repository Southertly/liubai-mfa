using System.Globalization;

namespace LiubaiMfa.Windows.Services;

internal readonly record struct SnippingToolResponse(
    Guid CorrelationId,
    int StatusCode,
    string? FileAccessToken);

internal static class SnippingToolProtocol
{
    private const string CaptureScheme = "ms-screenclip";
    private const string CaptureHost = "capture";
    private const string CapturePath = "/image";
    private const string ResponseScheme = "liubai-mfa";
    private const string ResponseHost = "capture-response";
    private const string RedirectUri = "liubai-mfa://capture-response";

    public static Uri CreateCaptureUri(Guid correlationId)
    {
        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("截图请求关联 ID 不能为空。", nameof(correlationId));
        }

        var queryParameters = new[]
        {
            EncodeQueryParameter("rectangle"),
            EncodeQueryParameter("enabledModes", "RectangleSnip"),
            EncodeQueryParameter("user-agent", "LiubaiMFA"),
            EncodeQueryParameter("api-version", "1.2"),
            EncodeQueryParameter("redirect-uri", RedirectUri),
            EncodeQueryParameter("x-request-correlation-id", correlationId.ToString("D", CultureInfo.InvariantCulture)),
        };

        var builder = new UriBuilder
        {
            Scheme = CaptureScheme,
            Host = CaptureHost,
            Path = CapturePath,
            Query = string.Join('&', queryParameters),
        };
        return builder.Uri;
    }

    public static bool TryParseResponse(Uri? uri, out SnippingToolResponse response)
    {
        response = default;
        if (!IsResponseEndpoint(uri))
        {
            return false;
        }

        string? statusCodeValue = null;
        string? correlationIdValue = null;
        string? fileAccessToken = null;
        var criticalParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var query = uri!.Query.Length == 0 ? string.Empty : uri.Query[1..];
            foreach (var parameter in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var separatorIndex = parameter.IndexOf('=');
                var encodedName = separatorIndex >= 0 ? parameter[..separatorIndex] : parameter;
                var encodedValue = separatorIndex >= 0 ? parameter[(separatorIndex + 1)..] : string.Empty;
                var name = Uri.UnescapeDataString(encodedName);

                if (!IsCriticalParameter(name))
                {
                    continue;
                }

                if (!criticalParameters.Add(name))
                {
                    return false;
                }

                var value = Uri.UnescapeDataString(encodedValue);
                if (name.Equals("code", StringComparison.OrdinalIgnoreCase))
                {
                    statusCodeValue = value;
                }
                else if (name.Equals("x-request-correlation-id", StringComparison.OrdinalIgnoreCase))
                {
                    correlationIdValue = value;
                }
                else
                {
                    fileAccessToken = value;
                }
            }
        }
        catch (UriFormatException)
        {
            return false;
        }

        if (!int.TryParse(statusCodeValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var statusCode)
            || !Guid.TryParse(correlationIdValue, out var correlationId)
            || correlationId == Guid.Empty)
        {
            return false;
        }

        response = new SnippingToolResponse(correlationId, statusCode, fileAccessToken);
        return true;
    }

    private static bool IsResponseEndpoint(Uri? uri) =>
        uri is { IsAbsoluteUri: true }
        && uri.Scheme.Equals(ResponseScheme, StringComparison.OrdinalIgnoreCase)
        && uri.Host.Equals(ResponseHost, StringComparison.OrdinalIgnoreCase)
        && uri.UserInfo.Length == 0
        && uri.IsDefaultPort
        && uri.Fragment.Length == 0
        && uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped).Length == 0;

    private static bool IsCriticalParameter(string name) =>
        name.Equals("code", StringComparison.OrdinalIgnoreCase)
        || name.Equals("x-request-correlation-id", StringComparison.OrdinalIgnoreCase)
        || name.Equals("file-access-token", StringComparison.OrdinalIgnoreCase);

    private static string EncodeQueryParameter(string name, string? value = null)
    {
        var encodedName = Uri.EscapeDataString(name);
        return value is null
            ? encodedName
            : $"{encodedName}={Uri.EscapeDataString(value)}";
    }
}
