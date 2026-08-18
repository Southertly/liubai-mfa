using LiubaiMfa.Core;
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
    ("添加成功会清除隐藏筛选并发布一次性定位", VerifyAddPublishesPendingRevealAsync),
    ("一次性定位只能消费一次", VerifyPendingRevealIsConsumedOnceAsync),
    ("添加保存失败保持筛选和定位状态", VerifyAddFailurePreservesRevealStateAsync),
    ("导入只在实际新增后清除隐藏筛选并定位", VerifyImportPublishesPendingRevealAtomicallyAsync),
    ("卸载必须清除未消费的账户定位", VerifyUnloadClearsPendingRevealAsync),
    ("会话取消域绑定令牌并隔离新会话", () => RunSync(VerifySessionCancellationLifecycle)),
    ("会话取消域可重复并发失效", VerifySessionCancellationConcurrentInvalidationAsync),
    ("会话取消租约延迟释放底层资源", () => RunSync(VerifySessionCancellationLeaseLifetime)),
    ("取消回调释放租约时不得死锁", VerifySessionCancellationCallbackLeaseReleaseAsync),
    ("备份临时写入取消时不得替换目标", VerifyBackupWriteCancellationSkipsReplaceAsync),
    ("备份临时写入成功时只替换一次目标", VerifyBackupWriteSuccessReplacesOnceAsync),
    ("最终替换开始后必须完成并报告会话失效", VerifyBackupReplaceCompletesAfterInvalidationAsync),
    ("延期备份提示只在有效解锁会话消费一次", () => RunSync(VerifyDeferredBackupNoticeGate)),
    ("备份准备和恢复密钥格式化必须受错误边界保护", () => RunSync(VerifyBackupPreparationErrorBoundary)),
    ("备份文件选择失败不得逸出异步事件", () => RunSync(VerifyBackupPickerErrorBoundary)),
    ("旧代际回调不能操作当前计时器", () => RunSync(VerifyEpochCallbackGuard)),
    ("新定位请求必须先替换旧计时器和高亮", () => RunSync(VerifyRevealReplacementOrdering)),
    ("旧定位 ID 不能消费新定位", VerifyPendingRevealRequiresExpectedIdAsync),
    ("派生筛选复用账户行并保持高亮", VerifyDerivedRefreshReusesAccountRowsAsync),
    ("隐藏账户行恢复前可清理高亮", VerifyHiddenHighlightCanBeClearedAsync),
    ("删除账户会移除行缓存", VerifyDeleteRemovesAccountRowCacheAsync),
    ("添加冲突 ID 必须生成唯一 ID", VerifyAddConflictingIdUsesUniqueIdAsync),
    ("导入冲突和同批重复 ID 必须唯一", VerifyImportConflictingIdsUseUniqueIdsAsync),
    ("解锁会话保护器初始保持锁定", () => RunSync(VerifyUnlockSessionGuardStartsLocked)),
    ("解锁会话保护器使失效令牌永久失效", () => RunSync(VerifyUnlockSessionGuardInvalidation)),
    ("解锁会话保护器为新会话生成不同令牌", () => RunSync(VerifyUnlockSessionGuardStartsDistinctSessions)),
    ("解锁会话保护器并发失效后不保留旧会话", VerifyUnlockSessionGuardConcurrentInvalidationAsync),
    ("紧凑账户行格式化、进度和边界", () => RunSync(VerifyCompactAccountRowFormatting)),
    ("紧凑账户行服务图标回退", () => RunSync(VerifyServiceIconResolution)),
    ("紧凑账户行服务图标文字保持可读对比", () => RunSync(VerifyServiceIconForegroundContrast)),
    ("紧凑账户行刷新和替换通知", () => RunSync(VerifyCompactAccountRowRefreshAndReplacement)),
    ("紧凑账户行拒绝空模型", () => RunSync(VerifyCompactAccountRowNullModelSafety)),
    ("紧凑账户行安全处理损坏密钥", () => RunSync(VerifyCompactAccountRowInvalidSecrets)),
    ("账户编辑不要求 Hello 或确认", () => RunSync(VerifyEditActionPolicy)),
    ("查看密钥只要求 Hello", () => RunSync(VerifyViewSecretActionPolicy)),
    ("删除账户只要求确认", () => RunSync(VerifyDeleteActionPolicy)),
    ("账户行 Ctrl+C 只接受精确 Ctrl 修饰键", () => RunSync(VerifyExactCopyShortcutPolicy)),
    ("窗口位置限制在主屏工作区", () => RunSync(VerifyWindowPlacementClampsToPrimaryWorkArea)),
    ("窗口位置修复损坏尺寸", () => RunSync(VerifyWindowPlacementRepairsDamagedSize)),
    ("窗口位置保留负坐标副屏", () => RunSync(VerifyWindowPlacementPreservesNegativeMonitorCoordinates)),
    ("副屏工作区转换为全局坐标", () => RunSync(VerifyDisplayWorkAreaUsesScreenCoordinates)),
    ("窗口尺寸不超出工作区", () => RunSync(VerifyWindowPlacementFitsWorkArea)),
    ("窗口位置计算防止整数溢出", () => RunSync(VerifyWindowPlacementHandlesExtremeValues)),
    ("窗口有效像素安全换算为物理像素", () => RunSync(VerifyEffectivePixelConversion)),
    ("窗口位置仅在普通状态发生变化时保存", () => RunSync(VerifyWindowPlacementSavePolicy)),
};

foreach (var check in checks)
{
    await check.Run();
    Console.WriteLine($"PASS {check.Name}");
}

Console.WriteLine($"{checks.Count} 项 Windows 平台回归检查通过。");

static void VerifyUnlockSessionGuardStartsLocked()
{
    var guard = new UnlockSessionGuard();

    Assert(!guard.IsCurrent(0), "令牌 0 永远不能代表有效解锁会话。");
    Assert(!guard.IsCurrent(1), "保护器初始状态必须保持锁定。");
}

static void VerifyUnlockSessionGuardInvalidation()
{
    var guard = new UnlockSessionGuard();
    var token = guard.BeginUnlockedSession();

    Assert(token != 0, "解锁会话令牌不能为 0。");
    Assert(guard.IsCurrent(token), "新解锁会话令牌应立即有效。");

    guard.Invalidate();

    Assert(!guard.IsCurrent(token), "失效后的旧令牌不得继续有效。");
    Assert(!guard.IsCurrent(0), "失效状态不得把令牌 0 视为有效。");
}

static void VerifyUnlockSessionGuardStartsDistinctSessions()
{
    var guard = new UnlockSessionGuard();
    var first = guard.BeginUnlockedSession();
    guard.Invalidate();
    var second = guard.BeginUnlockedSession();

    Assert(first != second, "每次解锁必须生成不同的会话令牌。");
    Assert(!guard.IsCurrent(first), "开始新会话后，旧令牌不得重新有效。");
    Assert(guard.IsCurrent(second), "最新解锁会话令牌应有效。");
}

static async Task VerifyUnlockSessionGuardConcurrentInvalidationAsync()
{
    var guard = new UnlockSessionGuard();
    var token = guard.BeginUnlockedSession();
    using var start = new ManualResetEventSlim(initialState: false);

    var reader = Task.Run(() =>
    {
        start.Wait();
        for (var index = 0; index < 10_000; index++)
        {
            _ = guard.IsCurrent(token);
        }
    });
    var invalidator = Task.Run(() =>
    {
        start.Wait();
        guard.Invalidate();
    });

    start.Set();
    await Task.WhenAll(reader, invalidator);

    Assert(!guard.IsCurrent(token), "并发失效完成后旧令牌必须保持无效。");
}

static void VerifyWindowPlacementClampsToPrimaryWorkArea()
{
    var workArea = new PixelRect(0, 0, 1920, 1040);
    var actual = WindowPlacementService.Clamp(
        new PixelRect(1800, 900, 520, 680),
        workArea,
        460,
        560);

    Assert(actual == new PixelRect(1400, 360, 520, 680), "窗口应完整限制在工作区内。");
}

static void VerifyWindowPlacementRepairsDamagedSize()
{
    var actual = WindowPlacementService.Clamp(
        new PixelRect(-200, -300, 200, 300),
        new PixelRect(0, 0, 1920, 1040),
        460,
        560);

    Assert(actual == new PixelRect(0, 0, 460, 560), "损坏的窗口尺寸应恢复到安全最小值。");
}

static void VerifyWindowPlacementPreservesNegativeMonitorCoordinates()
{
    var desired = new PixelRect(-1800, 120, 520, 680);
    var actual = WindowPlacementService.Clamp(
        desired,
        new PixelRect(-1920, 0, 1920, 1040),
        460,
        560);

    Assert(actual == desired, "负坐标副屏内的合法窗口位置不应改变。");
}

static void VerifyDisplayWorkAreaUsesScreenCoordinates()
{
    var actual = WindowPlacementService.ToScreenWorkArea(
        new PixelRect(-1920, 0, 1920, 1080),
        new PixelRect(40, 0, 1880, 1040));

    Assert(actual == new PixelRect(-1880, 0, 1880, 1040), "副屏工作区偏移应叠加到显示区的全局坐标。");
}

static void VerifyWindowPlacementFitsWorkArea()
{
    var actual = WindowPlacementService.Clamp(
        new PixelRect(200, 300, 2400, 1600),
        new PixelRect(-1920, -100, 1280, 720),
        460,
        560);

    Assert(actual == new PixelRect(-1920, -100, 1280, 720), "超大窗口应缩小到工作区并对齐工作区起点。");
}

static void VerifyWindowPlacementHandlesExtremeValues()
{
    var workArea = new PixelRect(0, 0, 1920, 1040);
    var damaged = WindowPlacementService.Clamp(
        new PixelRect(int.MinValue, int.MaxValue, 0, -1),
        workArea,
        460,
        560);
    var extreme = WindowPlacementService.Clamp(
        new PixelRect(int.MaxValue, int.MinValue, int.MaxValue, int.MaxValue),
        workArea,
        460,
        560);

    Assert(damaged == new PixelRect(0, 480, 460, 560), "非法尺寸和极端坐标应安全限制。");
    Assert(extreme == workArea, "极端尺寸不应导致整数溢出。");
}

static void VerifyEffectivePixelConversion()
{
    Assert(WindowPlacementService.EffectiveToPhysical(520, 144) == 780, "144 DPI 换算错误。");
    Assert(WindowPlacementService.EffectiveToPhysical(520, 96) == 520, "96 DPI 换算错误。");
    Assert(WindowPlacementService.EffectiveToPhysical(520, 0) == 520, "0 DPI 应回退到 96 DPI。");
    Assert(WindowPlacementService.EffectiveToPhysical(0, 144) >= 1, "换算结果至少应为 1 像素。");
    Assert(WindowPlacementService.EffectiveToPhysical(int.MaxValue, uint.MaxValue) == int.MaxValue, "极端 DPI 换算应防止溢出。");
}

static void VerifyWindowPlacementSavePolicy()
{
    Assert(!WindowPlacementService.ShouldSave(false, false, true), "位置和尺寸均未变化时不应保存。");
    Assert(WindowPlacementService.ShouldSave(true, false, true), "普通状态下位置变化应保存。");
    Assert(WindowPlacementService.ShouldSave(false, true, true), "普通状态下尺寸变化应保存。");
    Assert(!WindowPlacementService.ShouldSave(true, false, false), "最小化状态不应保存。");
    Assert(!WindowPlacementService.ShouldSave(false, true, false), "最大化状态不应保存。");
}

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

static void VerifyCompactAccountRowFormatting()
{
    Assert(AccountItemViewModel.FormatCode("123456") == "123 456", "6 位验证码格式错误。");
    Assert(AccountItemViewModel.FormatCode("12345678") == "12 345 678", "8 位验证码格式错误。");
    Assert(AccountItemViewModel.FormatCode("12345") == "12345", "非 6/8 位验证码不应改写。");
    Assert(AccountItemViewModel.CalculateProgress(15, 30) == 0.5, "剩余时间进度计算错误。");
    Assert(AccountItemViewModel.CalculateProgress(0, 30) == 0, "0 秒剩余时间应为 0 进度。");
    Assert(AccountItemViewModel.CalculateProgress(30, 30) == 1, "完整周期应被限制为 1 进度。");
    Assert(AccountItemViewModel.CalculateProgress(31, 30) == 1, "超出周期应被限制为 1 进度。");
    Assert(AccountItemViewModel.CalculateProgress(-1, 30) == 0, "负数剩余时间应被限制为 0 进度。");
    Assert(AccountItemViewModel.CalculateProgress(15, 0) == 0, "非法周期应安全返回 0 进度。");
}

static void VerifyServiceIconResolution()
{
    Assert(ServiceIconResolver.Resolve("Google").FallbackText == "G", "Google 图标回退文字错误。");
    Assert(ServiceIconResolver.Resolve("unknown service").Kind == ServiceIconKind.FallbackText, "未知服务必须使用文字回退图标。");
    Assert(ServiceIconResolver.Resolve(null).FallbackText == "?", "缺失服务应使用安全默认字符。");
    Assert(ServiceIconResolver.Resolve("   ").FallbackText == "?", "空白服务应使用安全默认字符。");
    Assert(ServiceIconResolver.Resolve("😀 服务").FallbackText == "😀", "emoji 服务名不应截断 surrogate。");
    Assert(ServiceIconResolver.Resolve("e\u0301 服务").FallbackText == "E\u0301", "组合字符应作为完整文本元素并大写。");
    var normalizedOne = ServiceIconResolver.Resolve(" Example ");
    var normalizedTwo = ServiceIconResolver.Resolve("example");
    Assert(normalizedOne.BackgroundColor == normalizedTwo.BackgroundColor, "规范化后的未知服务颜色必须稳定一致。");
    Assert(normalizedOne.BackgroundColor == "#FF7C3AED", "未知服务固定 FNV 调色板颜色错误。");
    Assert(System.Text.RegularExpressions.Regex.IsMatch(normalizedOne.BackgroundColor, "^#[0-9A-F]{8}$"), "图标背景色不是 ARGB 十六进制格式。");
}

static void VerifyServiceIconForegroundContrast()
{
    Assert(ServiceIconResolver.Resolve("Google").ForegroundColor == "#FF000000", "Google 图标应选择黑色文字以保持普通模式对比度。");
    Assert(ServiceIconResolver.Resolve("OpenAI").ForegroundColor == "#FF000000", "OpenAI 图标应选择黑色文字以保持普通模式对比度。");
    Assert(ServiceIconResolver.Resolve("X").ForegroundColor == "#FFFFFFFF", "深色 X 图标应选择白色文字以保持普通模式对比度。");
}

static void VerifyCompactAccountRowRefreshAndReplacement()
{
    const string secret = "JBSWY3DPEHPK3PXP";
    var item = new AccountItemViewModel(new MfaAccount
    {
        Id = "compact", Issuer = "Google", Account = "alice@example.com", Secret = secret, Group = "工作", CreatedAt = 1,
    });
    var atStart = DateTimeOffset.FromUnixTimeSeconds(0);
    var atNextPeriod = DateTimeOffset.FromUnixTimeSeconds(30);
    var midPeriod = DateTimeOffset.FromUnixTimeSeconds(45);

    item.Refresh(atStart);
    var changes = new List<string?>();
    item.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
    item.Refresh(atNextPeriod);
    Assert(item.Code == TotpService.Generate(secret, atNextPeriod), "刷新未更新验证码。");
    Assert(changes.Contains(nameof(AccountItemViewModel.FormattedCode)), "验证码变化未通知格式化值。");
    Assert(changes.Contains(nameof(AccountItemViewModel.AccessibleName)), "验证码变化未通知无障碍名称。");
    Assert(changes.Count(name => name == nameof(AccountItemViewModel.Code)) == 1, "验证码变化应只通知一次 Code。");
    Assert(changes.Count(name => name == nameof(AccountItemViewModel.FormattedCode)) == 1, "验证码变化应只通知一次 FormattedCode。");
    Assert(changes.Count(name => name == nameof(AccountItemViewModel.AccessibleName)) == 1, "仅验证码变化应只通知一次 AccessibleName。");

    changes.Clear();
    item.Refresh(midPeriod);
    Assert(item.Code == TotpService.Generate(secret, midPeriod), "固定时间戳的验证码错误。");
    Assert(item.FormattedCode == AccountItemViewModel.FormatCode(item.Code), "格式化验证码未同步。");
    Assert(item.SecondsRemaining == TotpService.GetTimeRemaining(midPeriod), "剩余秒数未使用同一时间戳。");
    Assert(item.ProgressValue == AccountItemViewModel.CalculateProgress(item.SecondsRemaining, 30), "进度值未同步。");
    Assert(changes.Contains(nameof(AccountItemViewModel.RemainingLabel)), "秒数变化未通知剩余时间文本。");
    Assert(changes.Contains(nameof(AccountItemViewModel.ProgressValue)), "秒数变化未通知进度值。");
    Assert(changes.Contains(nameof(AccountItemViewModel.AccessibleName)), "秒数变化未通知无障碍名称。");
    Assert(changes.Count(name => name == nameof(AccountItemViewModel.SecondsRemaining)) == 1, "秒数变化应只通知一次 SecondsRemaining。");
    Assert(changes.Count(name => name == nameof(AccountItemViewModel.RemainingLabel)) == 1, "秒数变化应只通知一次 RemainingLabel。");
    Assert(changes.Count(name => name == nameof(AccountItemViewModel.ProgressValue)) == 1, "秒数变化应只通知一次 ProgressValue。");
    Assert(changes.Count(name => name == nameof(AccountItemViewModel.AccessibleName)) == 1, "仅秒数变化应只通知一次 AccessibleName。");

    changes.Clear();
    item.Refresh(midPeriod);
    Assert(changes.Count == 0, "相同时间戳刷新不应发送通知。");

    item.Refresh(atNextPeriod);
    Assert(item.SecondsRemaining == 30 && item.ProgressValue == 1, "周期边界应显示完整剩余周期和进度。");

    changes.Clear();
    item.ReplaceModel(item.Model with { Issuer = "Microsoft", Account = "bob@example.com", Group = "个人" });
    Assert(changes.ToHashSet().IsSupersetOf([
        nameof(AccountItemViewModel.Icon), nameof(AccountItemViewModel.AccessibleName), nameof(AccountItemViewModel.Issuer),
        nameof(AccountItemViewModel.Account), nameof(AccountItemViewModel.GroupLabel),
    ]), "替换模型未通知所有相关展示属性。");
}

static void VerifyCompactAccountRowNullModelSafety()
{
    AssertThrows<ArgumentNullException>(() => _ = new AccountItemViewModel(null!), "构造函数应拒绝空模型。");
    var item = new AccountItemViewModel(new MfaAccount
    {
        Id = "safe", Issuer = "Google", Account = "alice", Secret = "JBSWY3DPEHPK3PXP", Group = "工作", CreatedAt = 1,
    });
    item.Refresh(DateTimeOffset.FromUnixTimeSeconds(45));
    var before = (item.Model, item.Issuer, item.Account, item.Code, item.Icon, item.AccessibleName);
    var changes = new List<string?>();
    item.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

    AssertThrows<ArgumentNullException>(() => item.ReplaceModel(null!), "ReplaceModel 应拒绝空模型。");
    Assert(before == (item.Model, item.Issuer, item.Account, item.Code, item.Icon, item.AccessibleName), "拒绝空模型后旧状态不应改变。");
    Assert(changes.Count == 0, "拒绝空模型后不应发送属性通知。");
}

static void VerifyEditActionPolicy()
{
    Assert(!AccountActionPolicy.RequiresWindowsHello(AccountAction.Edit), "编辑账户不应要求 Windows Hello。");
    Assert(!AccountActionPolicy.RequiresConfirmation(AccountAction.Edit), "编辑账户不应要求二次确认。");
}

static void VerifyViewSecretActionPolicy()
{
    Assert(AccountActionPolicy.RequiresWindowsHello(AccountAction.ViewSecret), "查看密钥必须要求 Windows Hello。");
    Assert(!AccountActionPolicy.RequiresConfirmation(AccountAction.ViewSecret), "查看密钥不应要求删除确认。");
}

static void VerifyDeleteActionPolicy()
{
    Assert(!AccountActionPolicy.RequiresWindowsHello(AccountAction.Delete), "删除账户不应要求 Windows Hello。");
    Assert(AccountActionPolicy.RequiresConfirmation(AccountAction.Delete), "删除账户必须要求二次确认。");
}

static void VerifyExactCopyShortcutPolicy()
{
    Assert(AccountActionPolicy.IsCopyShortcut(controlDown: true, shiftDown: false, altDown: false, windowsDown: false, wasKeyDown: false), "仅 Ctrl+C 首次按下应触发复制。");
    Assert(!AccountActionPolicy.IsCopyShortcut(controlDown: true, shiftDown: true, altDown: false, windowsDown: false, wasKeyDown: false), "Ctrl+Shift+C 不得触发复制。");
    Assert(!AccountActionPolicy.IsCopyShortcut(controlDown: true, shiftDown: false, altDown: true, windowsDown: false, wasKeyDown: false), "Ctrl+Alt+C 不得触发复制。");
    Assert(!AccountActionPolicy.IsCopyShortcut(controlDown: true, shiftDown: false, altDown: false, windowsDown: true, wasKeyDown: false), "Ctrl+Win+C 不得触发复制。");
    Assert(!AccountActionPolicy.IsCopyShortcut(controlDown: true, shiftDown: false, altDown: false, windowsDown: false, wasKeyDown: true), "按键自动重复不得再次复制。");
}

static void VerifyCompactAccountRowInvalidSecrets()
{
    foreach (var secret in new string?[] { null, string.Empty })
    {
        var item = new AccountItemViewModel(new MfaAccount
        {
            Id = "invalid", Issuer = "Example", Account = "broken", Secret = secret!, CreatedAt = 1,
        });
        item.Refresh(DateTimeOffset.FromUnixTimeSeconds(45));
        Assert(item.Code == "------", "损坏密钥应显示安全占位验证码。");
        Assert(item.SecondsRemaining == 0 && item.ProgressValue == 0, "损坏密钥应显示零剩余时间和零进度。");
    }
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

static async Task VerifyAddPublishesPendingRevealAsync()
{
    var repository = new FakeAccountRepository(CreateViewModelFixture());
    var viewModel = new MainViewModel(repository);
    await viewModel.LoadAsync();
    viewModel.SearchText = "不会匹配";
    viewModel.SelectedGroup = "工作";

    var added = await viewModel.AddAsync(CreateNewAccount());

    Assert(added.Id == "5", "添加应返回实际保存的账户。");
    Assert(viewModel.SearchText == string.Empty, "添加成功后必须清除会隐藏新账户的搜索条件。");
    Assert(viewModel.SelectedGroup is null, "添加成功后必须清除会隐藏新账户的分组条件。");
    Assert(viewModel.PendingRevealAccountId == "5", "添加成功后必须发布新账户定位。");
    Assert(viewModel.VisibleAccounts.Any(account => account.Id == "5"), "新增账户必须立即可见。");
}

static async Task VerifyPendingRevealIsConsumedOnceAsync()
{
    var repository = new FakeAccountRepository(CreateViewModelFixture());
    var viewModel = new MainViewModel(repository);
    await viewModel.LoadAsync();
    await viewModel.AddAsync(CreateNewAccount());

    Assert(viewModel.ConsumePendingReveal("5"), "首次消费应返回新增账户 ID。");
    Assert(!viewModel.ConsumePendingReveal("5"), "定位 ID 必须只能消费一次。");
    Assert(viewModel.PendingRevealAccountId is null, "消费后不应保留定位 ID。");
}

static async Task VerifyAddFailurePreservesRevealStateAsync()
{
    var repository = new FakeAccountRepository(CreateViewModelFixture()) { ThrowOnSave = true };
    var viewModel = new MainViewModel(repository);
    await viewModel.LoadAsync();
    viewModel.SearchText = "不会匹配";
    viewModel.SelectedGroup = "工作";
    var expected = CaptureState(viewModel);

    await AssertThrowsAsync<IOException>(() => viewModel.AddAsync(CreateNewAccount()));

    AssertStateUnchanged(expected, viewModel, repository, "添加保存失败的定位状态");
    Assert(viewModel.PendingRevealAccountId is null, "添加保存失败不能发布定位 ID。");
}

static async Task VerifyImportPublishesPendingRevealAtomicallyAsync()
{
    var repository = new FakeAccountRepository(CreateViewModelFixture());
    var viewModel = new MainViewModel(repository);
    await viewModel.LoadAsync();
    viewModel.SearchText = "不会匹配";
    viewModel.SelectedGroup = "工作";

    var result = await viewModel.ImportAsync([CreateViewModelFixture()[0], CreateNewAccount(), CreateAdditionalNewAccount()]);

    Assert(result == (2, 1), "导入结果应区分新增与重复账户。");
    Assert(viewModel.SearchText == string.Empty && viewModel.SelectedGroup is null, "实际新增导入后必须清除隐藏筛选。");
    Assert(viewModel.PendingRevealAccountId == "5", "导入应定位第一个实际新增账户。");
    Assert(viewModel.VisibleAccounts.Any(account => account.Id == "5"), "导入的首个新增账户必须可见。");

    Assert(viewModel.ConsumePendingReveal("5"), "应能消费导入的首个新增定位。");
    viewModel.SearchText = "不会匹配";
    viewModel.SelectedGroup = "工作";
    var expected = CaptureState(viewModel);
    var noImport = await viewModel.ImportAsync([CreateViewModelFixture()[0]]);
    Assert(noImport == (0, 1), "重复导入必须保持中性结果。");
    AssertStateUnchanged(expected, viewModel, repository, "无可导入账户");
    Assert(viewModel.PendingRevealAccountId is null, "无可导入账户不能发布定位 ID。");

    repository.ThrowOnSave = true;
    await AssertThrowsAsync<IOException>(() => viewModel.ImportAsync([CreateAdditionalNewAccount() with { Id = "7", Secret = "G" }]));
    AssertStateUnchanged(expected, viewModel, repository, "导入保存失败");
    Assert(viewModel.PendingRevealAccountId is null, "导入保存失败不能发布定位 ID。");
}

static async Task VerifyUnloadClearsPendingRevealAsync()
{
    var repository = new FakeAccountRepository(CreateViewModelFixture());
    var viewModel = new MainViewModel(repository);
    await viewModel.LoadAsync();
    await viewModel.AddAsync(CreateNewAccount());
    var notified = false;
    viewModel.PropertyChanged += (_, args) => notified |= args.PropertyName == nameof(MainViewModel.PendingRevealAccountId);

    viewModel.Unload();

    Assert(viewModel.PendingRevealAccountId is null, "卸载后不得残留账户定位 ID。");
    Assert(notified, "卸载清除定位 ID 时必须通知视图。");
}

static async Task VerifySessionCancellationCallbackLeaseReleaseAsync()
{
    var guard = new SessionCancellationGuard();
    guard.Begin(401);
    Assert(guard.TryAcquire(401, out var lease), "当前会话应能取得测试租约。");
    var token = lease.Token;
    using var registration = token.Register(() =>
    {
        lease.Dispose();
        var probe = Task.Run(() => !guard.TryAcquire(401, out _));
        if (!probe.Wait(TimeSpan.FromSeconds(1)))
        {
            throw new InvalidOperationException("取消回调不应被保护器锁阻塞。");
        }

        Assert(probe.Result, "失效会话不得在取消回调中重新取得租约。");
    });

    var invalidate = Task.Run(guard.Invalidate);
    Assert(await Task.WhenAny(invalidate, Task.Delay(TimeSpan.FromSeconds(2))) == invalidate, "取消回调释放租约时不得死锁。");
    await invalidate;
    Assert(token.IsCancellationRequested, "取消回调完成后令牌必须已取消。");
    Assert(guard.LiveStateCount == 0, "取消交付且回调释放最后租约后应清理取消源。");

    var disposedGuard = new SessionCancellationGuard();
    disposedGuard.Begin(402);
    Assert(disposedGuard.TryAcquire(402, out var disposedLease), "释放路径应能取得测试租约。");
    var disposedToken = disposedLease.Token;
    using var disposedRegistration = disposedToken.Register(() =>
    {
        disposedLease.Dispose();
        var probe = Task.Run(() => !disposedGuard.TryAcquire(402, out _));
        if (!probe.Wait(TimeSpan.FromSeconds(1)))
        {
            throw new InvalidOperationException("释放回调不应被保护器锁阻塞。");
        }

        Assert(probe.Result, "释放后的会话不得在取消回调中重新取得租约。");
    });
    var dispose = Task.Run(disposedGuard.Dispose);
    Assert(await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromSeconds(2))) == dispose, "释放保护器时取消回调不得死锁。");
    await dispose;
    Assert(disposedToken.IsCancellationRequested, "释放保护器必须取消活动令牌。");
    Assert(disposedGuard.LiveStateCount == 0, "释放后应清理已交付取消的资源。");
}

static async Task VerifyBackupWriteCancellationSkipsReplaceAsync()
{
    var coordinator = new BackupWriteCoordinator();
    using var cancellation = new CancellationTokenSource();
    var temporaryWrites = 0;
    var replacements = 0;

    var result = await coordinator.WriteAndReplaceAsync(
        _ =>
        {
            temporaryWrites++;
            cancellation.Cancel();
            return Task.CompletedTask;
        },
        () =>
        {
            replacements++;
            return Task.CompletedTask;
        },
        () => true,
        cancellation.Token);

    Assert(temporaryWrites == 1, "取消前应只写入临时文件一次。");
    Assert(replacements == 0, "取消后的备份不得替换用户选择的目标文件。");
    Assert(result == BackupWriteResult.NotReplaced, "取消后的写入应报告未替换目标文件。");
}

static async Task VerifyBackupWriteSuccessReplacesOnceAsync()
{
    var coordinator = new BackupWriteCoordinator();
    var temporaryWrites = 0;
    var replacements = 0;

    var result = await coordinator.WriteAndReplaceAsync(
        _ =>
        {
            temporaryWrites++;
            return Task.CompletedTask;
        },
        () =>
        {
            replacements++;
            return Task.CompletedTask;
        },
        () => true,
        CancellationToken.None);

    Assert(temporaryWrites == 1, "成功备份应只写入临时文件一次。");
    Assert(replacements == 1, "成功备份应只替换目标文件一次。");
    Assert(result == BackupWriteResult.Replaced, "成功备份应报告已替换目标文件。");
}

static async Task VerifyBackupReplaceCompletesAfterInvalidationAsync()
{
    var coordinator = new BackupWriteCoordinator();
    using var cancellation = new CancellationTokenSource();
    var isSessionCurrent = true;
    var replacements = 0;

    var result = await coordinator.WriteAndReplaceAsync(
        _ => Task.CompletedTask,
        () =>
        {
            replacements++;
            isSessionCurrent = false;
            cancellation.Cancel();
            return Task.CompletedTask;
        },
        () => isSessionCurrent,
        cancellation.Token);

    Assert(replacements == 1, "最终替换开始后必须完整执行一次。");
    Assert(result == BackupWriteResult.ReplacedAfterSessionInvalid, "最终替换后失效必须明确报告已替换但会话无效。");
}

static void VerifyDeferredBackupNoticeGate()
{
    var gate = new DeferredNoticeGate();

    Assert(!gate.TryConsume(), "新解锁首次递送前不应凭空显示提示。");
    gate.MarkPending();
    Assert(gate.TryConsume(), "旧操作在新解锁后标记时，当前会话应能立即消费提示。");
    Assert(!gate.TryConsume(), "同一提示不得重复递送。");

    gate.MarkPending();
    var isLocked = true;
    if (!isLocked)
    {
        Assert(gate.TryConsume(), "锁定状态不应走 UI 消费路径。");
    }

    isLocked = false;
    Assert(!isLocked && gate.TryConsume(), "锁定期间保留的提示必须在后续解锁时消费。");
    Assert(!gate.TryConsume(), "后续解锁不得重复消费同一提示。");
}

static void VerifyBackupPreparationErrorBoundary()
{
    var sourcePath = FindWorkspaceSourceFile("LiubaiMfa.Windows/MainWindow.xaml.cs");
    var source = File.ReadAllText(sourcePath);
    var methodStart = source.IndexOf("    private async void ExportBackup_Click", StringComparison.Ordinal);
    var methodEnd = source.IndexOf("    private async void ImportBackup_Click", methodStart, StringComparison.Ordinal);
    Assert(methodStart >= 0 && methodEnd > methodStart, "无法定位备份导出方法。");

    var method = source[methodStart..methodEnd];
    var createIndex = method.IndexOf("backup = BackupCrypto.CreateBackup", StringComparison.Ordinal);
    var formatIndex = method.IndexOf("formattedRecoveryKey = BackupCrypto.FormatRecoveryKey", StringComparison.Ordinal);
    var preparationCatchIndex = method.IndexOf("catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException", StringComparison.Ordinal);
    Assert(createIndex >= 0 && formatIndex > createIndex && preparationCatchIndex > formatIndex, "备份准备和恢复密钥格式化必须在受控错误边界内。");
}

static void VerifyBackupPickerErrorBoundary()
{
    var sourcePath = FindWorkspaceSourceFile("LiubaiMfa.Windows/MainWindow.xaml.cs");
    var source = File.ReadAllText(sourcePath);
    var methodStart = source.IndexOf("    private async void ExportBackup_Click", StringComparison.Ordinal);
    var methodEnd = source.IndexOf("    private async void ImportBackup_Click", methodStart, StringComparison.Ordinal);
    Assert(methodStart >= 0 && methodEnd > methodStart, "无法定位备份导出方法。");

    var method = source[methodStart..methodEnd];
    var pickerIndex = method.IndexOf("file = await _files.PickBackupSaveFileAsync();", StringComparison.Ordinal);
    var pickerCatchIndex = method.IndexOf("catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)", pickerIndex, StringComparison.Ordinal);
    Assert(pickerIndex >= 0 && pickerCatchIndex > pickerIndex, "备份文件选择失败必须在异步事件内转为克制提示。");
}

static void VerifySessionCancellationLifecycle()
{
    var guard = new SessionCancellationGuard();
    Assert(!guard.TryAcquire(1, out _), "初始状态不应提供取消令牌。");

    const long firstSession = 101;
    guard.Begin(firstSession);
    Assert(guard.TryAcquire(firstSession, out var firstLease), "当前会话应取得取消租约。");
    using (firstLease)
    {
        Assert(!firstLease.Token.IsCancellationRequested, "新会话令牌不应已取消。");
        Assert(!guard.TryAcquire(999, out _), "错误会话不能取得活动租约。");

        guard.Invalidate();
        Assert(firstLease.Token.IsCancellationRequested, "失效必须同步取消旧会话令牌。");
        Assert(!guard.TryAcquire(firstSession, out _), "失效后旧会话不能再取得租约。");
        Assert(guard.LiveStateCount == 1, "持有租约时不得过早释放取消源。");
    }
    Assert(guard.LiveStateCount == 0, "最后一个租约释放后应清理已失效取消源。");

    const long secondSession = 102;
    guard.Begin(secondSession);
    Assert(guard.TryAcquire(secondSession, out var secondLease), "新会话应取得新租约。");
    using (secondLease)
    {
        Assert(!secondLease.Token.IsCancellationRequested, "旧会话取消不能影响新会话。");
    }
    guard.Invalidate();
}

static async Task VerifySessionCancellationConcurrentInvalidationAsync()
{
    var guard = new SessionCancellationGuard();
    guard.Begin(201);
    await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(guard.Invalidate)));
    Assert(!guard.TryAcquire(201, out _), "并发重复失效后不应保留活动令牌。");
}

static void VerifySessionCancellationLeaseLifetime()
{
    var guard = new SessionCancellationGuard();
    for (var index = 1; index <= 100; index++)
    {
        guard.Begin(index);
        guard.Invalidate();
    }

    Assert(guard.LiveStateCount == 0, "无租约的重复会话不能线性保留取消源。");
    Assert(!guard.TryAcquire(1234, out _), "错误会话不能获取租约。");
}

static void VerifyEpochCallbackGuard()
{
    Assert(!EpochCallbackGuard.IsCurrent(4, 3, 7, 7, "new", "old"), "旧 epoch 回调不得操作当前计时器。");
    Assert(!EpochCallbackGuard.IsCurrent(4, 4, 8, 7, "same", "same"), "旧 attach 回调不得操作当前计时器。");
    Assert(!EpochCallbackGuard.IsCurrent(4, 4, 8, 8, "new", "old"), "旧账户回调不得操作当前计时器。");
    Assert(EpochCallbackGuard.IsCurrent(4, 4, 8, 8, "same", "same"), "当前代际回调应被允许执行。");
}

static void VerifyRevealReplacementOrdering()
{
    var sourcePath = FindWorkspaceSourceFile(Path.Combine("LiubaiMfa.Windows", "Views", "AuthenticatorView.xaml.cs"));
    var source = File.ReadAllText(sourcePath);
    var methodStart = source.IndexOf("    private void BeginRevealPending()", StringComparison.Ordinal);
    var methodEnd = source.IndexOf("    private void AccountsList_LayoutUpdated", methodStart, StringComparison.Ordinal);
    Assert(methodStart >= 0 && methodEnd > methodStart, "无法定位账户定位请求方法。");

    var method = source[methodStart..methodEnd];
    var stopIndex = method.IndexOf("StopRevealTimer();", StringComparison.Ordinal);
    var clearIndex = method.IndexOf("ClearRevealHighlight();", StringComparison.Ordinal);
    var requestedIndex = method.IndexOf("_requestedRevealAccountId = accountId;", StringComparison.Ordinal);
    var scrollIndex = method.IndexOf("ScrollPendingAccountIntoView", StringComparison.Ordinal);

    Assert(stopIndex >= 0 && clearIndex > stopIndex, "新定位请求必须先停止旧计时器并清除旧高亮。");
    Assert(requestedIndex > clearIndex && scrollIndex > requestedIndex, "新定位请求应在替换旧状态后才发布并滚动新请求。");
}

static string FindWorkspaceSourceFile(string relativePath)
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory }.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            var directPath = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(directPath))
            {
                return directPath;
            }

            var worktreePath = Path.Combine(directory.FullName, "artifacts", "windows", relativePath);
            if (File.Exists(worktreePath))
            {
                return worktreePath;
            }
        }
    }

    throw new InvalidOperationException($"无法定位工作区源文件：{relativePath}");
}

static async Task VerifyPendingRevealRequiresExpectedIdAsync()
{
    var viewModel = new MainViewModel(new FakeAccountRepository(CreateViewModelFixture()));
    await viewModel.LoadAsync();
    await viewModel.AddAsync(CreateNewAccount());

    Assert(!viewModel.ConsumePendingReveal("old-account"), "旧回调不能消费当前定位。");
    Assert(viewModel.PendingRevealAccountId == "5", "错误 ID 消费后必须保留当前定位。");
    Assert(viewModel.ConsumePendingReveal("5"), "匹配 ID 应能消费定位。");
    Assert(viewModel.PendingRevealAccountId is null, "匹配消费后应清空定位。");
}

static async Task VerifyDerivedRefreshReusesAccountRowsAsync()
{
    var viewModel = new MainViewModel(new FakeAccountRepository(CreateViewModelFixture()));
    await viewModel.LoadAsync();
    var original = viewModel.VisibleAccounts.Single(account => account.Id == "1");
    original.SetRevealHighlight(true);

    viewModel.SearchText = "alice";
    viewModel.SearchText = string.Empty;
    viewModel.SelectedGroup = "个人";
    viewModel.SelectedGroup = null;

    var refreshed = viewModel.VisibleAccounts.Single(account => account.Id == "1");
    Assert(ReferenceEquals(original, refreshed), "筛选刷新不应重建同一账户行对象。");
    Assert(refreshed.IsRevealHighlighted, "筛选刷新不应丢失账户高亮。");
}

static async Task VerifyHiddenHighlightCanBeClearedAsync()
{
    var viewModel = new MainViewModel(new FakeAccountRepository(CreateViewModelFixture()));
    await viewModel.LoadAsync();
    var original = viewModel.VisibleAccounts.Single(account => account.Id == "1");
    original.SetRevealHighlight(true);

    viewModel.SearchText = "不会匹配";
    Assert(!viewModel.VisibleAccounts.Contains(original), "筛选应暂时隐藏目标账户行。");
    original.SetRevealHighlight(false);
    viewModel.SearchText = string.Empty;

    var restored = viewModel.VisibleAccounts.Single(account => account.Id == "1");
    Assert(ReferenceEquals(original, restored), "恢复筛选必须复用原账户行。");
    Assert(!restored.IsRevealHighlighted, "隐藏状态下清理高亮后重新出现不得残留高亮。");
}

static async Task VerifyDeleteRemovesAccountRowCacheAsync()
{
    var viewModel = new MainViewModel(new FakeAccountRepository(CreateViewModelFixture()));
    await viewModel.LoadAsync();
    await viewModel.DeleteAsync("1");

    Assert(viewModel.VisibleAccounts.All(account => account.Id != "1"), "删除后不应保留可见账户行。");
    Assert(viewModel.AccountItemCacheCount == viewModel.AccountsSnapshot.Count, "删除后不应保留失效账户行缓存。");
}

static async Task VerifyAddConflictingIdUsesUniqueIdAsync()
{
    var viewModel = new MainViewModel(new FakeAccountRepository(CreateViewModelFixture()));
    await viewModel.LoadAsync();
    var added = await viewModel.AddAsync(CreateNewAccount() with { Id = "1", Secret = "unique-add-secret" });

    Assert(!string.IsNullOrWhiteSpace(added.Id) && added.Id != "1", "添加冲突 ID 必须替换为非空唯一 ID。");
    Assert(viewModel.PendingRevealAccountId == added.Id, "定位必须指向实际新增 ID。");
    Assert(viewModel.VisibleAccounts.Select(account => account.Id).Distinct(StringComparer.Ordinal).Count() == viewModel.VisibleAccounts.Count, "可见账户 ID 必须唯一。");
}

static async Task VerifyImportConflictingIdsUseUniqueIdsAsync()
{
    var viewModel = new MainViewModel(new FakeAccountRepository(CreateViewModelFixture()));
    await viewModel.LoadAsync();
    var result = await viewModel.ImportAsync(
    [
        CreateNewAccount() with { Id = "1", Secret = "unique-import-one" },
        CreateAdditionalNewAccount() with { Id = "1", Secret = "unique-import-two" },
    ]);

    Assert(result == (2, 0), "不同密钥的冲突 ID 账户应导入。");
    var ids = viewModel.VisibleAccounts.Select(account => account.Id).ToArray();
    Assert(ids.Distinct(StringComparer.Ordinal).Count() == ids.Length, "现有和同批导入账户 ID 必须唯一。");
    Assert(viewModel.PendingRevealAccountId is not null && viewModel.PendingRevealAccountId != "1", "导入定位必须指向实际分配的唯一 ID。");
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

static void AssertThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
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
