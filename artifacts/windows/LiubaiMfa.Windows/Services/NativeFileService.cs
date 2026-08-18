using Microsoft.UI.Xaml;
using WinRT.Interop;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace LiubaiMfa.Windows.Services;

public sealed class NativeFileService
{
    private readonly nint _windowHandle;
    private readonly BackupWriteCoordinator _backupWriteCoordinator = new();

    public NativeFileService(Window window)
    {
        _windowHandle = WindowNative.GetWindowHandle(window);
    }

    public async Task<StorageFile?> PickBackupSaveFileAsync()
    {
        var picker = new FileSavePicker
        {
            SuggestedFileName = $"Liubai-MFA-{DateTime.Now:yyyyMMdd-HHmmss}",
        };
        picker.FileTypeChoices.Add("留白 MFA 加密备份", new List<string> { ".mfa-backup" });
        InitializeWithWindow.Initialize(picker, _windowHandle);
        return await picker.PickSaveFileAsync();
    }

    public async Task<BackupWriteResult> WriteBackupAsync(
        StorageFile target,
        string contents,
        CancellationToken cancellationToken,
        Func<bool> isSessionCurrent)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentNullException.ThrowIfNull(isSessionCurrent);

        StorageFolder? parent = null;
        var temporaryName = $".liubai-mfa-backup-{Guid.NewGuid():N}.tmp";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            parent = await target.GetParentAsync().AsTask(cancellationToken)
                ?? throw new IOException("无法确定备份目标目录。");
            var temporaryFile = await parent.CreateFileAsync(temporaryName, CreationCollisionOption.FailIfExists).AsTask(cancellationToken);
            return await _backupWriteCoordinator.WriteAndReplaceAsync(
                writeToken => FileIO.WriteTextAsync(temporaryFile, contents).AsTask(writeToken),
                () => temporaryFile.MoveAndReplaceAsync(target).AsTask(CancellationToken.None),
                isSessionCurrent,
                cancellationToken);
        }
        finally
        {
            if (parent is not null)
            {
                await DeleteTemporaryFileAsync(parent, temporaryName);
            }
        }
    }

    private static async Task DeleteTemporaryFileAsync(StorageFolder parent, string temporaryName)
    {
        try
        {
            if (await parent.TryGetItemAsync(temporaryName).AsTask(CancellationToken.None) is StorageFile temporaryFile)
            {
                await temporaryFile.DeleteAsync(StorageDeleteOption.PermanentDelete).AsTask(CancellationToken.None);
            }
        }
        catch (Exception)
        {
            // 临时文件名唯一，且不会指向用户选择的目标文件。
        }
    }

    public async Task<string?> OpenBackupAsync()
    {
        var picker = CreateOpenPicker(new[] { ".mfa-backup", ".json", ".txt" });
        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return null;
        }

        var properties = await file.GetBasicPropertiesAsync();
        if (properties.Size > 5 * 1024 * 1024)
        {
            throw new FormatException("备份文件过大。");
        }

        return await FileIO.ReadTextAsync(file);
    }

    public async Task<StorageFile?> OpenQrImageAsync()
    {
        var picker = CreateOpenPicker(new[] { ".png", ".jpg", ".jpeg", ".bmp" });
        return await picker.PickSingleFileAsync();
    }

    private FileOpenPicker CreateOpenPicker(IEnumerable<string> extensions)
    {
        var picker = new FileOpenPicker();
        foreach (var extension in extensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        InitializeWithWindow.Initialize(picker, _windowHandle);
        return picker;
    }
}
