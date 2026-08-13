using System.ComponentModel;
using System.Runtime.CompilerServices;
using LiubaiMfa.Core;
using LiubaiMfa.Core.Models;

namespace LiubaiMfa.Windows.ViewModels;

public sealed class AccountItemViewModel : INotifyPropertyChanged
{
    private const int TotpPeriod = 30;
    private string _code = "------";
    private int _secondsRemaining;

    public AccountItemViewModel(MfaAccount model)
    {
        Model = model;
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MfaAccount Model { get; private set; }
    public string Id => Model.Id;
    public string Issuer => Model.Issuer;
    public string Account => Model.Account;
    public string GroupLabel => string.IsNullOrWhiteSpace(Model.Group) ? "未分组" : Model.Group;
    public ServiceIconDescriptor Icon => ServiceIconResolver.Resolve(Model.Issuer);
    public string FormattedCode => FormatCode(Code);
    public double ProgressValue => CalculateProgress(SecondsRemaining, TotpPeriod);
    public string AccessibleName => $"{DisplayIssuer} {DisplayAccount}，验证码 {FormattedCode}，剩余 {SecondsRemaining} 秒";

    public string Code
    {
        get => _code;
        private set
        {
            if (SetField(ref _code, value))
            {
                OnPropertyChanged(nameof(FormattedCode));
                OnPropertyChanged(nameof(AccessibleName));
            }
        }
    }

    public int SecondsRemaining
    {
        get => _secondsRemaining;
        private set
        {
            if (SetField(ref _secondsRemaining, value))
            {
                OnPropertyChanged(nameof(RemainingLabel));
                OnPropertyChanged(nameof(ProgressValue));
                OnPropertyChanged(nameof(AccessibleName));
            }
        }
    }

    public string RemainingLabel => $"{SecondsRemaining} 秒";

    public void ReplaceModel(MfaAccount model)
    {
        Model = model;
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(Id));
        OnPropertyChanged(nameof(Issuer));
        OnPropertyChanged(nameof(Account));
        OnPropertyChanged(nameof(GroupLabel));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(AccessibleName));
        Refresh();
    }

    public void Refresh(DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        try
        {
            Code = TotpService.Generate(Model.Secret, timestamp, TotpPeriod);
            SecondsRemaining = TotpService.GetTimeRemaining(timestamp, TotpPeriod);
        }
        catch (FormatException)
        {
            Code = "------";
            SecondsRemaining = 0;
        }
    }

    public void UpdateCode() => Refresh();

    public static string FormatCode(string code) => code.Length switch
    {
        6 => $"{code[..3]} {code[3..]}",
        8 => $"{code[..2]} {code[2..5]} {code[5..]}",
        _ => code,
    };

    public static double CalculateProgress(int secondsRemaining, int period)
    {
        if (period <= 0)
        {
            return 0;
        }

        return Math.Clamp((double)secondsRemaining / period, 0, 1);
    }

    private string DisplayIssuer => string.IsNullOrWhiteSpace(Issuer) ? "未知服务" : Issuer;

    private string DisplayAccount => string.IsNullOrWhiteSpace(Account) ? "未命名账户" : Account;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
