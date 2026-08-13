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
    ("添加保存失败不留下幽灵状态", VerifyAddSaveFailureDoesNotChangeStateAsync),
    ("更新保存取消不留下幽灵状态", VerifyUpdateSaveCancellationDoesNotChangeStateAsync),
    ("删除保存失败不留下幽灵状态", VerifyDeleteSaveFailureDoesNotChangeStateAsync),
    ("导入保存取消不留下幽灵状态", VerifyImportSaveCancellationDoesNotChangeStateAsync),
    ("无可导入账户时不保存", VerifyImportWithoutNewAccountsDoesNotSaveAsync),
    ("并发添加不会丢失已保存账户", VerifyConcurrentAddsAreSerializedAsync),
    ("并发添加重复账户只允许一个成功", VerifyConcurrentDuplicateAddsAllowOnlyOneAsync),
    ("卸载后保存完成不得回填账户状态", VerifyUnloadPreventsInFlightWritePublicationAsync),
    ("卸载后排队写不得保存或发布账户状态", VerifyUnloadCancelsQueuedWriteAsync),
    ("等待写门的外部取消必须传播且保持可用", VerifyExternalCancellationWhileWaitingForWriteGateAsync),
    ("仓库保存的调用方取消必须传播", VerifyRepositoryCancellationPropagatesAsync),
    ("锁定期间导入返回中性结果", VerifyLockedImportReturnsNeutralResultAsync),
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

static async Task VerifyAddSaveFailureDoesNotChangeStateAsync()
{
    var repository = new FakeAccountRepository(CreateViewModelFixture()) { ThrowOnSave = true };
    var viewModel = new MainViewModel(repository);
    await viewModel.LoadAsync();
    var expectedState = CaptureState(viewModel);

    await AssertThrowsAsync<IOException>(() => viewModel.AddAsync(CreateNewAccount()));
    AssertStateUnchanged(expectedState, viewModel, repository, "添加保存失败");
    Assert(repository.SaveCallCount == 1, "添加失败保存调用次数错误。");

    repository.ThrowOnSave = false;
    await viewModel.AddAsync(CreateNewAccount());
    Assert(viewModel.AccountsSnapshot.Count == expectedState.Accounts.Count + 1, "添加重试未提交账户。");
    Assert(repository.LastSuccessfulSnapshot.SequenceEqual(viewModel.AccountsSnapshot), "添加重试后持久化快照不一致。");
}

static async Task VerifyUpdateSaveCancellationDoesNotChangeStateAsync()
{
    var repository = new FakeAccountRepository(CreateViewModelFixture()) { CancelOnSave = true };
    var viewModel = new MainViewModel(repository);
    await viewModel.LoadAsync();
    var expectedState = CaptureState(viewModel);
    var updated = expectedState.Accounts.Single(account => account.Id == "1") with { Account = "alice-updated" };

    await AssertThrowsAsync<OperationCanceledException>(() => viewModel.UpdateAsync(updated));
    AssertStateUnchanged(expectedState, viewModel, repository, "更新保存取消");
    Assert(repository.SaveCallCount == 1, "更新取消保存调用次数错误。");

    repository.CancelOnSave = false;
    await viewModel.UpdateAsync(updated);
    Assert(viewModel.AccountsSnapshot.Single(account => account.Id == "1").Account == "alice-updated", "更新重试未提交账户。");
    Assert(repository.LastSuccessfulSnapshot.SequenceEqual(viewModel.AccountsSnapshot), "更新重试后持久化快照不一致。");
}

static async Task VerifyDeleteSaveFailureDoesNotChangeStateAsync()
{
    var repository = new FakeAccountRepository(CreateViewModelFixture()) { ThrowOnSave = true };
    var viewModel = new MainViewModel(repository);
    await viewModel.LoadAsync();
    var expectedState = CaptureState(viewModel);

    await AssertThrowsAsync<IOException>(() => viewModel.DeleteAsync("3"));
    AssertStateUnchanged(expectedState, viewModel, repository, "删除保存失败");
    Assert(repository.SaveCallCount == 1, "删除失败保存调用次数错误。");

    repository.ThrowOnSave = false;
    await viewModel.DeleteAsync("3");
    Assert(viewModel.AccountsSnapshot.All(account => account.Id != "3"), "删除重试未提交账户。");
    Assert(repository.LastSuccessfulSnapshot.SequenceEqual(viewModel.AccountsSnapshot), "删除重试后持久化快照不一致。");
}

static async Task VerifyImportSaveCancellationDoesNotChangeStateAsync()
{
    var repository = new FakeAccountRepository(CreateViewModelFixture()) { CancelOnSave = true };
    var viewModel = new MainViewModel(repository);
    await viewModel.LoadAsync();
    var expectedState = CaptureState(viewModel);

    await AssertThrowsAsync<OperationCanceledException>(() => viewModel.ImportAsync([CreateNewAccount()]));
    AssertStateUnchanged(expectedState, viewModel, repository, "导入保存取消");
    Assert(repository.SaveCallCount == 1, "导入取消保存调用次数错误。");

    repository.CancelOnSave = false;
    var result = await viewModel.ImportAsync([CreateNewAccount()]);
    Assert(result == (1, 0), "导入重试的导入和跳过数量错误。");
    Assert(repository.LastSuccessfulSnapshot.SequenceEqual(viewModel.AccountsSnapshot), "导入重试后持久化快照不一致。");
}

static async Task VerifyImportWithoutNewAccountsDoesNotSaveAsync()
{
    var repository = new FakeAccountRepository(CreateViewModelFixture());
    var viewModel = new MainViewModel(repository);
    await viewModel.LoadAsync();
    var expectedState = CaptureState(viewModel);

    var result = await viewModel.ImportAsync([CreateViewModelFixture()[0]]);
    Assert(result == (0, 1), "重复账户的导入和跳过数量错误。");
    Assert(repository.SaveCallCount == 0, "无可导入账户时不应保存。");
    AssertStateUnchanged(expectedState, viewModel, repository, "无可导入账户");
}

static async Task VerifyConcurrentAddsAreSerializedAsync()
{
    var repository = new FakeAccountRepository(CreateViewModelFixture()) { BlockFirstSave = true };
    var viewModel = new MainViewModel(repository);
    await viewModel.LoadAsync();

    var firstAdd = viewModel.AddAsync(CreateNewAccount());
    await repository.FirstSaveStarted;
    var secondAdd = viewModel.AddAsync(CreateAdditionalNewAccount());
    var secondSaveOvertookFirst = repository.SecondSaveStarted.IsCompleted;
    repository.ReleaseFirstSave();
    await Task.WhenAll(firstAdd, secondAdd);

    Assert(!secondSaveOvertookFirst, "第二个保存不应越过第一个保存。");
    AssertAccountIds(viewModel.AccountsSnapshot, "1", "2", "3", "4", "5", "6");
    AssertAccountIds(viewModel.VisibleAccounts.Select(account => account.Model), "1", "2", "3", "4", "5", "6");
    AssertAccountIds(repository.LastSuccessfulSnapshot, "1", "2", "3", "4", "5", "6");
    Assert(repository.SaveCallCount == 2, "并发添加的保存调用次数错误。");
}

static async Task VerifyConcurrentDuplicateAddsAllowOnlyOneAsync()
{
    var repository = new FakeAccountRepository(CreateViewModelFixture()) { BlockFirstSave = true };
    var viewModel = new MainViewModel(repository);
    await viewModel.LoadAsync();

    var firstAdd = viewModel.AddAsync(CreateNewAccount());
    await repository.FirstSaveStarted;
    var secondAdd = viewModel.AddAsync(CreateNewAccount());
    repository.ReleaseFirstSave();

    await firstAdd;
    await AssertThrowsAsync<InvalidOperationException>(() => secondAdd);
    Assert(viewModel.AccountsSnapshot.Count == 5, "并发重复添加后账户数量错误。");
    AssertAccountIds(viewModel.AccountsSnapshot, "1", "2", "3", "4", "5");
    AssertAccountIds(repository.LastSuccessfulSnapshot, "1", "2", "3", "4", "5");
    Assert(repository.SaveCallCount == 1, "并发重复添加不应进行第二次保存。");
}

static async Task VerifyUnloadPreventsInFlightWritePublicationAsync()
{
    var repository = new FakeAccountRepository(CreateViewModelFixture()) { BlockFirstSave = true };
    var viewModel = new MainViewModel(repository);
    await viewModel.LoadAsync();
    viewModel.SelectedGroup = "工作";
    var selectedGroupCleared = false;
    viewModel.PropertyChanged += (_, args) =>
        selectedGroupCleared |= args.PropertyName == nameof(MainViewModel.SelectedGroup);

    var add = viewModel.AddAsync(CreateNewAccount());
    await repository.FirstSaveStarted;
    viewModel.Unload();
    AssertUnloaded(viewModel, "卸载后立即");
    Assert(selectedGroupCleared, "卸载未通知分组选择已清空。");

    repository.ReleaseFirstSave();
    await AssertThrowsAsync<OperationCanceledException>(() => add);
    AssertUnloaded(viewModel, "保存完成后");
    Assert(repository.SaveCallCount == 1, "已开始的保存应只调用一次。");
}

static async Task VerifyUnloadCancelsQueuedWriteAsync()
{
    var repository = new FakeAccountRepository(CreateViewModelFixture()) { BlockFirstSave = true };
    var viewModel = new MainViewModel(repository);
    await viewModel.LoadAsync();

    var firstAdd = viewModel.AddAsync(CreateNewAccount());
    await repository.FirstSaveStarted;
    var queuedAdd = viewModel.AddAsync(CreateAdditionalNewAccount());
    viewModel.Unload();
    AssertUnloaded(viewModel, "排队写卸载后立即");

    repository.ReleaseFirstSave();
    await AssertThrowsAsync<OperationCanceledException>(() => firstAdd);
    await AssertThrowsAsync<OperationCanceledException>(() => queuedAdd);
    AssertUnloaded(viewModel, "排队写释放后");
    Assert(repository.SaveCallCount == 1, "排队写不应调用保存。");
    Assert(!repository.SecondSaveStarted.IsCompleted, "排队写不应进入第二次保存。");
}

static async Task VerifyExternalCancellationWhileWaitingForWriteGateAsync()
{
    var repository = new FakeAccountRepository(CreateViewModelFixture()) { BlockFirstSave = true };
    var viewModel = new MainViewModel(repository);
    await viewModel.LoadAsync();

    var firstAdd = viewModel.AddAsync(CreateNewAccount());
    await repository.FirstSaveStarted;
    using var cancellation = new CancellationTokenSource();
    var canceledAdd = viewModel.AddAsync(CreateAdditionalNewAccount(), cancellation.Token);
    cancellation.Cancel();
    await AssertThrowsAsync<OperationCanceledException>(() => canceledAdd);

    repository.ReleaseFirstSave();
    await firstAdd;
    AssertAccountIds(viewModel.AccountsSnapshot, "1", "2", "3", "4", "5");
    Assert(repository.SaveCallCount == 1, "等待门时取消不应进入第二次保存。");
    await viewModel.AddAsync(CreateAdditionalNewAccount());
    AssertAccountIds(viewModel.AccountsSnapshot, "1", "2", "3", "4", "5", "6");
}

static async Task VerifyRepositoryCancellationPropagatesAsync()
{
    using var cancellation = new CancellationTokenSource();
    var repository = new FakeAccountRepository(CreateViewModelFixture())
    {
        CancelSaveTokenSource = cancellation,
    };
    var viewModel = new MainViewModel(repository);
    await viewModel.LoadAsync();
    var expectedState = CaptureState(viewModel);

    await AssertThrowsAsync<OperationCanceledException>(() => viewModel.AddAsync(CreateNewAccount(), cancellation.Token));
    AssertStateUnchanged(expectedState, viewModel, repository, "仓库保存取消");
    Assert(repository.SaveCallCount == 1, "仓库保存取消调用次数错误。");
}

static async Task VerifyLockedImportReturnsNeutralResultAsync()
{
    var repository = new FakeAccountRepository(CreateViewModelFixture()) { BlockFirstSave = true };
    var viewModel = new MainViewModel(repository);
    await viewModel.LoadAsync();

    var import = viewModel.ImportAsync([CreateNewAccount()]);
    await repository.FirstSaveStarted;
    viewModel.Unload();
    repository.ReleaseFirstSave();

    await AssertThrowsAsync<OperationCanceledException>(() => import);
    AssertUnloaded(viewModel, "锁定期间导入完成后");
    Assert(repository.SaveCallCount == 1, "已开始的导入保存应只调用一次。");
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

static MfaAccount CreateNewAccount() => new()
{
    Id = "5", Issuer = "Example", Account = "new-account", Secret = "E", Group = "工作", CreatedAt = 5,
};

static MfaAccount CreateAdditionalNewAccount() => new()
{
    Id = "6", Issuer = "Example", Account = "additional-account", Secret = "F", Group = "工作", CreatedAt = 6,
};

static ViewModelState CaptureState(MainViewModel viewModel) => new(
    viewModel.AccountsSnapshot.ToArray(),
    viewModel.VisibleAccounts.Select(account => account.Model).ToArray(),
    viewModel.Groups.Select(group => new GroupState(group.Value, group.Label, group.Count)).ToArray(),
    viewModel.HasAccounts,
    viewModel.HasVisibleAccounts,
    viewModel.HasLoadFailure,
    viewModel.IsLoaded,
    viewModel.SearchText,
    viewModel.SelectedGroup);

static void AssertStateUnchanged(
    ViewModelState expected,
    MainViewModel viewModel,
    FakeAccountRepository repository,
    string operation)
{
    var actual = CaptureState(viewModel);
    Assert(expected.Accounts.SequenceEqual(actual.Accounts), $"{operation}后账户快照发生变化。");
    Assert(expected.VisibleAccounts.SequenceEqual(actual.VisibleAccounts), $"{operation}后可见账户发生变化。");
    Assert(expected.Groups.SequenceEqual(actual.Groups), $"{operation}后分组状态发生变化。");
    Assert(expected.HasAccounts == actual.HasAccounts, $"{operation}后账户状态发生变化。");
    Assert(expected.HasVisibleAccounts == actual.HasVisibleAccounts, $"{operation}后可见账户状态发生变化。");
    Assert(expected.HasLoadFailure == actual.HasLoadFailure, $"{operation}后加载失败状态发生变化。");
    Assert(expected.IsLoaded == actual.IsLoaded, $"{operation}后加载状态发生变化。");
    Assert(expected.SearchText == actual.SearchText, $"{operation}后搜索条件发生变化。");
    Assert(expected.SelectedGroup == actual.SelectedGroup, $"{operation}后分组选择发生变化。");
    Assert(expected.Accounts.SequenceEqual(repository.LastSuccessfulSnapshot), $"{operation}后持久化快照发生变化。");
}

static void AssertIds(IEnumerable<MfaAccount> actual, params string[] expected)
{
    var actualIds = actual.Select(account => account.Id).ToArray();
    Assert(actualIds.SequenceEqual(expected), "账户 ID 顺序或内容不匹配。");
}

static void AssertAccountIds(IEnumerable<MfaAccount> actual, params string[] expected)
{
    var actualIds = actual.Select(account => account.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
    var expectedIds = expected.OrderBy(id => id, StringComparer.Ordinal).ToArray();
    Assert(actualIds.SequenceEqual(expectedIds), "账户 ID 不匹配。");
}

static void AssertUnloaded(MainViewModel viewModel, string stage)
{
    Assert(!viewModel.IsLoaded, $"{stage}仍处于已加载状态。");
    Assert(viewModel.AccountsSnapshot.Count == 0, $"{stage}账户快照未清空。");
    Assert(viewModel.VisibleAccounts.Count == 0, $"{stage}可见账户未清空。");
    Assert(viewModel.Groups.Count == 0, $"{stage}分组未清空。");
    Assert(viewModel.SelectedGroup is null, $"{stage}分组选择未清空。");
    Assert(!viewModel.HasAccounts, $"{stage}账户状态未清空。");
    Assert(!viewModel.HasVisibleAccounts, $"{stage}可见账户状态未清空。");
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
    private readonly Exception? _loadException;

    public FakeAccountRepository(IReadOnlyList<MfaAccount>? accounts = null, Exception? loadException = null)
    {
        _loadException = loadException;
        LastSuccessfulSnapshot = (accounts ?? Array.Empty<MfaAccount>()).ToArray();
    }

    public int SaveCallCount { get; private set; }

    public bool ThrowOnSave { get; set; }

    public bool CancelOnSave { get; set; }

    public bool BlockFirstSave { get; set; }

    public CancellationTokenSource? CancelSaveTokenSource { get; set; }

    public IReadOnlyList<MfaAccount> LastSuccessfulSnapshot { get; private set; }

    public Task FirstSaveStarted => _firstSaveStarted.Task;

    public Task SecondSaveStarted => _secondSaveStarted.Task;

    private readonly TaskCompletionSource<bool> _firstSaveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _secondSaveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _releaseFirstSave = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<IReadOnlyList<MfaAccount>> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_loadException is not null)
        {
            return Task.FromException<IReadOnlyList<MfaAccount>>(_loadException);
        }

        return Task.FromResult<IReadOnlyList<MfaAccount>>(LastSuccessfulSnapshot.ToArray());
    }

    public async Task SaveAsync(IReadOnlyCollection<MfaAccount> accounts, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var saveNumber = ++SaveCallCount;
        if (BlockFirstSave && saveNumber == 1)
        {
            _firstSaveStarted.TrySetResult(true);
            await _releaseFirstSave.Task.WaitAsync(cancellationToken);
        }

        if (saveNumber == 2)
        {
            _secondSaveStarted.TrySetResult(true);
        }

        if (ThrowOnSave)
        {
            throw new IOException("模拟保存失败");
        }

        if (CancelOnSave)
        {
            throw new OperationCanceledException(new CancellationToken(canceled: true));
        }

        if (CancelSaveTokenSource is not null)
        {
            CancelSaveTokenSource.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
        }

        LastSuccessfulSnapshot = accounts.ToArray();
    }

    public void ReleaseFirstSave() => _releaseFirstSave.TrySetResult(true);
}

internal sealed record ViewModelState(
    IReadOnlyList<MfaAccount> Accounts,
    IReadOnlyList<MfaAccount> VisibleAccounts,
    IReadOnlyList<GroupState> Groups,
    bool HasAccounts,
    bool HasVisibleAccounts,
    bool HasLoadFailure,
    bool IsLoaded,
    string SearchText,
    string? SelectedGroup);

internal sealed record GroupState(string? Value, string Label, int Count);
