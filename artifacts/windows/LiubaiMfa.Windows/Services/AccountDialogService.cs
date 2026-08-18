using LiubaiMfa.Core;
using LiubaiMfa.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LiubaiMfa.Windows.Services;

public sealed record AccountEdit(string Id, string Issuer, string Account, string? Group, string? Color, long CreatedAt);

public sealed record AccountDraft(string Issuer, string Account, string Secret, string? Group);

public sealed class AccountDialogService
{
    private readonly WindowsHelloService _windowsHello;
    private readonly Func<ContentDialog, long, Task<ContentDialogResult>> _showDialog;
    private readonly Func<long, bool> _isSessionCurrent;
    private readonly Action<long, string, InfoBarSeverity> _showStatus;

    public AccountDialogService(
        WindowsHelloService windowsHello,
        Func<ContentDialog, long, Task<ContentDialogResult>> showDialog,
        Func<long, bool> isSessionCurrent,
        Action<long, string, InfoBarSeverity> showStatus)
    {
        _windowsHello = windowsHello;
        _showDialog = showDialog;
        _isSessionCurrent = isSessionCurrent;
        _showStatus = showStatus;
    }

    public Task<AccountDraft?> ShowAddAsync(XamlRoot root, long token) =>
        ShowDraftAsync(root, token, "添加账户", null);

    public Task<AccountDraft?> ShowImportConfirmationAsync(XamlRoot root, long token, AccountDraft draft) =>
        ShowDraftAsync(root, token, "确认二维码账户", draft);

    public async Task<AccountEdit?> ShowEditAsync(XamlRoot root, long token, AccountEdit existing)
    {
        if (!_isSessionCurrent(token))
        {
            return null;
        }

        var issuerBox = new TextBox { Header = "服务名称", Text = existing.Issuer, PlaceholderText = "例如 GitHub" };
        var accountBox = new TextBox { Header = "账户", Text = existing.Account, PlaceholderText = "例如 alice@example.com" };
        var groupBox = new TextBox { Header = "分组（可选）", Text = existing.Group ?? string.Empty };
        var dialog = CreateDialog(root, "编辑账户", new StackPanel { Spacing = 10, Children = { issuerBox, accountBox, groupBox } }, "保存", "取消");
        if (await _showDialog(dialog, token) != ContentDialogResult.Primary || !_isSessionCurrent(token))
        {
            return null;
        }

        var issuer = issuerBox.Text.Trim();
        var account = accountBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(account))
        {
            _showStatus(token, "请填写服务名称和账户。", InfoBarSeverity.Warning);
            return null;
        }

        return _isSessionCurrent(token)
            ? existing with { Issuer = issuer, Account = account, Group = NormalizeGroup(groupBox.Text) }
            : null;
    }

    public async Task<bool> ConfirmDeleteAsync(XamlRoot root, long token, AccountEdit account)
    {
        if (!_isSessionCurrent(token))
        {
            return false;
        }

        var content = new TextBlock
        {
            Text = $"确定删除“{account.Issuer} / {account.Account}”吗？此操作不会影响其他设备。",
            TextWrapping = TextWrapping.Wrap,
        };
        return await _showDialog(CreateDialog(root, "删除账户", content, "删除", "取消"), token) == ContentDialogResult.Primary
            && _isSessionCurrent(token);
    }

    public async Task ShowSecretAsync(
        XamlRoot root,
        long token,
        string accountId,
        Func<MfaAccount?> resolveCurrentAccount,
        Action<string> copyText)
    {
        if (!_isSessionCurrent(token))
        {
            return;
        }

        try
        {
            if (!await _windowsHello.VerifyAsync("验证身份以查看 MFA 密钥") || !_isSessionCurrent(token))
            {
                if (_isSessionCurrent(token))
                {
                    _showStatus(token, "系统身份验证未完成，操作已取消。", InfoBarSeverity.Warning);
                }

                return;
            }
        }
        catch (Exception)
        {
            if (_isSessionCurrent(token))
            {
                _showStatus(token, "Windows Hello 暂时不可用，请稍后重试。", InfoBarSeverity.Warning);
            }

            return;
        }

        var account = resolveCurrentAccount();
        if (account is null || !_isSessionCurrent(token))
        {
            return;
        }

        var secretText = new TextBlock
        {
            Text = FormatSecret(account.Secret),
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 18,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
        };
        var result = await _showDialog(CreateDialog(root, "MFA 密钥", secretText, "关闭", "复制密钥"), token);
        if (result != ContentDialogResult.Secondary || !_isSessionCurrent(token))
        {
            return;
        }

        var current = resolveCurrentAccount();
        if (current is null || current.Id != accountId || !_isSessionCurrent(token))
        {
            return;
        }

        copyText(current.Secret);
        _showStatus(token, "MFA 密钥已复制。", InfoBarSeverity.Success);
    }

    private async Task<AccountDraft?> ShowDraftAsync(XamlRoot root, long token, string title, AccountDraft? initial)
    {
        if (!_isSessionCurrent(token))
        {
            return null;
        }

        var issuerBox = new TextBox { Header = "服务名称", Text = initial?.Issuer ?? string.Empty, PlaceholderText = "例如 GitHub" };
        var accountBox = new TextBox { Header = "账户", Text = initial?.Account ?? string.Empty, PlaceholderText = "例如 alice@example.com" };
        var secretBox = new TextBox { Header = "Base32 密钥或 otpauth:// 地址", Text = initial?.Secret ?? string.Empty, PlaceholderText = "JBSW Y3DP EHPK 3PXP" };
        var groupBox = new TextBox { Header = "分组（可选）", Text = initial?.Group ?? string.Empty };
        var dialog = CreateDialog(root, title, new StackPanel { Spacing = 10, Children = { issuerBox, accountBox, secretBox, groupBox } }, "保存", "取消");
        if (await _showDialog(dialog, token) != ContentDialogResult.Primary || !_isSessionCurrent(token))
        {
            return null;
        }

        var issuer = issuerBox.Text.Trim();
        var account = accountBox.Text.Trim();
        var secret = secretBox.Text.Trim();
        if (OtpAuthUriParser.TryParse(secret, out var parsed) && parsed is not null)
        {
            issuer = string.IsNullOrWhiteSpace(issuer) ? parsed.Issuer : issuer;
            account = string.IsNullOrWhiteSpace(account) ? parsed.Account : account;
            secret = parsed.Secret;
        }
        else
        {
            secret = TotpService.NormalizeSecret(secret);
        }

        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(account))
        {
            _showStatus(token, "请填写服务名称和账户。", InfoBarSeverity.Warning);
            return null;
        }

        try
        {
            _ = TotpService.Generate(secret);
        }
        catch (FormatException)
        {
            _showStatus(token, "密钥格式无效，请检查 Base32 内容后重试。", InfoBarSeverity.Warning);
            return null;
        }

        return _isSessionCurrent(token) ? new AccountDraft(issuer, account, secret, NormalizeGroup(groupBox.Text)) : null;
    }

    private static ContentDialog CreateDialog(XamlRoot root, string title, object content, string primary, string secondary) => new()
    {
        XamlRoot = root,
        Title = title,
        Content = content,
        PrimaryButtonText = primary,
        SecondaryButtonText = secondary,
        DefaultButton = ContentDialogButton.Primary,
    };

    private static string? NormalizeGroup(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FormatSecret(string secret) => string.Join(' ', Enumerable.Range(0, (secret.Length + 3) / 4)
        .Select(index => secret.Substring(index * 4, Math.Min(4, secret.Length - index * 4))));
}
