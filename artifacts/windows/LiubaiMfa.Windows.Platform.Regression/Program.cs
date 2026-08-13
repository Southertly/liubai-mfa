using LiubaiMfa.Core.Models;
using LiubaiMfa.Windows.Services;
using LiubaiMfa.Windows.ViewModels;

var checks = new List<(string Name, Func<Task> Run)>
{
    ("搜索覆盖服务、账户和分组", () => RunSync(VerifySearch)),
    ("分组过滤与搜索叠加", () => RunSync(VerifyGroupAndSearch)),
    ("排序忽略大小写且稳定", () => RunSync(VerifyStableSort)),
    ("分组计数包含全部和非空分组", () => RunSync(VerifyGroupCounts)),
    ("搜索与分组会规范化空白文本", () => RunSync(VerifyWhitespaceNormalization)),
    ("搜索可处理缺失的服务和账户", () => RunSync(VerifyNullSearchFields)),
    ("主视图模型派生搜索和分组状态", VerifyMainViewModelDerivedStateAsync),
    ("主视图模型删除后更新分组并回退选择", VerifyMainViewModelGroupDeletionAsync),
    ("主视图模型加载失败时禁止写入", VerifyMainViewModelLoadFailureAsync),
};

foreach (var check in checks)
{
    await check.Run();
    Console.WriteLine($"PASS {check.Name}");
}

Console.WriteLine($"{checks.Count} 项 Windows 平台账户查询回归检查通过。");

static void VerifySearch()
{
    var source = CreateFixture();

    AssertIds(AccountQuery.FilterAndSort(source, "alice", null), "1");
    AssertIds(AccountQuery.FilterAndSort(source, "google", null), "2");
    AssertIds(AccountQuery.FilterAndSort(source, "工作", null), "3", "4");
}

static void VerifyGroupAndSearch()
{
    var source = CreateFixture();

    AssertIds(AccountQuery.FilterAndSort(source, null, "工作"), "3", "4");
    AssertIds(AccountQuery.FilterAndSort(source, "charlie", "工作"), "3", "4");
    AssertIds(AccountQuery.FilterAndSort(source, "alice", "工作"));
}

static void VerifyStableSort()
{
    var source = CreateFixture();

    AssertIds(AccountQuery.FilterAndSort(source, null, null), "1", "3", "4", "2");
}

static void VerifyGroupCounts()
{
    var counts = AccountQuery.BuildGroupCounts(CreateFixture());

    Assert(counts.Count == 3, "分组计数数量错误。");
    Assert(counts[0] == new GroupCount(null, "全部", 4), "全部计数错误。");
    Assert(counts[1] == new GroupCount("个人", "个人", 1), "个人分组计数错误。");
    Assert(counts[2] == new GroupCount("工作", "工作", 2), "工作分组计数错误。");
}

static void VerifyWhitespaceNormalization()
{
    var source = CreateFixture();

    AssertIds(AccountQuery.FilterAndSort(source, "   ", null), "1", "3", "4", "2");
    AssertIds(AccountQuery.FilterAndSort(source, " alice ", null), "1");
    AssertIds(AccountQuery.FilterAndSort(source, null, "   "), "1", "3", "4", "2");

    var counts = AccountQuery.BuildGroupCounts(
    [
        new MfaAccount { Id = "5", Issuer = "Example", Account = "one", Secret = "E", Group = " Work ", CreatedAt = 5 },
        new MfaAccount { Id = "6", Issuer = "Example", Account = "two", Secret = "F", Group = "work", CreatedAt = 6 },
        new MfaAccount { Id = "7", Issuer = "Example", Account = "three", Secret = "G", Group = "   ", CreatedAt = 7 },
    ]);

    Assert(counts.Count == 2, "空白分组不应计入分组计数。");
    Assert(counts[1] == new GroupCount("Work", "Work", 2), "分组计数应合并大小写和空白差异。");
}

static void VerifyNullSearchFields()
{
    var missingIssuer = new MfaAccount
    {
        Id = "8", Issuer = null!, Account = "alice", Secret = "H", CreatedAt = 8,
    };
    var missingAccount = new MfaAccount
    {
        Id = "9", Issuer = "Example", Account = null!, Secret = "I", CreatedAt = 9,
    };

    AssertIds(AccountQuery.FilterAndSort([missingIssuer], "alice", null), "8");
    AssertIds(AccountQuery.FilterAndSort([missingAccount], "missing", null));
}

static async Task VerifyMainViewModelDerivedStateAsync()
{
    var viewModel = new MainViewModel(new FakeAccountRepository(CreateViewModelFixture()));
    var changedProperties = new HashSet<string?>();
    viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

    await viewModel.LoadAsync();
    Assert(changedProperties.Contains(nameof(MainViewModel.HasAccounts)), "加载后未通知账户状态。");
    Assert(changedProperties.Contains(nameof(MainViewModel.HasVisibleAccounts)), "加载后未通知可见账户状态。");

    viewModel.SearchText = "alice";
    AssertIds(viewModel.VisibleAccounts.Select(item => item.Model), "1");
    Assert(changedProperties.Contains(nameof(MainViewModel.SearchText)), "搜索文本变更未通知。");

    viewModel.SearchText = string.Empty;
    viewModel.SelectedGroup = "工作";
    AssertIds(viewModel.VisibleAccounts.Select(item => item.Model), "3");
    Assert(changedProperties.Contains(nameof(MainViewModel.SelectedGroup)), "分组选择变更未通知。");
    Assert(viewModel.Groups[0].DisplayText == "全部 (4)", "全部筛选器计数错误。");
}

static async Task VerifyMainViewModelGroupDeletionAsync()
{
    var viewModel = new MainViewModel(new FakeAccountRepository(CreateViewModelFixture()));

    await viewModel.LoadAsync();
    viewModel.SelectedGroup = "工作";
    await viewModel.DeleteAsync("3");
    Assert(viewModel.Groups[0].Count == 3, "删除后全部分组计数未更新。");
    Assert(viewModel.SelectedGroup is null, "已删除的分组仍被选中。");
    Assert(viewModel.Groups.All(group => group.Value != "工作"), "空分组不应继续显示。");
}

static async Task VerifyMainViewModelLoadFailureAsync()
{
    var repository = new FakeAccountRepository(loadException: new IOException("模拟读取失败"));
    var viewModel = new MainViewModel(repository);
    var changedProperties = new HashSet<string?>();
    viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

    await AssertThrowsAsync<IOException>(() => viewModel.LoadAsync());
    Assert(viewModel.HasLoadFailure, "加载失败状态未保留。");
    Assert(changedProperties.Contains(nameof(MainViewModel.HasLoadFailure)), "加载失败状态变更未通知。");
    Assert(!viewModel.HasAccounts, "加载失败不应被视为已有账户。");
    Assert(!viewModel.HasVisibleAccounts, "加载失败后不应显示账户。");

    await AssertThrowsAsync<InvalidOperationException>(() => viewModel.AddAsync(CreateFixture()[0]));
    await AssertThrowsAsync<InvalidOperationException>(() => viewModel.UpdateAsync(CreateFixture()[0]));
    await AssertThrowsAsync<InvalidOperationException>(() => viewModel.DeleteAsync("1"));
    await AssertThrowsAsync<InvalidOperationException>(() => viewModel.ImportAsync(CreateFixture()));
    Assert(repository.SaveCallCount == 0, "加载失败后不应保存账户数据。");
}

static MfaAccount[] CreateFixture() =>
[
    new MfaAccount { Id = "1", Issuer = "GitHub", Account = "alice", Secret = "A", Group = "个人", CreatedAt = 1 },
    new MfaAccount { Id = "2", Issuer = "Google", Account = "bob", Secret = "B", Group = " ", CreatedAt = 2 },
    new MfaAccount { Id = "3", Issuer = "GitHub", Account = "charlie", Secret = "C", Group = " 工作 ", CreatedAt = 3 },
    new MfaAccount { Id = "4", Issuer = "github", Account = "Charlie", Secret = "D", Group = "工作", CreatedAt = 4 },
];

static MfaAccount[] CreateViewModelFixture() =>
[
    new MfaAccount { Id = "1", Issuer = "GitHub", Account = "alice", Secret = "A", Group = "个人", CreatedAt = 1 },
    new MfaAccount { Id = "2", Issuer = "Google", Account = "bob", Secret = "B", Group = " ", CreatedAt = 2 },
    new MfaAccount { Id = "3", Issuer = "GitHub", Account = "charlie", Secret = "C", Group = "工作", CreatedAt = 3 },
    new MfaAccount { Id = "4", Issuer = "Microsoft", Account = "dora", Secret = "D", Group = "个人", CreatedAt = 4 },
];

static void AssertIds(IEnumerable<MfaAccount> actual, params string[] expected)
{
    var actualIds = actual.Select(account => account.Id).ToArray();
    Assert(actualIds.SequenceEqual(expected), $"账户 ID 不匹配。实际：{string.Join(',', actualIds)}；预期：{string.Join(',', expected)}。");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static Task RunSync(Action action)
{
    action();
    return Task.CompletedTask;
}

static async Task AssertThrowsAsync<TException>(Func<Task> action)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"预期抛出 {typeof(TException).Name}。");
}

internal sealed class FakeAccountRepository : IAccountRepository
{
    private readonly IReadOnlyList<MfaAccount> _accounts;
    private readonly Exception? _loadException;

    public FakeAccountRepository(IReadOnlyList<MfaAccount>? accounts = null, Exception? loadException = null)
    {
        _accounts = accounts ?? Array.Empty<MfaAccount>();
        _loadException = loadException;
    }

    public int SaveCallCount { get; private set; }

    public Task<IReadOnlyList<MfaAccount>> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_loadException is not null)
        {
            return Task.FromException<IReadOnlyList<MfaAccount>>(_loadException);
        }

        return Task.FromResult(_accounts);
    }

    public Task SaveAsync(IReadOnlyCollection<MfaAccount> accounts, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SaveCallCount++;
        return Task.CompletedTask;
    }
}
