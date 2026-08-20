using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace LiubaiMfa.Windows.Services;

internal sealed class SnippingToolCaptureService
{
    private readonly Func<Uri, Task<bool>> _launchAsync;
    private readonly Func<string, Task<StorageFile>> _redeemAsync;

    public SnippingToolCaptureService()
        : this(
            uri => Launcher.LaunchUriAsync(uri).AsTask(),
            token => SharedStorageAccessManager.RedeemTokenForFileAsync(token).AsTask())
    {
    }

    internal SnippingToolCaptureService(
        Func<Uri, Task<bool>> launchAsync,
        Func<string, Task<StorageFile>> redeemAsync)
    {
        ArgumentNullException.ThrowIfNull(launchAsync);
        ArgumentNullException.ThrowIfNull(redeemAsync);

        _launchAsync = launchAsync;
        _redeemAsync = redeemAsync;
    }

    public Task<bool> LaunchAsync(Guid correlationId) =>
        _launchAsync(SnippingToolProtocol.CreateCaptureUri(correlationId));

    public Task<StorageFile> RedeemAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("截图文件访问令牌不能为空。", nameof(token));
        }

        return _redeemAsync(token);
    }
}
