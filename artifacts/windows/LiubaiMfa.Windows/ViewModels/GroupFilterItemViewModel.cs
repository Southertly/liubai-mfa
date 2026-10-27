namespace LiubaiMfa.Windows.ViewModels;

public sealed class GroupFilterItemViewModel
{
    public GroupFilterItemViewModel(string? value, string label, int count)
    {
        Value = value;
        Label = label;
        Count = count;
    }

    public string? Value { get; }

    public string Label { get; }

    public int Count { get; }

    public string DisplayText => $"{Label} ({Count})";
}
