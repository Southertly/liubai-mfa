using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace LiubaiMfa.Windows.Services;

internal sealed class SnippingToolCaptureService
{
    private readonly Func<Uri, Task<bool>> _launchAsync;
    private readonly Func<string, Task<StorageFile?>> _redeemAsync;

    public SnippingToolCaptureService()
        : this(
            uri => Launcher.LaunchUriAsync(uri).AsTask(),
            async token => await SharedStorageAccessManager.RedeemTokenForFileAsync(token))
    {
    }

    internal SnippingToolCaptureService(
        Func<Uri, Task<bool>> launchAsync,
        Func<string, Task<StorageFile?>> redeemAsync)
    {
        ArgumentNullException.ThrowIfNull(launchAsync);
        ArgumentNullException.ThrowIfNull(redeemAsync);

        _launchAsync = launchAsync;
        _redeemAsync = redeemAsync;
    }

    public async Task<bool> LaunchAsync(Guid correlationId)
    {
        var launchTask = _launchAsync(SnippingToolProtocol.CreateCaptureUri(correlationId));
        if (launchTask is null)
        {
            throw new InvalidOperationException("截图工具启动操作未返回有效结果。");
        }

        return await launchTask;
    }

    public async Task<StorageFile> RedeemAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("截图文件访问令牌不能为空。", nameof(token));
        }

        var redeemTask = _redeemAsync(token);
        if (redeemTask is null)
        {
            throw new InvalidOperationException("截图文件兑换操作未返回有效结果。");
        }

        return await redeemTask
            ?? throw new InvalidOperationException("截图文件兑换操作未返回有效文件。");
    }
}
