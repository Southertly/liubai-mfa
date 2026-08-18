namespace LiubaiMfa.Windows.ViewModels;

public enum AccountAction
{
    Edit,
    ViewSecret,
    Delete,
}

public static class AccountActionPolicy
{
    public static bool RequiresWindowsHello(AccountAction action) => action == AccountAction.ViewSecret;

    public static bool RequiresConfirmation(AccountAction action) => action == AccountAction.Delete;

    public static bool IsCopyShortcut(bool controlDown, bool shiftDown, bool altDown, bool windowsDown, bool wasKeyDown) =>
        controlDown && !shiftDown && !altDown && !windowsDown && !wasKeyDown;
}
