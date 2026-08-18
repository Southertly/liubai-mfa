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

    public static bool IsCopyShortcut(bool isCKey, bool controlDown, bool shiftDown, bool altDown) =>
        isCKey && controlDown && !shiftDown && !altDown;

    public static bool IsContextMenuShortcut(bool isF10Key, bool controlDown, bool shiftDown, bool altDown) =>
        isF10Key && shiftDown && !controlDown && !altDown;
}
