using System.ComponentModel;
using System.Runtime.CompilerServices;
using LiubaiMfa.Core;
using LiubaiMfa.Core.Models;

namespace LiubaiMfa.Windows.ViewModels;

public sealed class AccountItemViewModel : INotifyPropertyChanged
{
    private string _code = "------";
    private int _secondsRemaining;

    public AccountItemViewModel(MfaAccount model)
    {
        Model = model;
        UpdateCode();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MfaAccount Model { get; private set; }
    public string Id => Model.Id;
    public string Issuer => Model.Issuer;
    public string Account => Model.Account;
    public string GroupLabel => string.IsNullOrWhiteSpace(Model.Group) ? "未分组" : Model.Group;

    public string Code
    {
        get => _code;
        private set => SetField(ref _code, value);
    }

    public int SecondsRemaining
    {
        get => _secondsRemaining;
        private set
        {
            if (SetField(ref _secondsRemaining, value))
            {
                OnPropertyChanged(nameof(RemainingLabel));
            }
        }
    }

    public string RemainingLabel => $"{SecondsRemaining} 秒";

    public void ReplaceModel(MfaAccount model)
    {
        Model = model;
        OnPropertyChanged(nameof(Issuer));
        OnPropertyChanged(nameof(Account));
        OnPropertyChanged(nameof(GroupLabel));
        UpdateCode();
    }

    public void UpdateCode()
    {
        try
        {
            Code = TotpService.Generate(Model.Secret);
            SecondsRemaining = TotpService.GetTimeRemaining();
        }
        catch (FormatException)
        {
            Code = "------";
            SecondsRemaining = 0;
        }
    }

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
