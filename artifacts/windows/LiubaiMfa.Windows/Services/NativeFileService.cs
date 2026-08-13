using Microsoft.UI.Xaml;
using WinRT.Interop;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace LiubaiMfa.Windows.Services;

public sealed class NativeFileService
{
    private readonly nint _windowHandle;

    public NativeFileService(Window window)
    {
        _windowHandle = WindowNative.GetWindowHandle(window);
    }

    public async Task<bool> SaveBackupAsync(string contents)
    {
        var picker = new FileSavePicker
        {
            SuggestedFileName = $"Liubai-MFA-{DateTime.Now:yyyyMMdd-HHmmss}",
        };
        picker.FileTypeChoices.Add("留白 MFA 加密备份", new List<string> { ".mfa-backup" });
        InitializeWithWindow.Initialize(picker, _windowHandle);
        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return false;
        }

        await FileIO.WriteTextAsync(file, contents);
        return true;
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
