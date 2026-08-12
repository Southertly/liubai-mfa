using Microsoft.UI.Xaml;
using WinRT.Interop;
using Windows.Security.Credentials.UI;

namespace LiubaiMfa.Windows.Services;

public sealed class WindowsHelloService
{
    private readonly Window _window;

    public WindowsHelloService(Window window)
    {
        _window = window;
    }

    public async Task<bool> IsAvailableAsync()
    {
        return await UserConsentVerifier.CheckAvailabilityAsync() == UserConsentVerifierAvailability.Available;
    }

    public async Task<bool> VerifyAsync(string reason)
    {
        if (!await IsAvailableAsync())
        {
            return false;
        }

        var windowHandle = WindowNative.GetWindowHandle(_window);
        var result = await UserConsentVerifierInterop.RequestVerificationForWindowAsync(windowHandle, reason);
        return result == UserConsentVerificationResult.Verified;
    }
}
