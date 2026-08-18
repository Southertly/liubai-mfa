using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;

namespace LiubaiMfa.Windows.Services;

public sealed class ClipboardService
{
    private readonly Action<string> _writeText;

    public ClipboardService()
        : this(WriteText)
    {
    }

    internal ClipboardService(Action<string> writeText)
    {
        ArgumentNullException.ThrowIfNull(writeText);
        _writeText = writeText;
    }

    public bool TryCopyText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        try
        {
            _writeText(text);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static void WriteText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }
}
