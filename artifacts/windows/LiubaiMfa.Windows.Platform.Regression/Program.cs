using LiubaiMfa.Core;
using LiubaiMfa.Core.Models;
using LiubaiMfa.Windows.Services;
using LiubaiMfa.Windows.ViewModels;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Xml.Linq;
using Windows.Storage;

var checks = new List<(string Name, Func<Task> Run)>
{
    ("启动保护设置缺失时默认关闭", () => RunSync(VerifyStartupProtectionDefaultsOff)),
    ("启动保护设置读取已保存状态", () => RunSync(VerifyStartupProtectionReadsStoredValue)),
    ("启动保护设置只写入目标布尔值", () => RunSync(VerifyStartupProtectionWritesRequestedValue)),
    ("启动保护只在开启时阻断启动", () => RunSync(VerifyStartupProtectionOpenPolicy)),
    ("启动保护切换只在验证和会话有效时提交", () => RunSync(VerifyStartupProtectionTransitionPolicy)),
    ("启动保护只在开启且已进入主界面时自动锁定", () => RunSync(VerifyStartupProtectionAutoLockPolicy)),
    ("加载期间失活保留原始自动锁定时限", () => RunSync(VerifyAutoLockStatePreservesDeactivationDuringLoad)),
    ("加载超过失活时限时立即锁定", () => RunSync(VerifyAutoLockStateLocksWhenLoadFinishesAfterDeadline)),
    ("加载期间重新激活会清除失活状态", () => RunSync(VerifyAutoLockStateClearsWhenActivatedDuringLoad)),
    ("时限内重新激活不会继续安排锁定", () => RunSync(VerifyAutoLockStateActivationBeforeDeadlineReturnsNone)),
    ("关闭启动保护时不记录失活状态", () => RunSync(VerifyAutoLockStateIgnoresDeactivationWhenDisabled)),
    ("关闭启动保护可清除既有失活状态", () => RunSync(VerifyAutoLockStateCanClearExistingDeactivation)),
    ("系统时钟回拨不会延长自动锁定时限", () => RunSync(VerifyAutoLockStateClampsClockRollback)),
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
    ("重复密钥使用专用异常且写保护异常保持通用", VerifyDuplicateMfaSecretExceptionClassificationAsync),
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
    ("旧挂载排队的分组同步不得进入新挂载", () => RunSync(VerifyQueuedGroupSynchronizationIsolatedByAttachEpoch)),
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
    ("验证码最后五秒进入临期状态", () => RunSync(VerifyAccountExpiryState)),
    ("账户临期覆盖层状态随剩余时间更新", () => RunSync(VerifyExpiryAlertOverlayState)),
    ("账户临期状态使用直接绑定的警示覆盖层", () => RunSync(VerifyExpiryProgressUsesDirectAlertOverlay)),
    ("紧凑账户行拒绝空模型", () => RunSync(VerifyCompactAccountRowNullModelSafety)),
    ("紧凑账户行安全处理损坏密钥", () => RunSync(VerifyCompactAccountRowInvalidSecrets)),
    ("账户编辑不要求 Hello 或确认", () => RunSync(VerifyEditActionPolicy)),
    ("查看密钥只要求 Hello", () => RunSync(VerifyViewSecretActionPolicy)),
    ("删除账户只要求确认", () => RunSync(VerifyDeleteActionPolicy)),
    ("账户草稿密钥输入必须默认遮挡且结束清空", () => RunSync(VerifyAccountDraftSecretInputIsMaskedAndCleared)),
    ("账户行 Ctrl+C 只接受精确 Ctrl 修饰键", () => RunSync(VerifyExactCopyShortcutPolicy)),
    ("窗口位置限制在主屏工作区", () => RunSync(VerifyWindowPlacementClampsToPrimaryWorkArea)),
    ("窗口位置修复损坏尺寸", () => RunSync(VerifyWindowPlacementRepairsDamagedSize)),
    ("窗口位置保留负坐标副屏", () => RunSync(VerifyWindowPlacementPreservesNegativeMonitorCoordinates)),
    ("副屏工作区转换为全局坐标", () => RunSync(VerifyDisplayWorkAreaUsesScreenCoordinates)),
    ("窗口尺寸不超出工作区", () => RunSync(VerifyWindowPlacementFitsWorkArea)),
    ("窗口位置计算防止整数溢出", () => RunSync(VerifyWindowPlacementHandlesExtremeValues)),
    ("窗口有效像素安全换算为物理像素", () => RunSync(VerifyEffectivePixelConversion)),
    ("窗口位置仅在普通状态发生变化时保存", () => RunSync(VerifyWindowPlacementSavePolicy)),
    ("柔和卡片窗口使用新默认和最小尺寸", () => RunSync(VerifySoftCardWindowDimensions)),
    ("剪贴板写入成功且只调用一次", () => RunSync(VerifyClipboardWriteSuccess)),
    ("剪贴板 COM 失败转换为中性结果", () => RunSync(VerifyClipboardWriteComFailure)),
    ("剪贴板拒绝空白验证码", () => RunSync(VerifyClipboardRejectsBlankText)),
    ("主窗口复制流程使用安全结果边界", () => RunSync(VerifyClipboardResultBoundaryInMainWindow)),
    ("敏感密钥复制必须消费剪贴板失败结果", () => RunSync(VerifySensitiveClipboardResultHandling)),
    ("剪贴板提示策略区分成功和失败", () => RunSync(VerifyClipboardStatusFeedbackPolicy)),
    ("恢复密钥策略只在成功时允许继续", () => RunSync(VerifyRecoveryKeyClipboardFeedbackPolicy)),
    ("截图响应锁定时延期且解锁后只消费一次", () => RunSync(VerifySnippingCaptureDefersUntilUnlockOnce)),
    ("锁定时截图终态延期到解锁并只消费一次", () => RunSync(VerifySnippingCaptureDefersTerminalStatusesUntilUnlockOnce)),
    ("截图响应忽略错误关联和重复回调", () => RunSync(VerifySnippingCaptureIgnoresMismatchedAndDuplicateResponses)),
    ("截图响应状态映射和令牌校验", () => RunSync(VerifySnippingCaptureStatusMappingAndTokenValidation)),
    ("截图新请求替换旧请求和延期令牌", () => RunSync(VerifySnippingCaptureBeginReplacesPreviousState)),
    ("截图放弃请求会清除活动状态和延期令牌", () => RunSync(VerifySnippingCaptureAbandonClearsState)),
    ("旧截图启动失败不得放弃较新的活动请求", () => RunSync(VerifySnippingCaptureConditionalAbandonPreservesNewerRequest)),
    ("截图请求拒绝空关联 ID", () => RunSync(VerifySnippingCaptureRejectsEmptyCorrelationId)),
    ("并发截图响应只允许一次立即兑换", VerifyConcurrentSnippingReceiveRedeemsOnceAsync),
    ("并发解锁恢复只允许一次立即兑换", VerifyConcurrentSnippingResumeRedeemsOnceAsync),
    ("新截图请求不会被旧响应回填延期令牌", VerifyConcurrentSnippingBeginRejectsOldPendingTokenAsync),
    ("放弃截图不会被旧响应回填延期令牌", VerifyConcurrentSnippingAbandonRejectsOldPendingTokenAsync),
    ("截图协议生成精确且安全编码的捕获 URI", () => RunSync(VerifySnippingToolCaptureUri)),
    ("截图协议解析有效响应和可选令牌", () => RunSync(VerifySnippingToolParsesValidResponses)),
    ("截图协议拒绝非目标响应端点", () => RunSync(VerifySnippingToolRejectsInvalidResponseEndpoints)),
    ("截图协议拒绝缺失或无效的必填参数", () => RunSync(VerifySnippingToolRejectsInvalidRequiredParameters)),
    ("截图协议拒绝重复关键参数", () => RunSync(VerifySnippingToolRejectsDuplicateCriticalParameters)),
    ("二维码导入按统一顺序确认并只保存一次", VerifyQrAccountImportValidAsync),
    ("二维码导入拒绝空白解码结果且不继续处理", VerifyQrAccountImportRejectsBlankDecodeAsync),
    ("二维码导入拒绝无效 URI 且不继续处理", VerifyQrAccountImportRejectsInvalidUriAsync),
    ("二维码导入确认取消时不保存", VerifyQrAccountImportConfirmationCancellationAsync),
    ("二维码导入在四个会话检查点失效时取消", VerifyQrAccountImportSessionInvalidationAsync),
    ("二维码导入在四个取消检查点停止敏感步骤", VerifyQrAccountImportCancellationBoundariesAsync),
    ("旧会话锁定取消后不得串入新解锁会话", VerifyQrAccountImportLockThenReunlockAsync),
    ("并发二维码导入保持各自会话身份和取消域", VerifyQrAccountImportConcurrentContextsAsync),
    ("二维码导入保存确认后的编辑草稿", VerifyQrAccountImportPassesConfirmedDraftAsync),
    ("二维码导入将保存冲突映射为冲突结果", VerifyQrAccountImportConflictAsync),
    ("二维码导入传播所有非冲突异常", VerifyQrAccountImportExceptionPropagationAsync),
    ("二维码导入拒绝空文件和空委托", VerifyQrAccountImportNullArgumentsAsync),
    ("截图服务按协议 URI 启动一次并返回结果", VerifySnippingToolCaptureLaunchAsync),
    ("截图服务按原样令牌兑换一次并返回同一文件", VerifySnippingToolCaptureRedeemAsync),
    ("截图服务拒绝空关联和空白令牌", VerifySnippingToolCaptureRejectsInvalidArgumentsAsync),
    ("截图服务拒绝空委托并传播委托异常", VerifySnippingToolCaptureDependencyBoundariesAsync),
    ("截图服务拒绝空任务和空兑换文件且不泄露输入", VerifySnippingToolCaptureNullResultsAsync),
    ("添加账户统一入口源契约", () => RunSync(VerifyUnifiedAddAccountEntrySourceContract)),
    ("主窗口添加账户、标题栏与截图生命周期源契约", () => RunSync(VerifyMainWindowAddAccountFlowSourceContract)),
    ("激活调度队列保持首次参数优先的 FIFO", () => RunSync(VerifyActivationDispatchQueueInitialFirstFifo)),
    ("激活调度队列并发入队不丢失且只保留一个排空预约", () => RunSync(VerifyActivationDispatchQueueConcurrentEnqueue)),
    ("激活调度队列在线性化判空后不会丢失唤醒", () => RunSync(VerifyActivationDispatchQueueDrainRace)),
    ("激活调度队列可从调度失败预约中恢复", () => RunSync(VerifyActivationDispatchQueueScheduleFailureRelease)),
    ("激活调度队列停止后清空并拒绝后续参数", () => RunSync(VerifyActivationDispatchQueueStop)),
    ("单实例协议激活入口源契约", () => RunSync(VerifySingleInstanceActivationSourceContract)),
};

static void VerifyActivationDispatchQueueInitialFirstFifo()
{
    var queue = new ActivationDispatchQueue<string>();
    Assert(queue.TryEnqueue("initial"), "首次激活参数必须成功入队。");
    Assert(queue.TryEnqueue("redirect-1") && queue.TryEnqueue("redirect-2"),
        "启动间隙内的重定向参数必须成功入队。");
    Assert(queue.TryReserveDrain(), "有待处理参数时必须能预约排空。");
    Assert(!queue.TryReserveDrain(), "同一批参数只能保留一个排空预约。");
    Assert(queue.TryBeginDrain(), "已预约的排空必须能开始。");

    var actual = new List<string>();
    while (queue.TryTake(out var item))
    {
        actual.Add(item);
    }

    Assert(actual.SequenceEqual(["initial", "redirect-1", "redirect-2"]),
        "激活参数必须保持 initial-first FIFO 顺序。");
    Assert(!queue.CompleteDrain(), "队列已空时不应预约下一轮排空。");
}

static void VerifyActivationDispatchQueueConcurrentEnqueue()
{
    const int itemCount = 512;
    var queue = new ActivationDispatchQueue<int>();
    var acceptedCount = 0;
    Parallel.For(0, itemCount, item =>
    {
        if (queue.TryEnqueue(item))
        {
            Interlocked.Increment(ref acceptedCount);
        }
    });

    Assert(acceptedCount == itemCount, "并发入队不得拒绝、丢失或重复参数。");
    var reservationCount = 0;
    Parallel.For(0, 64, _ =>
    {
        if (queue.TryReserveDrain())
        {
            Interlocked.Increment(ref reservationCount);
        }
    });

    Assert(reservationCount == 1, "并发预约必须只有一个调用方获得排空权。");
    Assert(queue.TryBeginDrain(), "唯一预约必须能开始排空。");
    Assert(!queue.TryBeginDrain(), "同一时刻不得开始第二个排空。");

    var actual = new List<int>();
    while (queue.TryTake(out var item))
    {
        actual.Add(item);
    }

    Assert(!queue.CompleteDrain(), "并发入队全部消费后不应安排空排空。");
    Assert(actual.Count == itemCount
            && actual.Distinct().Count() == itemCount
            && actual.OrderBy(item => item).SequenceEqual(Enumerable.Range(0, itemCount)),
        "并发入队的每个参数必须且只能排空一次。");
}

static void VerifyActivationDispatchQueueDrainRace()
{
    var queue = new ActivationDispatchQueue<string>();
    Assert(queue.TryEnqueue("first") && queue.TryReserveDrain() && queue.TryBeginDrain(),
        "测试必须先开始第一轮排空。");
    Assert(queue.TryTake(out var first) && first == "first", "第一轮必须先取出既有参数。");

    Assert(queue.TryEnqueue("during-drain"), "排空期间必须接受新参数。");
    Assert(!queue.TryReserveDrain(), "排空期间不得并发预约第二个 drain。");
    Assert(queue.TryTake(out var duringDrain) && duringDrain == "during-drain",
        "排空期间先到达的参数可由同一 drain 顺序取出。");
    Assert(!queue.TryTake(out _), "判空点必须可被测试观察。");

    Assert(queue.TryEnqueue("after-empty-check"), "判空后、收尾前到达的参数必须成功入队。");
    Assert(queue.CompleteDrain(), "收尾必须为竞态到达的新参数原子预约下一轮 drain。");
    Assert(queue.TryBeginDrain(), "收尾预约的下一轮 drain 必须可开始。");
    Assert(queue.TryTake(out var afterEmptyCheck) && afterEmptyCheck == "after-empty-check",
        "判空竞态中的参数不得丢失或永久卡住。");
    Assert(!queue.TryTake(out _) && !queue.CompleteDrain(), "第二轮排空后队列必须回到空闲状态。");
}

static void VerifyActivationDispatchQueueScheduleFailureRelease()
{
    var queue = new ActivationDispatchQueue<int>();
    Assert(queue.TryEnqueue(7) && queue.TryReserveDrain(), "测试必须先取得调度预约。");
    queue.ReleaseDrainReservation();
    Assert(queue.TryReserveDrain(), "调度失败释放预约后，同一参数必须可重新预约。");
    Assert(queue.TryBeginDrain() && queue.TryTake(out var item) && item == 7,
        "重新预约后不得丢失原参数。");
    Assert(!queue.TryTake(out _) && !queue.CompleteDrain(), "恢复后的 drain 必须能正常完成。");
}

static void VerifyActivationDispatchQueueStop()
{
    var queue = new ActivationDispatchQueue<int>();
    Assert(queue.TryEnqueue(1) && queue.TryReserveDrain() && queue.TryBeginDrain(),
        "测试必须先进入排空状态。");
    Assert(queue.TryEnqueue(2), "停止前必须接受排空期间的新参数。");

    queue.Stop();

    Assert(!queue.TryEnqueue(3), "停止后必须拒绝后续参数。");
    Assert(!queue.TryTake(out _), "停止必须清除尚未消费的参数。");
    Assert(!queue.CompleteDrain(), "停止后不得预约后续排空。");
    queue.ReleaseDrainReservation();
    Assert(!queue.TryReserveDrain() && !queue.TryBeginDrain(), "停止状态不得被释放预约操作恢复。");
}

static void VerifySingleInstanceActivationSourceContract()
{
    var programPath = FindWorkspaceSourceFile(Path.Combine("LiubaiMfa.Windows", "Program.cs"));
    var programSource = File.ReadAllText(programPath);
    var programRoot = ParseCSharpRoot(programSource, "Program.cs");
    var projectPath = FindWorkspaceSourceFile(Path.Combine("LiubaiMfa.Windows", "LiubaiMfa.Windows.csproj"));
    var project = XDocument.Load(projectPath);
    var defineConstants = project.Descendants()
        .Where(element => element.Name.LocalName == "DefineConstants")
        .Select(element => element.Value.Trim())
        .ToArray();
    Assert(defineConstants.Contains("$(DefineConstants);DISABLE_XAML_GENERATED_MAIN", StringComparer.Ordinal),
        "WinUI 项目必须追加 DISABLE_XAML_GENERATED_MAIN，且不得覆盖已有编译常量。");

    var programClass = FindUniqueClass(programRoot, "Program");
    var main = programClass.Members.OfType<MethodDeclarationSyntax>()
        .SingleOrDefault(method => method.Identifier.ValueText == "Main")
        ?? throw new InvalidOperationException("Program 必须声明自定义 Main 入口。");
    Assert(main.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.StaticKeyword))
            && main.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.AsyncKeyword))
            && main.AttributeLists.SelectMany(list => list.Attributes)
                .Any(attribute => attribute.Name.ToString() == "STAThread"),
        "自定义 Main 必须是带 STAThread 的 static async 入口。");
    Assert(main.ReturnType.ToString() == "Task", "自定义 Main 必须返回 Task。");
    Assert(main.Body is { Statements.Count: > 0 }, "自定义 Main 必须有可检查的语句体。");
    Assert(main.Body!.Statements[0].ToString() == "WinRT.ComWrappersSupport.InitializeComWrappers();",
        "Main 必须先初始化 WinRT COM wrappers，再调用任何 AppInstance 或 UI API。");

    var mainSource = main.ToFullString();
    AssertOrderedSource(mainSource,
        "WinRT.ComWrappersSupport.InitializeComWrappers()",
        "AppInstance.GetCurrent()",
        "GetActivatedEventArgs()",
        "App.SetInitialActivation",
        "current.Activated += App.OnAppInstanceActivated",
        "AppInstance.FindOrRegisterForKey(\"liubai-mfa-main\")",
        "Application.Start");
    Assert(CountOccurrences(mainSource, "Application.Start") == 1,
        "主实例必须且只能调用一次 Application.Start。");

    var redirectBranch = main.DescendantNodes().OfType<IfStatementSyntax>()
        .SingleOrDefault(statement => statement.Condition.ToString().Replace(" ", string.Empty, StringComparison.Ordinal)
            == "!main.IsCurrent");
    Assert(redirectBranch is not null, "非主实例必须通过 !main.IsCurrent 分支重定向。");
    var redirectSource = redirectBranch!.Statement.ToFullString();
    AssertOrderedSource(redirectSource,
        "current.Activated -= App.OnAppInstanceActivated",
        "await main.RedirectActivationToAsync(initial)",
        "return");
    Assert(!redirectSource.Contains("Application.Start", StringComparison.Ordinal),
        "非主实例不得调用 Application.Start。");
    Assert(mainSource.IndexOf(redirectBranch.ToFullString(), StringComparison.Ordinal)
            < mainSource.IndexOf("Application.Start", StringComparison.Ordinal),
        "只有通过非主实例重定向分支后，主实例才能启动应用。");

    var appPath = FindWorkspaceSourceFile(Path.Combine("LiubaiMfa.Windows", "App.xaml.cs"));
    var appSource = File.ReadAllText(appPath);
    var appRoot = ParseCSharpRoot(appSource, "App.xaml.cs");
    var appClass = FindUniqueClass(appRoot, "App");
    Assert(appClass.Members.OfType<MethodDeclarationSyntax>()
            .Any(method => method.Identifier.ValueText == "SetInitialActivation"),
        "App 必须提供 SetInitialActivation。");
    Assert(appClass.Members.OfType<MethodDeclarationSyntax>()
            .Any(method => method.Identifier.ValueText == "OnAppInstanceActivated"),
        "App 必须提供 OnAppInstanceActivated。");
    Assert(appSource.Contains("ActivationDispatchQueue<AppActivationArguments>", StringComparison.Ordinal)
            && !appClass.Members.OfType<FieldDeclarationSyntax>()
                .Any(field => field.Declaration.Type is GenericNameSyntax genericType
                    && genericType.Identifier.ValueText == "Queue")
            && !appSource.Contains("lock (ActivationGate)", StringComparison.Ordinal),
        "App 必须只使用可测试的 ActivationDispatchQueue 协调激活，不得保留第二套手写 Queue/gate 状态。");
    var setInitialActivation = appClass.Members.OfType<MethodDeclarationSyntax>()
        .Single(method => method.Identifier.ValueText == "SetInitialActivation");
    Assert(setInitialActivation.ToFullString().Contains("PendingActivations.TryEnqueue(activation)", StringComparison.Ordinal),
        "首次激活必须先进入统一调度队列。");
    var redirectedActivation = appClass.Members.OfType<MethodDeclarationSyntax>()
        .Single(method => method.Identifier.ValueText == "OnAppInstanceActivated");
    AssertOrderedSource(redirectedActivation.ToFullString(),
        "PendingActivations.TryEnqueue(activation)",
        "Volatile.Read(ref _current)?.RequestActivationDrain()");
    Assert(appSource.Contains("DispatcherQueue.GetForCurrentThread()", StringComparison.Ordinal)
            && appSource.Contains("_dispatcherQueue.TryEnqueue(DispatchActivationDrain)", StringComparison.Ordinal)
            && appSource.Contains("PendingActivations.Stop();", StringComparison.Ordinal),
        "App 必须记录 UI Dispatcher；TryEnqueue 失败时停止并清理队列，不得向系统激活线程抛错。");
    Assert(appSource.Contains("if (_window is null)", StringComparison.Ordinal)
            && CountOccurrences(appSource, "new MainWindow()") == 1
            && appSource.Contains("Volatile.Write(ref _windowReady, 1)", StringComparison.Ordinal),
        "OnLaunched 必须且只能创建一个 MainWindow。");
    Assert(appSource.Contains("PendingActivations.TryReserveDrain()", StringComparison.Ordinal)
            && appSource.Contains("PendingActivations.TryBeginDrain()", StringComparison.Ordinal)
            && appSource.Contains("while (PendingActivations.TryTake(out var activation))", StringComparison.Ordinal)
            && appSource.Contains("await HandleActivationAsync", StringComparison.Ordinal)
            && appSource.Contains("PendingActivations.CompleteDrain()", StringComparison.Ordinal),
        "App 必须仅在预约成功后派发，并在 UI 线程串行 drain；判空收尾竞态由调度队列处理。");
    Assert(appSource.Contains("activation.Kind != ExtendedActivationKind.Protocol", StringComparison.Ordinal)
            && appSource.Contains("activation.Data is not IProtocolActivatedEventArgs", StringComparison.Ordinal)
            && appSource.Contains("await _window.HandleProtocolActivationAsync(protocolArgs.Uri)", StringComparison.Ordinal)
            && CountOccurrences(appSource, "HandleProtocolActivationAsync(protocolArgs.Uri)") == 1,
        "只有 Protocol 且数据实现 IProtocolActivatedEventArgs 时才能路由 URI。");
    Assert(appSource.Contains("_window.Activate();", StringComparison.Ordinal),
        "Launch 和未知激活必须只激活现有窗口。");
    var debugStatements = appRoot.DescendantNodes().OfType<InvocationExpressionSyntax>()
        .Where(invocation => invocation.Expression.ToString() == "Debug.WriteLine")
        .Select(invocation => invocation.ToString())
        .ToArray();
    Assert(appSource.Contains("Debug.WriteLine($\"Activation routing failed: {exception.GetType().Name}\")", StringComparison.Ordinal)
            && debugStatements.All(statement => !statement.Contains("Uri", StringComparison.OrdinalIgnoreCase)
                && !statement.Contains("token", StringComparison.OrdinalIgnoreCase)),
        "异步激活异常必须在安全边界观察，且日志只能记录异常类型，不能记录 URI 或令牌。");
    Assert(!appClass.Members.OfType<MethodDeclarationSyntax>()
            .Any(method => method.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.AsyncKeyword))
                && method.ReturnType is PredefinedTypeSyntax type
                && type.Keyword.IsKind(SyntaxKind.VoidKeyword)),
        "App 激活路由不得使用 async void 形成未观察异常边界。");

    var dispatchQueuePath = FindWorkspaceSourceFile(
        Path.Combine("LiubaiMfa.Windows", "Services", "ActivationDispatchQueue.cs"));
    var dispatchQueueSource = File.ReadAllText(dispatchQueuePath);
    var dispatchQueueRoot = ParseCSharpRoot(dispatchQueueSource, "ActivationDispatchQueue.cs");
    var dispatchQueueClass = FindUniqueClass(dispatchQueueRoot, "ActivationDispatchQueue");
    Assert(dispatchQueueClass.TypeParameterList?.Parameters.Count == 1,
        "ActivationDispatchQueue 必须是单类型参数的泛型协调器。");
    Assert(!dispatchQueueSource.Contains("Microsoft.UI", StringComparison.Ordinal)
            && !dispatchQueueSource.Contains("Dispatcher", StringComparison.Ordinal)
            && !dispatchQueueSource.Contains("Func<", StringComparison.Ordinal)
            && !dispatchQueueSource.Contains("Action<", StringComparison.Ordinal)
            && !dispatchQueueClass.Members.OfType<MethodDeclarationSyntax>()
                .Any(method => method.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.AsyncKeyword))),
        "ActivationDispatchQueue 不得依赖 WinUI、Dispatcher、异步或外部委托。");
    var queueMethodNames = dispatchQueueClass.Members.OfType<MethodDeclarationSyntax>()
        .Select(method => method.Identifier.ValueText)
        .ToArray();
    Assert(new[]
        {
            "TryEnqueue",
            "TryReserveDrain",
            "ReleaseDrainReservation",
            "TryBeginDrain",
            "TryTake",
            "CompleteDrain",
            "Stop",
        }.All(queueMethodNames.Contains),
        "ActivationDispatchQueue 必须完整提供入队、预约、排空、失败释放和停止状态操作。");

    var mainWindowPath = FindWorkspaceSourceFile(Path.Combine("LiubaiMfa.Windows", "MainWindow.xaml.cs"));
    var mainWindowSource = File.ReadAllText(mainWindowPath);
    var mainWindowRoot = ParseCSharpRoot(mainWindowSource, "MainWindow.xaml.cs");
    var mainWindowClass = FindUniqueClass(mainWindowRoot, "MainWindow");
    var protocolHandler = mainWindowClass.Members.OfType<MethodDeclarationSyntax>()
        .SingleOrDefault(method => method.Identifier.ValueText == "HandleProtocolActivationAsync")
        ?? throw new InvalidOperationException(
            "MainWindow 必须提供 internal async Task HandleProtocolActivationAsync(Uri uri) 协议接缝。");
    Assert(protocolHandler.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.InternalKeyword))
            && protocolHandler.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.AsyncKeyword))
            && protocolHandler.ReturnType.ToString() == "Task",
        "MainWindow 必须提供 internal async Task HandleProtocolActivationAsync(Uri uri) 协议接缝。");
    var protocolHandlerBody = protocolHandler.Body?.ToString() ?? string.Empty;
    Assert(protocolHandlerBody.Contains("ArgumentNullException.ThrowIfNull(uri);", StringComparison.Ordinal)
            && protocolHandlerBody.Contains("SnippingToolProtocol.TryParseResponse", StringComparison.Ordinal)
            && protocolHandlerBody.Contains("_snippingCaptureState.Receive", StringComparison.Ordinal),
        "协议激活接缝必须校验 URI 并只将严格解析后的响应交给截图状态机。");
}

static void AssertOrderedSource(string source, params string[] fragments)
{
    var position = -1;
    foreach (var fragment in fragments)
    {
        var next = source.IndexOf(fragment, position + 1, StringComparison.Ordinal);
        Assert(next >= 0, $"源契约缺少片段：{fragment}");
        Assert(next > position, $"源契约顺序错误：{fragment}");
        position = next;
    }
}

static int CountOccurrences(string source, string fragment)
{
    var count = 0;
    var position = 0;
    while ((position = source.IndexOf(fragment, position, StringComparison.Ordinal)) >= 0)
    {
        count++;
        position += fragment.Length;
    }

    return count;
}

static void VerifySnippingToolCaptureUri()
{
    var correlationId = Guid.Parse("55555555-5555-4555-8555-555555555555");
    var uri = SnippingToolProtocol.CreateCaptureUri(correlationId);

    Assert(uri.Scheme == "ms-screenclip", "截图捕获 URI scheme 必须精确为 ms-screenclip。");
    Assert(uri.Host == "capture", "截图捕获 URI host 必须精确为 capture。");
    Assert(uri.AbsolutePath == "/image", "截图捕获 URI path 必须精确为 /image。");

    var queryParts = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
    Assert(queryParts.Contains("rectangle", StringComparer.Ordinal), "截图捕获 URI 必须包含裸 rectangle 参数。");
    Assert(!queryParts.Any(part => part.StartsWith("rectangle=", StringComparison.Ordinal)),
        "rectangle 参数不得带等号和值。");
    Assert(queryParts.Contains("enabledModes=RectangleSnip", StringComparer.Ordinal),
        "截图捕获 URI 必须只启用矩形截图模式。");
    Assert(queryParts.Contains("user-agent=LiubaiMFA", StringComparer.Ordinal),
        "截图捕获 URI 必须携带固定 user-agent。");
    Assert(queryParts.Contains("api-version=1.2", StringComparer.Ordinal),
        "截图捕获 URI 必须使用 1.2 协议版本。");
    Assert(queryParts.Contains("redirect-uri=liubai-mfa%3A%2F%2Fcapture-response", StringComparer.Ordinal),
        "截图捕获 URI 必须安全编码固定回调地址。");
    Assert(queryParts.Contains($"x-request-correlation-id={correlationId:D}", StringComparer.Ordinal),
        "截图捕获 URI 必须携带 D 格式关联 ID。");
    Assert(!queryParts.Any(part => part.StartsWith("auto-save", StringComparison.OrdinalIgnoreCase)),
        "截图捕获 URI 不得启用 auto-save。");
    AssertThrows<ArgumentException>(() => SnippingToolProtocol.CreateCaptureUri(Guid.Empty),
        "截图捕获 URI 必须拒绝空关联 ID。");
}

static async Task VerifyQrAccountImportValidAsync()
{
    var file = await GetRegressionAssemblyStorageFileAsync();
    const long sessionToken = 101;
    using var cancellation = new CancellationTokenSource();
    var calls = new List<string>();
    AccountDraft? savedDraft = null;
    var coordinator = new QrAccountImportCoordinator(
        _ =>
        {
            calls.Add("decode");
            return Task.FromResult<string?>(CreateValidOtpAuthUri());
        },
        (actualToken, draft, actualCancellationToken) =>
        {
            calls.Add("confirm");
            Assert(actualToken == sessionToken && actualCancellationToken == cancellation.Token,
                "确认委托必须收到本次导入的会话身份和取消域。");
            Assert(draft == new AccountDraft("示例服务", "示例账户", "JBSWY3DPEHPK3PXP", null),
                "有效二维码必须按解析结果创建无分组账户草稿。");
            return Task.FromResult<AccountDraft?>(draft);
        },
        (actualToken, draft, actualCancellationToken) =>
        {
            calls.Add("save");
            Assert(actualToken == sessionToken && actualCancellationToken == cancellation.Token,
                "保存委托必须收到本次导入的会话身份和取消域。");
            savedDraft = draft;
            return Task.FromResult(QrAccountSaveOutcome.Imported);
        },
        actualToken =>
        {
            calls.Add("session");
            return actualToken == sessionToken;
        });

    var outcome = await coordinator.ImportAsync(file, sessionToken, cancellation.Token);

    Assert(outcome == QrAccountImportOutcome.Imported, "成功保存后必须返回 Imported。");
    Assert(savedDraft is not null, "有效二维码必须且只能进入一次保存委托。");
    Assert(calls.SequenceEqual(["session", "decode", "session", "confirm", "session", "save", "session"]),
        "二维码导入必须在 decode 前后、confirm 后和 save 后检查会话上下文。");
}

static async Task VerifyQrAccountImportRejectsBlankDecodeAsync()
{
    var file = await GetRegressionAssemblyStorageFileAsync();
    foreach (var decoded in new string?[] { null, string.Empty, " \t" })
    {
        var confirmCount = 0;
        var saveCount = 0;
        var coordinator = new QrAccountImportCoordinator(
            _ => Task.FromResult(decoded),
            (_, draft, _) =>
            {
                confirmCount++;
                return Task.FromResult<AccountDraft?>(draft);
            },
            (_, _, _) =>
            {
                saveCount++;
                return Task.FromResult(QrAccountSaveOutcome.Imported);
            },
            _ => true);

        Assert(await coordinator.ImportAsync(file, 102, CancellationToken.None) == QrAccountImportOutcome.InvalidQr,
            "空或空白解码结果必须返回 InvalidQr。");
        Assert(confirmCount == 0 && saveCount == 0, "空或空白解码结果不得确认或保存。");
    }
}

static async Task VerifyQrAccountImportRejectsInvalidUriAsync()
{
    var file = await GetRegressionAssemblyStorageFileAsync();
    var confirmCount = 0;
    var saveCount = 0;
    var coordinator = new QrAccountImportCoordinator(
        _ => Task.FromResult<string?>("https://example.invalid/not-otp"),
        (_, draft, _) =>
        {
            confirmCount++;
            return Task.FromResult<AccountDraft?>(draft);
        },
        (_, _, _) =>
        {
            saveCount++;
            return Task.FromResult(QrAccountSaveOutcome.Imported);
        },
        _ => true);

    Assert(await coordinator.ImportAsync(file, 103, CancellationToken.None) == QrAccountImportOutcome.InvalidQr,
        "无法解析的 URI 必须返回 InvalidQr。");
    Assert(confirmCount == 0 && saveCount == 0, "无效 URI 不得确认或保存。");
}

static async Task VerifyQrAccountImportConfirmationCancellationAsync()
{
    var file = await GetRegressionAssemblyStorageFileAsync();
    var sessionCheckCount = 0;
    var saveCount = 0;
    var coordinator = new QrAccountImportCoordinator(
        _ => Task.FromResult<string?>(CreateValidOtpAuthUri()),
        (_, _, _) => Task.FromResult<AccountDraft?>(null),
        (_, _, _) =>
        {
            saveCount++;
            return Task.FromResult(QrAccountSaveOutcome.Imported);
        },
        _ =>
        {
            sessionCheckCount++;
            return true;
        });

    Assert(await coordinator.ImportAsync(file, 104, CancellationToken.None) == QrAccountImportOutcome.Cancelled,
        "确认返回空草稿时必须返回 Cancelled。");
    Assert(sessionCheckCount == 3, "确认结束后必须执行第三个会话检查点。");
    Assert(saveCount == 0, "确认取消后不得保存。");
}

static async Task VerifyQrAccountImportSessionInvalidationAsync()
{
    var file = await GetRegressionAssemblyStorageFileAsync();
    foreach (var invalidCheck in new[] { 1, 2, 3, 4 })
    {
        var sessionCheckCount = 0;
        var decodeCount = 0;
        var confirmCount = 0;
        var saveCount = 0;
        var coordinator = new QrAccountImportCoordinator(
            _ =>
            {
                decodeCount++;
                return Task.FromResult<string?>(CreateValidOtpAuthUri());
            },
            (_, draft, _) =>
            {
                confirmCount++;
                return Task.FromResult<AccountDraft?>(draft);
            },
            (_, _, _) =>
            {
                saveCount++;
                return Task.FromResult(QrAccountSaveOutcome.Imported);
            },
            _ => ++sessionCheckCount != invalidCheck);

        Assert(await coordinator.ImportAsync(file, 105, CancellationToken.None) == QrAccountImportOutcome.Cancelled,
            "任一会话检查点失效都必须返回 Cancelled。");
        Assert(decodeCount == (invalidCheck == 1 ? 0 : 1), "首个会话检查失败时不得开始解码。");
        Assert(confirmCount == (invalidCheck <= 2 ? 0 : 1), "会话失效后不得越过对应检查点继续确认。");
        Assert(saveCount == (invalidCheck <= 3 ? 0 : 1), "会话失效后不得越过对应检查点继续保存。");
    }
}

static async Task VerifyQrAccountImportCancellationBoundariesAsync()
{
    var file = await GetRegressionAssemblyStorageFileAsync();
    foreach (var cancelledAt in new[] { 1, 2, 3, 4 })
    {
        using var cancellation = new CancellationTokenSource();
        var decodeCount = 0;
        var confirmCount = 0;
        var saveCount = 0;
        if (cancelledAt == 1)
        {
            cancellation.Cancel();
        }

        var coordinator = new QrAccountImportCoordinator(
            _ =>
            {
                decodeCount++;
                if (cancelledAt == 2)
                {
                    cancellation.Cancel();
                }

                return Task.FromResult<string?>(CreateValidOtpAuthUri());
            },
            (_, draft, actualCancellationToken) =>
            {
                confirmCount++;
                Assert(actualCancellationToken == cancellation.Token, "确认委托不得收到其他调用的取消域。");
                if (cancelledAt == 3)
                {
                    cancellation.Cancel();
                }

                return Task.FromResult<AccountDraft?>(draft);
            },
            (_, _, actualCancellationToken) =>
            {
                saveCount++;
                Assert(actualCancellationToken == cancellation.Token, "保存委托不得收到其他调用的取消域。");
                if (cancelledAt == 4)
                {
                    cancellation.Cancel();
                }

                return Task.FromResult(QrAccountSaveOutcome.Imported);
            },
            _ => true);

        Assert(await coordinator.ImportAsync(file, 106, cancellation.Token) == QrAccountImportOutcome.Cancelled,
            "绑定取消域在任一检查点取消后必须返回 Cancelled。");
        Assert(decodeCount == (cancelledAt == 1 ? 0 : 1), "调用前已取消时不得开始解码。");
        Assert(confirmCount == (cancelledAt <= 2 ? 0 : 1), "取消后不得继续确认。");
        Assert(saveCount == (cancelledAt <= 3 ? 0 : 1), "取消后不得继续保存。");
    }
}

static async Task VerifyQrAccountImportLockThenReunlockAsync()
{
    var file = await GetRegressionAssemblyStorageFileAsync();
    const long oldToken = 201;
    const long newToken = 202;
    var currentToken = oldToken;
    var decodeCount = 0;
    var confirmedTokens = new List<long>();
    var savedTokens = new List<long>();
    var firstDecodeStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseFirstDecode = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    var coordinator = new QrAccountImportCoordinator(
        async _ =>
        {
            if (Interlocked.Increment(ref decodeCount) == 1)
            {
                firstDecodeStarted.TrySetResult(true);
                await releaseFirstDecode.Task;
            }

            return CreateValidOtpAuthUri();
        },
        (token, draft, _) =>
        {
            confirmedTokens.Add(token);
            return Task.FromResult<AccountDraft?>(draft);
        },
        (token, _, _) =>
        {
            savedTokens.Add(token);
            return Task.FromResult(QrAccountSaveOutcome.Imported);
        },
        token => token == Interlocked.Read(ref currentToken));
    using var oldCancellation = new CancellationTokenSource();

    var oldImport = coordinator.ImportAsync(file, oldToken, oldCancellation.Token);
    await firstDecodeStarted.Task;
    oldCancellation.Cancel();
    Interlocked.Exchange(ref currentToken, newToken);

    var newImport = await coordinator.ImportAsync(file, newToken, CancellationToken.None);
    releaseFirstDecode.TrySetResult(true);
    var oldOutcome = await oldImport;

    Assert(newImport == QrAccountImportOutcome.Imported, "新解锁会话必须能独立完成导入。");
    Assert(oldOutcome == QrAccountImportOutcome.Cancelled, "锁定并取消的旧会话必须返回 Cancelled。");
    Assert(confirmedTokens.SequenceEqual([newToken]) && savedTokens.SequenceEqual([newToken]),
        "旧会话不得在新 token 解锁后进入确认或保存。");
}

static async Task VerifyQrAccountImportConcurrentContextsAsync()
{
    var file = await GetRegressionAssemblyStorageFileAsync();
    const long firstToken = 301;
    const long secondToken = 302;
    using var firstCancellation = new CancellationTokenSource();
    using var secondCancellation = new CancellationTokenSource();
    var confirmStartedCount = 0;
    var bothConfirmStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    var confirmedContexts = new Dictionary<long, CancellationToken>();
    var savedContexts = new Dictionary<long, CancellationToken>();
    var sync = new object();
    var coordinator = new QrAccountImportCoordinator(
        _ => Task.FromResult<string?>(CreateValidOtpAuthUri()),
        async (token, draft, cancellationToken) =>
        {
            lock (sync)
            {
                confirmedContexts.Add(token, cancellationToken);
            }

            if (Interlocked.Increment(ref confirmStartedCount) == 2)
            {
                bothConfirmStarted.TrySetResult(true);
            }

            await bothConfirmStarted.Task;
            return draft with { Account = $"会话-{token}" };
        },
        (token, draft, cancellationToken) =>
        {
            Assert(draft.Account == $"会话-{token}", "并发保存不得收到其他会话确认后的草稿。");
            lock (sync)
            {
                savedContexts.Add(token, cancellationToken);
            }

            return Task.FromResult(QrAccountSaveOutcome.Imported);
        },
        token => token is firstToken or secondToken);

    var outcomes = await Task.WhenAll(
        coordinator.ImportAsync(file, firstToken, firstCancellation.Token),
        coordinator.ImportAsync(file, secondToken, secondCancellation.Token));

    Assert(outcomes.All(outcome => outcome == QrAccountImportOutcome.Imported),
        "两个独立有效上下文必须都能完成导入。");
    Assert(confirmedContexts[firstToken] == firstCancellation.Token
            && confirmedContexts[secondToken] == secondCancellation.Token
            && savedContexts[firstToken] == firstCancellation.Token
            && savedContexts[secondToken] == secondCancellation.Token,
        "并发导入的 token 和取消域不得串用。");
}

static async Task VerifyQrAccountImportPassesConfirmedDraftAsync()
{
    var file = await GetRegressionAssemblyStorageFileAsync();
    var confirmedDraft = new AccountDraft("编辑后服务", "编辑后账户", "GEZDGNBVGY3TQOJQ", "编辑后分组");
    AccountDraft? savedDraft = null;
    var coordinator = new QrAccountImportCoordinator(
        _ => Task.FromResult<string?>(CreateValidOtpAuthUri()),
        (_, _, _) => Task.FromResult<AccountDraft?>(confirmedDraft),
        (_, draft, _) =>
        {
            savedDraft = draft;
            return Task.FromResult(QrAccountSaveOutcome.Imported);
        },
        _ => true);

    Assert(await coordinator.ImportAsync(file, 401, CancellationToken.None) == QrAccountImportOutcome.Imported,
        "确认后编辑的草稿保存成功必须返回 Imported。");
    Assert(ReferenceEquals(savedDraft, confirmedDraft), "保存委托必须收到确认委托返回的同一草稿。");
}

static async Task VerifyQrAccountImportConflictAsync()
{
    var file = await GetRegressionAssemblyStorageFileAsync();
    var coordinator = new QrAccountImportCoordinator(
        _ => Task.FromResult<string?>(CreateValidOtpAuthUri()),
        (_, draft, _) => Task.FromResult<AccountDraft?>(draft),
        (_, _, _) => Task.FromResult(QrAccountSaveOutcome.Conflict),
        _ => true);

    Assert(await coordinator.ImportAsync(file, 402, CancellationToken.None) == QrAccountImportOutcome.Conflict,
        "保存委托返回结构化冲突时必须映射为 Conflict。");
}

static async Task VerifyQrAccountImportExceptionPropagationAsync()
{
    var file = await GetRegressionAssemblyStorageFileAsync();
    var propagatedFactories = new Func<Exception>[]
    {
        () => new IOException("模拟读取失败"),
        () => new FormatException("模拟格式失败"),
        () => new OperationCanceledException("模拟操作取消", new CancellationToken(canceled: true)),
    };

    foreach (var exceptionFactory in propagatedFactories.Append<Func<Exception>>(
        () => new InvalidOperationException("模拟解码异常")))
    {
        var coordinator = new QrAccountImportCoordinator(
            _ => throw exceptionFactory(),
            (_, draft, _) => Task.FromResult<AccountDraft?>(draft),
            (_, _, _) => Task.FromResult(QrAccountSaveOutcome.Imported),
            _ => true);
        await AssertThrowsAsyncExact(exceptionFactory().GetType(),
            () => coordinator.ImportAsync(file, 403, CancellationToken.None));
    }

    foreach (var exceptionFactory in propagatedFactories.Append<Func<Exception>>(
        () => new InvalidOperationException("模拟确认异常")))
    {
        var coordinator = new QrAccountImportCoordinator(
            _ => Task.FromResult<string?>(CreateValidOtpAuthUri()),
            (_, _, _) => throw exceptionFactory(),
            (_, _, _) => Task.FromResult(QrAccountSaveOutcome.Imported),
            _ => true);
        await AssertThrowsAsyncExact(exceptionFactory().GetType(),
            () => coordinator.ImportAsync(file, 404, CancellationToken.None));
    }

    foreach (var exceptionFactory in propagatedFactories
        .Append<Func<Exception>>(() => new InvalidOperationException("模拟非冲突保存异常"))
        .Append<Func<Exception>>(() => new ApplicationException("模拟其他保存异常")))
    {
        var coordinator = new QrAccountImportCoordinator(
            _ => Task.FromResult<string?>(CreateValidOtpAuthUri()),
            (_, draft, _) => Task.FromResult<AccountDraft?>(draft),
            (_, _, _) => Task.FromException<QrAccountSaveOutcome>(exceptionFactory()),
            _ => true);
        await AssertThrowsAsyncExact(exceptionFactory().GetType(),
            () => coordinator.ImportAsync(file, 405, CancellationToken.None));
    }
}

static async Task VerifyQrAccountImportNullArgumentsAsync()
{
    var file = await GetRegressionAssemblyStorageFileAsync();
    Func<StorageFile, Task<string?>> decode = _ => Task.FromResult<string?>(null);
    Func<long, AccountDraft, CancellationToken, Task<AccountDraft?>> confirm =
        (_, draft, _) => Task.FromResult<AccountDraft?>(draft);
    Func<long, AccountDraft, CancellationToken, Task<QrAccountSaveOutcome>> save =
        (_, _, _) => Task.FromResult(QrAccountSaveOutcome.Imported);
    Func<long, bool> isSessionCurrent = _ => true;

    AssertThrows<ArgumentNullException>(
        () => new QrAccountImportCoordinator(null!, confirm, save, isSessionCurrent),
        "二维码导入协调器必须拒绝空解码委托。");
    AssertThrows<ArgumentNullException>(
        () => new QrAccountImportCoordinator(decode, null!, save, isSessionCurrent),
        "二维码导入协调器必须拒绝空确认委托。");
    AssertThrows<ArgumentNullException>(
        () => new QrAccountImportCoordinator(decode, confirm, null!, isSessionCurrent),
        "二维码导入协调器必须拒绝空保存委托。");
    AssertThrows<ArgumentNullException>(
        () => new QrAccountImportCoordinator(decode, confirm, save, null!),
        "二维码导入协调器必须拒绝空会话检查委托。");

    var coordinator = new QrAccountImportCoordinator(decode, confirm, save, isSessionCurrent);
    await AssertThrowsAsync<ArgumentNullException>(
        () => coordinator.ImportAsync(null!, 406, CancellationToken.None));
    await AssertThrowsAsync<ArgumentException>(
        () => coordinator.ImportAsync(file, 0, CancellationToken.None));
}

static async Task VerifySnippingToolCaptureLaunchAsync()
{
    var correlationId = Guid.Parse("99999999-9999-4999-8999-999999999999");
    Uri? launchedUri = null;
    var launchCount = 0;
    var service = new SnippingToolCaptureService(
        uri =>
        {
            launchCount++;
            launchedUri = uri;
            return Task.FromResult(false);
        },
        _ => throw new InvalidOperationException("此测试不得兑换文件"));

    var launched = await service.LaunchAsync(correlationId);

    Assert(!launched, "截图服务必须原样返回启动委托的布尔结果。");
    Assert(launchCount == 1, "截图服务必须只调用一次启动委托。");
    Assert(launchedUri == SnippingToolProtocol.CreateCaptureUri(correlationId),
        "截图服务必须复用截图协议生成捕获 URI。");
}

static async Task VerifySnippingToolCaptureRedeemAsync()
{
    var expectedFile = await GetRegressionAssemblyStorageFileAsync();
    const string token = "capture-token+verbatim";
    string? receivedToken = null;
    var redeemCount = 0;
    var service = new SnippingToolCaptureService(
        _ => throw new InvalidOperationException("此测试不得启动截图"),
        actualToken =>
        {
            redeemCount++;
            receivedToken = actualToken;
            return Task.FromResult<StorageFile?>(expectedFile);
        });

    var actualFile = await service.RedeemAsync(token);

    Assert(ReferenceEquals(actualFile, expectedFile), "截图服务必须返回兑换委托给出的同一 StorageFile。");
    Assert(redeemCount == 1, "截图服务必须只调用一次兑换委托。");
    Assert(receivedToken == token, "有效兑换令牌不得裁剪、改写或重新编码。");
}

static async Task VerifySnippingToolCaptureRejectsInvalidArgumentsAsync()
{
    var launchCount = 0;
    var redeemCount = 0;
    var service = new SnippingToolCaptureService(
        _ =>
        {
            launchCount++;
            return Task.FromResult(true);
        },
        async _ =>
        {
            redeemCount++;
            return await GetRegressionAssemblyStorageFileAsync();
        });

    await AssertThrowsAsync<ArgumentException>(() => service.LaunchAsync(Guid.Empty));
    Assert(launchCount == 0, "空关联 ID 必须由协议拒绝且不得调用启动委托。");

    foreach (var token in new string?[] { null, string.Empty, " \t" })
    {
        await AssertThrowsAsync<ArgumentException>(() => service.RedeemAsync(token!));
    }

    Assert(redeemCount == 0, "空或空白令牌不得调用兑换委托。");
}

static async Task VerifySnippingToolCaptureDependencyBoundariesAsync()
{
    Func<Uri, Task<bool>> launch = _ => Task.FromResult(true);
    Func<string, Task<StorageFile?>> redeem = async _ => await GetRegressionAssemblyStorageFileAsync();
    AssertThrows<ArgumentNullException>(
        () => new SnippingToolCaptureService(null!, redeem),
        "截图服务必须拒绝空启动委托。");
    AssertThrows<ArgumentNullException>(
        () => new SnippingToolCaptureService(launch, null!),
        "截图服务必须拒绝空兑换委托。");

    var launchFailure = new IOException("模拟启动失败");
    var launchService = new SnippingToolCaptureService(
        _ => Task.FromException<bool>(launchFailure),
        redeem);
    await AssertThrowsSameAsync(launchFailure, () => launchService.LaunchAsync(Guid.NewGuid()));

    var redeemFailure = new OperationCanceledException("模拟兑换取消", new CancellationToken(canceled: true));
    var redeemService = new SnippingToolCaptureService(
        launch,
        _ => Task.FromException<StorageFile?>(redeemFailure));
    await AssertThrowsSameAsync(redeemFailure, () => redeemService.RedeemAsync("test-token"));
}

static async Task VerifySnippingToolCaptureNullResultsAsync()
{
    var launchService = new SnippingToolCaptureService(
        _ => null!,
        _ => Task.FromResult<StorageFile?>(null));
    var launchException = await CaptureThrowsAsync<InvalidOperationException>(
        () => launchService.LaunchAsync(Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa")));
    Assert(!launchException.Message.Contains("ms-screenclip", StringComparison.OrdinalIgnoreCase),
        "空启动任务错误不得泄露 URI 内容。");

    const string token = "private-test-token";
    foreach (var redeem in new Func<string, Task<StorageFile?>>[]
    {
        _ => null!,
        _ => Task.FromResult<StorageFile?>(null),
    })
    {
        var redeemService = new SnippingToolCaptureService(_ => Task.FromResult(true), redeem);
        var redeemException = await CaptureThrowsAsync<InvalidOperationException>(
            () => redeemService.RedeemAsync(token));
        Assert(!redeemException.Message.Contains(token, StringComparison.Ordinal),
            "空兑换结果错误不得泄露文件访问令牌。");
    }
}

static string CreateValidOtpAuthUri() =>
    "otpauth://totp/%E7%A4%BA%E4%BE%8B%E6%9C%8D%E5%8A%A1:%E7%A4%BA%E4%BE%8B%E8%B4%A6%E6%88%B7"
    + "?secret=JBSWY3DPEHPK3PXP&issuer=%E7%A4%BA%E4%BE%8B%E6%9C%8D%E5%8A%A1";

static async Task<StorageFile> GetRegressionAssemblyStorageFileAsync() =>
    await StorageFile.GetFileFromPathAsync(typeof(SnippingToolProtocol).Assembly.Location);

static async Task AssertThrowsAsyncExact(Type exceptionType, Func<Task> action)
{
    try
    {
        await action();
    }
    catch (Exception ex) when (ex.GetType() == exceptionType)
    {
        return;
    }

    throw new InvalidOperationException($"预期精确抛出 {exceptionType.Name}。");
}

static async Task AssertThrowsSameAsync(Exception expected, Func<Task> action)
{
    try
    {
        await action();
    }
    catch (Exception actual) when (ReferenceEquals(actual, expected))
    {
        return;
    }

    throw new InvalidOperationException($"预期传播同一 {expected.GetType().Name} 实例。");
}

static async Task<TException> CaptureThrowsAsync<TException>(Func<Task> action)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException ex)
    {
        return ex;
    }

    throw new InvalidOperationException($"预期抛出 {typeof(TException).Name}。");
}

static void VerifySnippingToolParsesValidResponses()
{
    var correlationId = Guid.Parse("66666666-6666-4666-8666-666666666666");
    var withToken = new Uri(
        $"LIUBAI-MFA://CAPTURE-RESPONSE?code=200&x-request-correlation-id={correlationId:D}"
        + "&file-access-token=token%2Bvalue%2Fone&reason=completed&future-field=allowed");

    Assert(SnippingToolProtocol.TryParseResponse(withToken, out var parsed), "合法截图响应必须解析成功。");
    Assert(parsed.CorrelationId == correlationId, "截图响应关联 ID 解析错误。");
    Assert(parsed.StatusCode == 200, "截图响应状态码解析错误。");
    Assert(parsed.FileAccessToken == "token+value/one", "截图响应令牌必须按查询编码解码。");

    var withoutToken = new Uri(
        $"liubai-mfa://capture-response?code=499&x-request-correlation-id={correlationId:D}&reason=cancelled");
    Assert(SnippingToolProtocol.TryParseResponse(withoutToken, out var cancelled),
        "缺少可选令牌的合法截图响应必须解析成功。");
    Assert(cancelled.StatusCode == 499 && cancelled.FileAccessToken is null,
        "缺少可选令牌时必须保留空值。");

    var emptyToken = new Uri(
        $"liubai-mfa://capture-response?code=200&x-request-correlation-id={correlationId:D}&file-access-token=");
    Assert(SnippingToolProtocol.TryParseResponse(emptyToken, out var empty),
        "空令牌必须由协议层解析并交给状态机判定失败。");
    Assert(empty.FileAccessToken == string.Empty, "显式空令牌必须与缺失令牌区分。");
}

static void VerifySnippingToolRejectsInvalidResponseEndpoints()
{
    var correlationId = Guid.Parse("77777777-7777-4777-8777-777777777777");
    var query = $"?code=200&x-request-correlation-id={correlationId:D}";
    var invalidUris = new[]
    {
        new Uri("other://capture-response" + query),
        new Uri("liubai-mfa://other" + query),
        new Uri("liubai-mfa://user@capture-response" + query),
        new Uri("liubai-mfa://capture-response:123" + query),
        new Uri("liubai-mfa://capture-response/extra" + query),
        new Uri("liubai-mfa://capture-response" + query + "#fragment"),
    };

    foreach (var uri in invalidUris)
    {
        Assert(!SnippingToolProtocol.TryParseResponse(uri, out var response),
            $"非目标截图响应端点必须拒绝：{uri.GetLeftPart(UriPartial.Path)}");
        Assert(response == default, "拒绝响应时不得返回部分解析结果。");
    }
}

static void VerifySnippingToolRejectsInvalidRequiredParameters()
{
    var correlationId = Guid.Parse("88888888-8888-4888-8888-888888888888");
    var invalidUris = new[]
    {
        new Uri($"liubai-mfa://capture-response?x-request-correlation-id={correlationId:D}"),
        new Uri("liubai-mfa://capture-response?code=200"),
        new Uri($"liubai-mfa://capture-response?code=not-an-integer&x-request-correlation-id={correlationId:D}"),
        new Uri("liubai-mfa://capture-response?code=200&x-request-correlation-id=not-a-guid"),
        new Uri("liubai-mfa://capture-response?code=200&x-request-correlation-id=00000000-0000-0000-0000-000000000000"),
    };

    foreach (var uri in invalidUris)
    {
        Assert(!SnippingToolProtocol.TryParseResponse(uri, out var response),
            "缺失或无效的截图响应必填参数必须拒绝。");
        Assert(response == default, "必填参数无效时不得返回部分解析结果。");
    }
}

static void VerifySnippingToolRejectsDuplicateCriticalParameters()
{
    var correlationId = Guid.Parse("99999999-9999-4999-8999-999999999999");
    var duplicateQueries = new[]
    {
        $"code=200&CODE=499&x-request-correlation-id={correlationId:D}",
        $"code=200&x-request-correlation-id={correlationId:D}&X-REQUEST-CORRELATION-ID={correlationId:D}",
        $"code=200&x-request-correlation-id={correlationId:D}&file-access-token=one&FILE-ACCESS-TOKEN=two",
    };

    foreach (var query in duplicateQueries)
    {
        var uri = new Uri("liubai-mfa://capture-response?" + query);
        Assert(!SnippingToolProtocol.TryParseResponse(uri, out var response),
            "大小写不同的重复关键参数也必须拒绝。");
        Assert(response == default, "关键参数重复时不得返回部分解析结果。");
    }
}

static void VerifySnippingCaptureDefersUntilUnlockOnce()
{
    var state = new SnippingCaptureState();
    var first = state.Begin(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    var deferred = state.Receive(first, 200, "token-a", sessionCurrent: false);
    Assert(deferred.Kind == SnippingCaptureDecisionKind.DeferUntilUnlock, "锁定时必须延期处理。");
    Assert(deferred.FileAccessToken is null, "延期决定不得提前返回文件访问令牌。");
    Assert(state.ResumeAfterUnlock(sessionCurrent: false).Kind == SnippingCaptureDecisionKind.DeferUntilUnlock,
        "会话仍无效时必须继续延期且不得消费令牌。");

    var resumed = state.ResumeAfterUnlock(sessionCurrent: true);
    Assert(resumed.Kind == SnippingCaptureDecisionKind.RedeemNow && resumed.FileAccessToken == "token-a",
        "解锁后必须只返回一次令牌。");
    Assert(state.ResumeAfterUnlock(true).Kind == SnippingCaptureDecisionKind.Ignore, "一次性令牌不得重复返回。");
}

static void VerifySnippingCaptureDefersTerminalStatusesUntilUnlockOnce()
{
    var cases = new[]
    {
        (StatusCode: 400, Token: (string?)"must-not-escape", Expected: SnippingCaptureDecisionKind.BadRequest, Name: "错误请求"),
        (StatusCode: 408, Token: (string?)"must-not-escape", Expected: SnippingCaptureDecisionKind.Timeout, Name: "超时"),
        (StatusCode: 499, Token: (string?)"must-not-escape", Expected: SnippingCaptureDecisionKind.Cancelled, Name: "用户取消"),
        (StatusCode: 500, Token: (string?)"must-not-escape", Expected: SnippingCaptureDecisionKind.Failed, Name: "服务失败"),
        (StatusCode: 200, Token: (string?)null, Expected: SnippingCaptureDecisionKind.Failed, Name: "成功响应缺少令牌"),
    };

    foreach (var testCase in cases)
    {
        var state = new SnippingCaptureState();
        var correlationId = state.Begin(Guid.NewGuid());
        var deferred = state.Receive(correlationId, testCase.StatusCode, testCase.Token, sessionCurrent: false);
        Assert(deferred.Kind == SnippingCaptureDecisionKind.DeferUntilUnlock,
            $"锁定时{testCase.Name}必须先延期到新解锁会话。");
        Assert(state.ResumeAfterUnlock(sessionCurrent: false).Kind == SnippingCaptureDecisionKind.DeferUntilUnlock,
            $"会话仍无效时不得消费{testCase.Name}终态。");
        var resumed = state.ResumeAfterUnlock(sessionCurrent: true);
        Assert(resumed.Kind == testCase.Expected,
            $"解锁后必须恢复原始{testCase.Name}终态。");
        Assert(resumed.FileAccessToken is null,
            $"解锁后恢复{testCase.Name}终态时不得泄露无关文件令牌。");
        Assert(state.ResumeAfterUnlock(sessionCurrent: true).Kind == SnippingCaptureDecisionKind.Ignore,
            $"{testCase.Name}终态只能恢复一次。");
    }
}

static void VerifySnippingCaptureIgnoresMismatchedAndDuplicateResponses()
{
    var state = new SnippingCaptureState();
    var current = state.Begin(Guid.Parse("22222222-2222-2222-2222-222222222222"));
    var stale = Guid.Parse("22222222-2222-2222-2222-222222222223");

    Assert(state.Receive(stale, 200, "stale-token", sessionCurrent: true).Kind == SnippingCaptureDecisionKind.Ignore,
        "关联 ID 不匹配时必须忽略回调。");
    var accepted = state.Receive(current, 200, "current-token", sessionCurrent: true);
    Assert(accepted.Kind == SnippingCaptureDecisionKind.RedeemNow && accepted.FileAccessToken == "current-token",
        "错误关联回调不得清除当前有效请求。");
    Assert(state.Receive(current, 200, "duplicate-token", sessionCurrent: true).Kind == SnippingCaptureDecisionKind.Ignore,
        "匹配响应只允许处理一次。");

    var noRequest = new SnippingCaptureState();
    Assert(noRequest.Receive(current, 200, "unused-token", sessionCurrent: true).Kind == SnippingCaptureDecisionKind.Ignore,
        "没有当前请求时必须忽略响应。");
}

static void VerifySnippingCaptureStatusMappingAndTokenValidation()
{
    var cases = new[]
    {
        (StatusCode: 499, Expected: SnippingCaptureDecisionKind.Cancelled, Name: "用户取消"),
        (StatusCode: 400, Expected: SnippingCaptureDecisionKind.BadRequest, Name: "错误请求"),
        (StatusCode: 408, Expected: SnippingCaptureDecisionKind.Timeout, Name: "超时"),
        (StatusCode: 500, Expected: SnippingCaptureDecisionKind.Failed, Name: "服务失败"),
        (StatusCode: 418, Expected: SnippingCaptureDecisionKind.Failed, Name: "未知状态"),
    };

    foreach (var testCase in cases)
    {
        var state = new SnippingCaptureState();
        var correlationId = Guid.NewGuid();
        state.Begin(correlationId);
        var decision = state.Receive(correlationId, testCase.StatusCode, "must-not-escape", sessionCurrent: true);
        Assert(decision.Kind == testCase.Expected, $"{testCase.Name}状态映射错误。");
        Assert(decision.FileAccessToken is null, $"{testCase.Name}不得返回文件访问令牌。");
        Assert(state.Receive(correlationId, testCase.StatusCode, null, sessionCurrent: true).Kind == SnippingCaptureDecisionKind.Ignore,
            $"{testCase.Name}响应处理后不得再次处理。");
    }

    foreach (var token in new string?[] { null, string.Empty, "   " })
    {
        var state = new SnippingCaptureState();
        var correlationId = Guid.NewGuid();
        state.Begin(correlationId);
        Assert(state.Receive(correlationId, 200, token, sessionCurrent: true).Kind == SnippingCaptureDecisionKind.Failed,
            "200 响应缺失或包含空白令牌时必须失败。");
    }
}

static void VerifySnippingCaptureBeginReplacesPreviousState()
{
    var state = new SnippingCaptureState();
    var first = Guid.Parse("33333333-3333-3333-3333-333333333331");
    var second = Guid.Parse("33333333-3333-3333-3333-333333333332");
    var third = Guid.Parse("33333333-3333-3333-3333-333333333333");

    Assert(state.Begin(first) == first, "Begin 必须返回传入的关联 ID。");
    Assert(state.Begin(second) == second, "新请求必须返回新的关联 ID。");
    Assert(state.Receive(first, 200, "old-token", sessionCurrent: true).Kind == SnippingCaptureDecisionKind.Ignore,
        "新 Begin 后旧请求响应必须视为陈旧回调。");
    Assert(state.Receive(second, 200, "pending-token", sessionCurrent: false).Kind == SnippingCaptureDecisionKind.DeferUntilUnlock,
        "当前请求在锁定时应保存延期令牌。");

    state.Begin(third);
    Assert(state.ResumeAfterUnlock(true).Kind == SnippingCaptureDecisionKind.Ignore,
        "新 Begin 必须清除旧延期令牌。");
    Assert(state.Receive(second, 200, "late-token", sessionCurrent: true).Kind == SnippingCaptureDecisionKind.Ignore,
        "新 Begin 后已完成请求的迟到回调必须忽略。");
    Assert(state.Receive(third, 499, null, sessionCurrent: true).Kind == SnippingCaptureDecisionKind.Cancelled,
        "新 Begin 必须保留新请求本身。");
}

static void VerifySnippingCaptureAbandonClearsState()
{
    var state = new SnippingCaptureState();
    var active = Guid.Parse("44444444-4444-4444-4444-444444444441");
    state.Begin(active);
    state.Abandon();
    Assert(state.Receive(active, 200, "abandoned-token", sessionCurrent: true).Kind == SnippingCaptureDecisionKind.Ignore,
        "Abandon 必须清除当前活动请求。");

    var deferred = Guid.Parse("44444444-4444-4444-4444-444444444442");
    state.Begin(deferred);
    state.Receive(deferred, 200, "deferred-token", sessionCurrent: false);
    state.Abandon();
    Assert(state.ResumeAfterUnlock(true).Kind == SnippingCaptureDecisionKind.Ignore,
        "Abandon 必须清除延期令牌。");
}

static void VerifySnippingCaptureConditionalAbandonPreservesNewerRequest()
{
    var state = new SnippingCaptureState();
    var first = state.Begin(Guid.NewGuid());
    var second = state.Begin(Guid.NewGuid());

    Assert(!state.Abandon(first), "旧截图请求失败不得清除较新的活动请求。");
    var accepted = state.Receive(second, 200, "new-token", sessionCurrent: true);
    Assert(accepted.Kind == SnippingCaptureDecisionKind.RedeemNow
            && accepted.FileAccessToken == "new-token",
        "旧请求条件放弃后，新请求仍必须能兑换自己的令牌。");

    var third = state.Begin(Guid.NewGuid());
    Assert(state.Abandon(third), "当前活动请求必须能被关联感知地放弃。");
    Assert(state.Receive(third, 200, "abandoned-token", sessionCurrent: true).Kind == SnippingCaptureDecisionKind.Ignore,
        "条件放弃当前请求后，迟到回调必须被忽略。");

    var verified = state.Begin(Guid.NewGuid());
    Assert(state.Receive(verified, 200, "verified-token", sessionCurrent: false).Kind
            == SnippingCaptureDecisionKind.DeferUntilUnlock,
        "测试必须先建立已验证的延期回调。");
    Assert(!state.Abandon(verified), "启动结果迟到时不得条件清理已验证的延期回调。");
    var resumed = state.ResumeAfterUnlock(sessionCurrent: true);
    Assert(resumed.Kind == SnippingCaptureDecisionKind.RedeemNow
            && resumed.FileAccessToken == "verified-token",
        "已验证的延期回调必须保留到解锁后一次性兑换。");
}

static void VerifySnippingCaptureRejectsEmptyCorrelationId()
{
    var state = new SnippingCaptureState();
    AssertThrows<ArgumentException>(() => state.Begin(Guid.Empty), "空关联 ID 必须被拒绝。");
    AssertThrows<ArgumentException>(() => state.Abandon(Guid.Empty), "条件放弃也必须拒绝空关联 ID。");
}

static async Task VerifyConcurrentSnippingReceiveRedeemsOnceAsync()
{
    const int workerCount = 8;
    const int trialCount = 1_000;
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

    for (var trial = 0; trial < trialCount; trial++)
    {
        var state = new SnippingCaptureState();
        var correlationId = Guid.NewGuid();
        state.Begin(correlationId);
        using var start = new ManualResetEventSlim();
        var tasks = Enumerable.Range(0, workerCount)
            .Select(_ => Task.Run(() =>
            {
                start.Wait(timeout.Token);
                return state.Receive(correlationId, 200, "concurrent-token", sessionCurrent: true);
            }, timeout.Token))
            .ToArray();

        start.Set();
        var decisions = await Task.WhenAll(tasks).WaitAsync(timeout.Token);
        Assert(decisions.Count(decision => decision.Kind == SnippingCaptureDecisionKind.RedeemNow) == 1,
            "同一截图响应并发到达时只允许一个调用立即兑换。");
        Assert(decisions.Count(decision => decision.Kind == SnippingCaptureDecisionKind.Ignore) == workerCount - 1,
            "同一截图响应的其余并发调用必须忽略。");
    }
}

static async Task VerifyConcurrentSnippingResumeRedeemsOnceAsync()
{
    const int workerCount = 8;
    const int trialCount = 1_000;
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

    for (var trial = 0; trial < trialCount; trial++)
    {
        var state = new SnippingCaptureState();
        var correlationId = Guid.NewGuid();
        state.Begin(correlationId);
        state.Receive(correlationId, 200, "pending-token", sessionCurrent: false);
        using var start = new ManualResetEventSlim();
        var tasks = Enumerable.Range(0, workerCount)
            .Select(_ => Task.Run(() =>
            {
                start.Wait(timeout.Token);
                return state.ResumeAfterUnlock(sessionCurrent: true);
            }, timeout.Token))
            .ToArray();

        start.Set();
        var decisions = await Task.WhenAll(tasks).WaitAsync(timeout.Token);
        Assert(decisions.Count(decision => decision.Kind == SnippingCaptureDecisionKind.RedeemNow) == 1,
            "延期令牌并发恢复时只允许一个调用立即兑换。");
        Assert(decisions.Count(decision => decision.Kind == SnippingCaptureDecisionKind.Ignore) == workerCount - 1,
            "延期令牌的其余并发恢复调用必须忽略。");
    }
}

static async Task VerifyConcurrentSnippingBeginRejectsOldPendingTokenAsync()
{
    const int trialCount = 5_000;
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

    for (var trial = 0; trial < trialCount; trial++)
    {
        var state = new SnippingCaptureState();
        var oldCorrelationId = Guid.NewGuid();
        var newCorrelationId = Guid.NewGuid();
        state.Begin(oldCorrelationId);
        using var start = new ManualResetEventSlim();
        var receive = Task.Run(() =>
        {
            start.Wait(timeout.Token);
            state.Receive(oldCorrelationId, 200, "old-token", sessionCurrent: false);
        }, timeout.Token);
        var begin = Task.Run(() =>
        {
            start.Wait(timeout.Token);
            state.Begin(newCorrelationId);
        }, timeout.Token);

        start.Set();
        await Task.WhenAll(receive, begin).WaitAsync(timeout.Token);
        Assert(state.ResumeAfterUnlock(sessionCurrent: true).Kind == SnippingCaptureDecisionKind.Ignore,
            "新 Begin 与旧响应交错后不得保留旧延期令牌。");
    }
}

static async Task VerifyConcurrentSnippingAbandonRejectsOldPendingTokenAsync()
{
    const int trialCount = 5_000;
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

    for (var trial = 0; trial < trialCount; trial++)
    {
        var state = new SnippingCaptureState();
        var correlationId = Guid.NewGuid();
        state.Begin(correlationId);
        using var start = new ManualResetEventSlim();
        var receive = Task.Run(() =>
        {
            start.Wait(timeout.Token);
            state.Receive(correlationId, 200, "old-token", sessionCurrent: false);
        }, timeout.Token);
        var abandon = Task.Run(() =>
        {
            start.Wait(timeout.Token);
            state.Abandon();
        }, timeout.Token);

        start.Set();
        await Task.WhenAll(receive, abandon).WaitAsync(timeout.Token);
        Assert(state.ResumeAfterUnlock(sessionCurrent: true).Kind == SnippingCaptureDecisionKind.Ignore,
            "Abandon 与旧响应交错后不得保留旧延期令牌。");
    }
}

static void VerifyStartupProtectionDefaultsOff()
{
    var settings = new StartupProtectionSettings(_ => null, (_, _) => { });
    Assert(!settings.IsEnabled, "缺失启动保护设置必须默认关闭。");
}

static void VerifyStartupProtectionReadsStoredValue()
{
    string? trueReadKey = null;
    var enabledSettings = new StartupProtectionSettings(key =>
    {
        trueReadKey = key;
        return true;
    }, (_, _) => { });
    Assert(enabledSettings.IsEnabled, "保存的 true 必须读取为开启。");
    Assert(trueReadKey == "Security.UseWindowsHelloOnOpen", "读取必须使用固定启动保护设置键。");

    string? nonBooleanReadKey = null;
    var nonBooleanSettings = new StartupProtectionSettings(key =>
    {
        nonBooleanReadKey = key;
        return "true";
    }, (_, _) => { });
    Assert(!nonBooleanSettings.IsEnabled, "字符串 true 不得被当作布尔开启。");
    Assert(nonBooleanReadKey == "Security.UseWindowsHelloOnOpen", "读取非布尔值也必须使用固定启动保护设置键。");
}

static void VerifyStartupProtectionWritesRequestedValue()
{
    var writes = new List<(string Key, object Value)>();
    var settings = new StartupProtectionSettings(_ => null, (key, value) => writes.Add((key, value)));

    settings.SetEnabled(true);
    settings.SetEnabled(false);

    Assert(writes.Count == 2, "每次设置启动保护都必须只写入一次。");
    Assert(writes.All(write => write.Key == "Security.UseWindowsHelloOnOpen"), "设置启动保护只能写入固定目标键。");
    Assert(writes[0].Value is bool firstValue && firstValue, "开启启动保护必须写入布尔 true。");
    Assert(writes[1].Value is bool secondValue && !secondValue, "关闭启动保护必须写入布尔 false。");
}

static void VerifyStartupProtectionOpenPolicy()
{
    Assert(!StartupProtectionPolicy.ShouldStartLocked(false), "保护关闭时启动不得阻断。");
    Assert(StartupProtectionPolicy.ShouldStartLocked(true), "保护开启时启动必须阻断。");
    Assert(!StartupProtectionPolicy.ShouldShowManualLock(false), "保护关闭时不显示手动锁定入口。");
    Assert(StartupProtectionPolicy.ShouldShowManualLock(true), "保护开启时显示手动锁定入口。");
}

static void VerifyStartupProtectionTransitionPolicy()
{
    var cases = new[]
    {
        (Current: false, Requested: false, Verified: false, SessionCurrent: false, Allowed: true, Name: "设置未变化"),
        (Current: false, Requested: true, Verified: false, SessionCurrent: true, Allowed: false, Name: "开启时验证取消"),
        (Current: false, Requested: true, Verified: true, SessionCurrent: false, Allowed: false, Name: "开启时会话过期"),
        (Current: false, Requested: true, Verified: true, SessionCurrent: true, Allowed: true, Name: "有效会话验证成功开启"),
        (Current: true, Requested: false, Verified: false, SessionCurrent: true, Allowed: false, Name: "关闭时验证取消"),
        (Current: true, Requested: false, Verified: true, SessionCurrent: false, Allowed: false, Name: "关闭时会话过期"),
        (Current: true, Requested: false, Verified: true, SessionCurrent: true, Allowed: true, Name: "有效会话验证成功关闭"),
    };

    foreach (var testCase in cases)
    {
        var actual = StartupProtectionPolicy.CanCommitSettingChange(
            testCase.Current,
            testCase.Requested,
            testCase.Verified,
            testCase.SessionCurrent);
        Assert(actual == testCase.Allowed, $"{testCase.Name}策略结果错误。");
    }
}

static void VerifyStartupProtectionAutoLockPolicy()
{
    Assert(!StartupProtectionPolicy.ShouldScheduleAutoLock(false, true), "保护关闭时不得安排自动锁定。");
    Assert(!StartupProtectionPolicy.ShouldScheduleAutoLock(true, false), "未解锁时不得安排自动锁定。");
    Assert(StartupProtectionPolicy.ShouldScheduleAutoLock(true, true), "保护开启且已解锁时应安排自动锁定。");
    Assert(!StartupProtectionPolicy.ShouldLockAfterDelay(true, true, TimeSpan.FromSeconds(59), TimeSpan.FromSeconds(60)), "未到延迟时间不得锁定。");
    Assert(StartupProtectionPolicy.ShouldLockAfterDelay(true, true, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60)), "达到延迟时间应允许锁定。");
    Assert(!StartupProtectionPolicy.ShouldLockAfterDelay(false, true, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60)), "保护关闭时不得自动锁定。");
    Assert(!StartupProtectionPolicy.ShouldLockAfterDelay(true, false, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60)), "未解锁时不得自动锁定。");
}

static void VerifyAutoLockStatePreservesDeactivationDuringLoad()
{
    var state = new AutoLockActivationState();
    var startedAt = new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);
    state.RecordDeactivated(true, startedAt);

    var whileLoading = state.Evaluate(true, false, startedAt.AddSeconds(20), TimeSpan.FromMinutes(1));
    Assert(whileLoading.Kind == AutoLockDecisionKind.None, "尚未建立主会话时不得安排自动锁定，但必须保留失活时间。");

    state.RecordDeactivated(true, startedAt.AddSeconds(30));
    var afterLoad = state.Evaluate(true, true, startedAt.AddSeconds(40), TimeSpan.FromMinutes(1));
    Assert(afterLoad.Kind == AutoLockDecisionKind.Schedule, "加载完成且未到时限时必须安排自动锁定。");
    Assert(afterLoad.RemainingDelay == TimeSpan.FromSeconds(20), "必须从首次失活时间计算精确剩余时长，重复失活不得重置起点。");
}

static void VerifyAutoLockStateLocksWhenLoadFinishesAfterDeadline()
{
    var state = new AutoLockActivationState();
    var startedAt = new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);
    state.RecordDeactivated(true, startedAt);

    var afterLoad = state.Evaluate(true, true, startedAt.AddMinutes(1), TimeSpan.FromMinutes(1));
    Assert(afterLoad.Kind == AutoLockDecisionKind.LockNow, "加载完成时已达到失活时限必须立即锁定，不得发布可见会话。");
}

static void VerifyAutoLockStateClearsWhenActivatedDuringLoad()
{
    var state = new AutoLockActivationState();
    var startedAt = new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);
    state.RecordDeactivated(true, startedAt);

    var activation = state.Activate(true, false, startedAt.AddSeconds(20), TimeSpan.FromMinutes(1));
    Assert(activation.Kind == AutoLockDecisionKind.None, "加载期间重新激活且尚无会话时不应锁定。");

    var afterLoad = state.Evaluate(true, true, startedAt.AddMinutes(2), TimeSpan.FromMinutes(1));
    Assert(afterLoad.Kind == AutoLockDecisionKind.None, "重新激活后必须清除失活时间，后续加载完成可正常发布会话。");
}

static void VerifyAutoLockStateActivationBeforeDeadlineReturnsNone()
{
    var state = new AutoLockActivationState();
    var startedAt = new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);
    state.RecordDeactivated(true, startedAt);

    var activation = state.Activate(true, true, startedAt.AddSeconds(20), TimeSpan.FromMinutes(1));
    Assert(activation.Kind == AutoLockDecisionKind.None, "时限内重新激活后窗口保持前台，不得继续安排自动锁定。");

    var later = state.Evaluate(true, true, startedAt.AddMinutes(2), TimeSpan.FromMinutes(1));
    Assert(later.Kind == AutoLockDecisionKind.None, "Activated 作出决策后必须清除失活时间。");
}

static void VerifyAutoLockStateIgnoresDeactivationWhenDisabled()
{
    var state = new AutoLockActivationState();
    var startedAt = new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);
    state.RecordDeactivated(false, startedAt);

    var decision = state.Evaluate(true, true, startedAt.AddMinutes(2), TimeSpan.FromMinutes(1));
    Assert(decision.Kind == AutoLockDecisionKind.None, "启动保护关闭时不得记录可在稍后触发锁定的失活状态。");
}

static void VerifyAutoLockStateCanClearExistingDeactivation()
{
    var state = new AutoLockActivationState();
    var startedAt = new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);
    state.RecordDeactivated(true, startedAt);

    state.Clear();

    var decision = state.Evaluate(true, true, startedAt.AddMinutes(2), TimeSpan.FromMinutes(1));
    Assert(decision.Kind == AutoLockDecisionKind.None, "显式清空后重新开启保护不得沿用旧失活时间。");
}

static void VerifyAutoLockStateClampsClockRollback()
{
    var state = new AutoLockActivationState();
    var startedAt = new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);
    state.RecordDeactivated(true, startedAt);

    var decision = state.Evaluate(true, true, startedAt.AddSeconds(-10), TimeSpan.FromMinutes(1));
    Assert(decision.Kind == AutoLockDecisionKind.Schedule, "系统时钟回拨时仍应安排自动锁定。");
    Assert(decision.RemainingDelay == TimeSpan.FromMinutes(1), "负失活时长必须按零处理，剩余时长不得超过完整延迟。");
}

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
        480,
        620);

    Assert(actual == new PixelRect(1400, 360, 520, 680), "窗口应完整限制在工作区内。");
}

static void VerifyWindowPlacementRepairsDamagedSize()
{
    var actual = WindowPlacementService.Clamp(
        new PixelRect(-200, -300, 200, 300),
        new PixelRect(0, 0, 1920, 1040),
        480,
        620);

    Assert(actual == new PixelRect(0, 0, 480, 620), "损坏的窗口尺寸应恢复到安全最小值。");
}

static void VerifyWindowPlacementPreservesNegativeMonitorCoordinates()
{
    var desired = new PixelRect(-1800, 120, 520, 680);
    var actual = WindowPlacementService.Clamp(
        desired,
        new PixelRect(-1920, 0, 1920, 1040),
        480,
        620);

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
        480,
        620);

    Assert(actual == new PixelRect(-1920, -100, 1280, 720), "超大窗口应缩小到工作区并对齐工作区起点。");
}

static void VerifyWindowPlacementHandlesExtremeValues()
{
    var workArea = new PixelRect(0, 0, 1920, 1040);
    var damaged = WindowPlacementService.Clamp(
        new PixelRect(int.MinValue, int.MaxValue, 0, -1),
        workArea,
        480,
        620);
    var extreme = WindowPlacementService.Clamp(
        new PixelRect(int.MaxValue, int.MinValue, int.MaxValue, int.MaxValue),
        workArea,
        480,
        620);

    Assert(damaged == new PixelRect(0, 420, 480, 620), "非法尺寸和极端坐标应安全限制。");
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

static void VerifySoftCardWindowDimensions()
{
    Assert(WindowPlacementService.DefaultEffectiveWidth == 560, "柔和卡片默认宽度错误。");
    Assert(WindowPlacementService.DefaultEffectiveHeight == 780, "柔和卡片默认高度错误。");
    Assert(WindowPlacementService.MinimumEffectiveWidth == 480, "柔和卡片最小宽度错误。");
    Assert(WindowPlacementService.MinimumEffectiveHeight == 620, "柔和卡片最小高度错误。");
}

static void VerifyWindowPlacementSavePolicy()
{
    Assert(!WindowPlacementService.ShouldSave(false, false, true), "位置和尺寸均未变化时不应保存。");
    Assert(WindowPlacementService.ShouldSave(true, false, true), "普通状态下位置变化应保存。");
    Assert(WindowPlacementService.ShouldSave(false, true, true), "普通状态下尺寸变化应保存。");
    Assert(!WindowPlacementService.ShouldSave(true, false, false), "最小化状态不应保存。");
    Assert(!WindowPlacementService.ShouldSave(false, true, false), "最大化状态不应保存。");
}

static void VerifyClipboardWriteSuccess()
{
    var writeCount = 0;
    string? writtenText = null;
    var service = new ClipboardService(text =>
    {
        writeCount++;
        writtenText = text;
    });

    Assert(service.TryCopyText("123456"), "正常剪贴板写入应返回成功。");
    Assert(writeCount == 1, "一次复制只能调用一次剪贴板写入。");
    Assert(writtenText == "123456", "剪贴板写入内容错误。");
}

static void VerifyClipboardWriteComFailure()
{
    var writeCount = 0;
    var service = new ClipboardService(_ =>
    {
        writeCount++;
        throw new System.Runtime.InteropServices.COMException(
            "模拟剪贴板占用",
            unchecked((int)0x800401D0));
    });

    Assert(!service.TryCopyText("123456"), "剪贴板 COM 失败应返回 false，不能逸出异常。");
    Assert(writeCount == 1, "剪贴板失败后不得后台重试。");
}

static void VerifyClipboardRejectsBlankText()
{
    var service = new ClipboardService(_ => throw new InvalidOperationException("空白文本不应进入写入委托。"));

    AssertThrows<ArgumentException>(
        () => service.TryCopyText("   "),
        "空白验证码必须在进入系统剪贴板前被拒绝。");
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
    Assert(changes.Contains(nameof(AccountItemViewModel.IsExpiring)), "秒数变化未通知验证码临期状态。");
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

static void VerifyAccountExpiryState()
{
    Assert(!AccountItemViewModel.IsExpiringAt(30), "30 秒不应进入临期状态。");
    Assert(!AccountItemViewModel.IsExpiringAt(6), "6 秒不应进入临期状态。");
    Assert(AccountItemViewModel.IsExpiringAt(5), "5 秒应进入临期状态。");
    Assert(AccountItemViewModel.IsExpiringAt(1), "1 秒应进入临期状态。");
    Assert(!AccountItemViewModel.IsExpiringAt(0), "0 秒不应进入临期状态。");

    var item = new AccountItemViewModel(new MfaAccount
    {
        Id = "expiry", Issuer = "Google", Account = "alice", Secret = "JBSWY3DPEHPK3PXP",
    });
    item.Refresh(DateTimeOffset.FromUnixTimeSeconds(24));
    Assert(item.SecondsRemaining == 6 && !item.IsExpiring, "6 秒时临期状态应关闭。");
    var changes = new List<string?>();
    item.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
    item.Refresh(DateTimeOffset.FromUnixTimeSeconds(25));
    Assert(item.SecondsRemaining == 5 && item.IsExpiring, "跨越 6 到 5 秒应进入临期状态。");
    Assert(changes.Contains(nameof(AccountItemViewModel.IsExpiring)), "跨越临界值时必须通知临期状态。");
}

static void VerifyExpiryAlertOverlayState()
{
    var item = new AccountItemViewModel(new MfaAccount
    {
        Id = "expiry-overlay", Issuer = "GitHub", Account = "qa", Secret = "JBSWY3DPEHPK3PXP",
    });
    item.Refresh(DateTimeOffset.FromUnixTimeSeconds(24));
    Assert(item.ExpiryAlertOpacity == 0, "6 秒时临期警示覆盖层必须完全透明。");

    var changes = new List<string?>();
    item.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
    item.Refresh(DateTimeOffset.FromUnixTimeSeconds(25));
    Assert(item.ExpiryAlertOpacity == 1, "5 秒时临期警示覆盖层必须完全显示。");
    Assert(changes.Contains(nameof(AccountItemViewModel.ExpiryAlertOpacity)),
        "跨越临界值时必须通知临期警示覆盖层透明度。");

    item.Refresh(DateTimeOffset.FromUnixTimeSeconds(29));
    Assert(item.SecondsRemaining == 1 && item.ExpiryAlertOpacity == 1,
        "1 秒时临期警示覆盖层必须保持显示。");
    changes.Clear();
    item.Refresh(DateTimeOffset.FromUnixTimeSeconds(30));
    Assert(item.SecondsRemaining == 30 && item.ExpiryAlertOpacity == 0,
        "新周期开始时临期警示覆盖层必须恢复透明。");
    Assert(changes.Contains(nameof(AccountItemViewModel.ExpiryAlertOpacity)),
        "跨越周期边界时必须通知临期警示覆盖层透明度。");
}

static void VerifyExpiryProgressUsesDirectAlertOverlay()
{
    var sourcePath = FindWorkspaceSourceFile(Path.Combine("LiubaiMfa.Windows", "Views", "AuthenticatorView.xaml"));
    var source = File.ReadAllText(sourcePath);
    Assert(!source.Contains("<StateTrigger", StringComparison.Ordinal),
        "账户临期颜色不得继续依赖模板内无法可靠解析名称域的 StateTrigger。");
    Assert(!source.Contains("Tag=\"{x:Bind IsExpiring", StringComparison.Ordinal),
        "账户临期颜色不得通过 Tag 和二次 Binding 中转。");

    var baseName = source.IndexOf("x:Name=\"ExpiryProgress\"", StringComparison.Ordinal);
    var alertName = source.IndexOf("x:Name=\"ExpiryAlertProgress\"", StringComparison.Ordinal);
    Assert(baseName >= 0 && alertName > baseName, "临期警示进度条必须覆盖在普通进度条之上。");

    var alertStart = source.LastIndexOf("<ProgressBar", alertName, StringComparison.Ordinal);
    var alertEnd = source.IndexOf("/>", alertName, StringComparison.Ordinal);
    Assert(alertStart >= 0 && alertEnd > alertName, "无法定位临期警示进度条。");
    var alertProgress = source[alertStart..(alertEnd + 2)];
    Assert(alertProgress.Contains("Value=\"{x:Bind ProgressValue, Mode=OneWay}\"", StringComparison.Ordinal),
        "临期警示进度条必须与普通进度条使用相同进度值。");
    Assert(alertProgress.Contains("Opacity=\"{x:Bind ExpiryAlertOpacity, Mode=OneWay}\"", StringComparison.Ordinal),
        "临期警示进度条必须直接绑定警示透明度。");
    Assert(alertProgress.Contains("Foreground=\"{ThemeResource SystemFillColorCriticalBrush}\"", StringComparison.Ordinal),
        "临期警示进度条必须使用主题感知的 CriticalBrush。");
    Assert(alertProgress.Contains("IsHitTestVisible=\"False\"", StringComparison.Ordinal),
        "临期警示覆盖层不得拦截账户卡片输入。");
    Assert(alertProgress.Contains("AutomationProperties.AccessibilityView=\"Raw\"", StringComparison.Ordinal),
        "临期警示覆盖层不得在无障碍树中重复暴露进度语义。");
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

static async Task VerifyDuplicateMfaSecretExceptionClassificationAsync()
{
    var duplicateViewModel = new MainViewModel(new FakeAccountRepository(CreateViewModelFixture()));
    await duplicateViewModel.LoadAsync();
    var duplicate = await CaptureThrowsAsync<DuplicateMfaSecretException>(
        () => duplicateViewModel.AddAsync(CreateNewAccount() with { Secret = "A" }));
    Assert(duplicate.Message == "该 MFA 密钥已经存在。", "专用重复异常必须保留克制的用户可见原因。");

    var loadFailureViewModel = new MainViewModel(
        new FakeAccountRepository(loadException: new IOException("模拟读取失败")));
    await AssertThrowsAsync<IOException>(() => loadFailureViewModel.LoadAsync());
    var writeProtection = await CaptureThrowsAsync<InvalidOperationException>(
        () => loadFailureViewModel.AddAsync(CreateNewAccount()));
    Assert(writeProtection is not DuplicateMfaSecretException,
        "加载失败写保护不得伪装成重复密钥冲突。");
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

static void VerifyQueuedGroupSynchronizationIsolatedByAttachEpoch()
{
    var sourcePath = FindWorkspaceSourceFile(Path.Combine("LiubaiMfa.Windows", "Views", "AuthenticatorView.xaml.cs"));
    var source = File.ReadAllText(sourcePath);
    var methodStart = source.IndexOf("    private void Groups_CollectionChanged", StringComparison.Ordinal);
    var methodEnd = source.IndexOf("    private void SynchronizeGroupSelection", methodStart, StringComparison.Ordinal);
    Assert(methodStart >= 0 && methodEnd > methodStart, "无法定位分组集合变更方法。");

    var method = source[methodStart..methodEnd];
    var viewModelCapture = System.Text.RegularExpressions.Regex.Match(
        method,
        @"var\s+attachedViewModel\s*=\s*_viewModel\s*;");
    var attachEpochCapture = System.Text.RegularExpressions.Regex.Match(
        method,
        @"var\s+attachEpoch\s*=\s*_attachEpoch\s*;");
    var enqueue = System.Text.RegularExpressions.Regex.Match(
        method,
        @"var\s+(?<result>\w+)\s*=\s*DispatcherQueue\.TryEnqueue\s*\(\s*\(\s*\)\s*=>\s*\{(?<callback>[\s\S]*?)\}\s*\)\s*;");

    Assert(viewModelCapture.Success && attachEpochCapture.Success, "分组同步入队前必须同时捕获视图模型和 attach epoch。");
    Assert(enqueue.Success && viewModelCapture.Index < enqueue.Index && attachEpochCapture.Index < enqueue.Index,
        "分组同步必须在捕获当前挂载身份后再入队。");

    var callback = enqueue.Groups["callback"].Value;
    Assert(System.Text.RegularExpressions.Regex.IsMatch(
            callback,
            @"if\s*\(\s*ReferenceEquals\s*\(\s*_viewModel\s*,\s*attachedViewModel\s*\)\s*&&\s*_attachEpoch\s*==\s*attachEpoch\s*\)\s*\{\s*SynchronizeGroupSelection\s*\(\s*\)\s*;"),
        "排队的分组同步必须在执行前同时校验视图模型引用和 attach epoch。");

    var enqueueResult = System.Text.RegularExpressions.Regex.Escape(enqueue.Groups["result"].Value);
    Assert(System.Text.RegularExpressions.Regex.IsMatch(
            method[enqueue.Index..],
            $@"if\s*\(\s*!\s*{enqueueResult}\s*\)\s*\{{\s*_isSynchronizingGroups\s*=\s*false\s*;"),
        "分组同步入队失败时必须恢复同步标志。");
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

static void VerifyClipboardResultBoundaryInMainWindow()
{
    var sourcePath = FindWorkspaceSourceFile(Path.Combine("LiubaiMfa.Windows", "MainWindow.xaml.cs"));
    var source = File.ReadAllText(sourcePath);

    Assert(source.Contains("ClipboardResultPolicy.CreateStatus(_clipboard.TryCopyText(account.Code), \"验证码已复制。\")", StringComparison.Ordinal),
        "主窗口必须将剪贴板结果交给提示策略。");
    Assert(!source.Contains("Clipboard.SetContent", StringComparison.Ordinal),
        "主窗口不得继续直接调用系统剪贴板。");
    Assert(!source.Contains("private static void CopyText", StringComparison.Ordinal),
        "主窗口不得保留无错误边界的旧复制方法。");
}

static void VerifySensitiveClipboardResultHandling()
{
    var mainWindowPath = FindWorkspaceSourceFile(Path.Combine("LiubaiMfa.Windows", "MainWindow.xaml.cs"));
    var mainWindow = File.ReadAllText(mainWindowPath);
    Assert(mainWindow.Contains("ClipboardResultPolicy.CreateStatus(_clipboard.TryCopyText(account.Code), \"验证码已复制。\")", StringComparison.Ordinal),
        "验证码复制入口必须调用剪贴板提示策略。");

    var accountDialogPath = FindWorkspaceSourceFile(Path.Combine("LiubaiMfa.Windows", "Services", "AccountDialogService.cs"));
    var accountDialog = File.ReadAllText(accountDialogPath);
    Assert(accountDialog.Contains("ClipboardResultPolicy.CreateStatus(copyText(current.Secret), \"MFA 密钥已复制。\")", StringComparison.Ordinal),
        "MFA 密钥复制入口必须调用剪贴板提示策略。");

    var backupDialogPath = FindWorkspaceSourceFile(Path.Combine("LiubaiMfa.Windows", "Services", "BackupDialogService.cs"));
    var backupDialog = File.ReadAllText(backupDialogPath);
    Assert(backupDialog.Contains("ClipboardResultPolicy.CreateRecoveryKey(copyText(recoveryKey))", StringComparison.Ordinal),
        "恢复密钥复制入口必须调用剪贴板提示策略。");
}

static void VerifyAccountDraftSecretInputIsMaskedAndCleared()
{
    var sourcePath = FindWorkspaceSourceFile(Path.Combine("LiubaiMfa.Windows", "Services", "AccountDialogService.cs"));
    var source = File.ReadAllText(sourcePath);
    var methodStart = source.IndexOf("    private async Task<AccountDraft?> ShowDraftAsync", StringComparison.Ordinal);
    var methodEnd = source.IndexOf("    private static AccountDraft? CreateDraft", methodStart, StringComparison.Ordinal);
    Assert(methodStart >= 0 && methodEnd > methodStart, "无法定位账户草稿对话框方法。");

    var method = source[methodStart..methodEnd];
    Assert(System.Text.RegularExpressions.Regex.IsMatch(method, @"var\s+secretBox\s*=\s*new\s+PasswordBox\s*\{"),
        "账户草稿密钥必须使用 PasswordBox，默认由系统遮挡。");
    Assert(!System.Text.RegularExpressions.Regex.IsMatch(method, @"var\s+secretBox\s*=\s*new\s+TextBox\s*\{"),
        "账户草稿密钥不得继续使用明文 TextBox。");
    Assert(System.Text.RegularExpressions.Regex.IsMatch(method, @"PasswordRevealMode\s*=\s*PasswordRevealMode\.Peek"),
        "密钥输入只允许通过 Peek 临时查看。");
    Assert(System.Text.RegularExpressions.Regex.IsMatch(method, @"CreateDraft\s*\(\s*issuerBox\.Text\s*,\s*accountBox\.Text\s*,\s*secretBox\.Password\s*,"),
        "账户草稿必须从 Password 属性读取密钥。");

    var tryIndex = method.IndexOf("try", StringComparison.Ordinal);
    var showIndex = method.IndexOf("await _showDialog(dialog, token)", StringComparison.Ordinal);
    var finallyIndex = method.IndexOf("finally", showIndex, StringComparison.Ordinal);
    var clearIndex = method.IndexOf("secretBox.Password = string.Empty;", finallyIndex, StringComparison.Ordinal);
    Assert(tryIndex >= 0 && showIndex > tryIndex && finallyIndex > showIndex && clearIndex > finallyIndex,
        "对话框结束或异常时必须在 finally 中无条件清空密钥输入。");
}

static void VerifyClipboardStatusFeedbackPolicy()
{
    var failure = ClipboardResultPolicy.CreateStatus(false, "验证码已复制。");
    Assert(failure.Message == "剪贴板暂时不可用，请稍后重试。", "失败提示错误。");
    Assert(failure.Severity == Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning, "失败必须使用 Warning。");
    var success = ClipboardResultPolicy.CreateStatus(true, "验证码已复制。");
    Assert(success.Message == "验证码已复制。", "成功提示错误。");
    Assert(success.Severity == Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success, "成功必须使用 Success。");
}

static void VerifyRecoveryKeyClipboardFeedbackPolicy()
{
    var failure = ClipboardResultPolicy.CreateRecoveryKey(false);
    Assert(failure.ButtonText == "复制失败，请重试", "恢复密钥失败按钮错误。");
    Assert(!failure.CanContinue, "恢复密钥失败不得允许继续。");
    var success = ClipboardResultPolicy.CreateRecoveryKey(true);
    Assert(success.ButtonText == "恢复密钥已复制", "恢复密钥成功按钮错误。");
    Assert(success.CanContinue, "恢复密钥成功后应允许继续。");
}

static void VerifyUnifiedAddAccountEntrySourceContract()
{
    var authenticatorXamlPath = FindWorkspaceSourceFile(Path.Combine("LiubaiMfa.Windows", "Views", "AuthenticatorView.xaml"));
    var authenticatorCodePath = FindWorkspaceSourceFile(Path.Combine("LiubaiMfa.Windows", "Views", "AuthenticatorView.xaml.cs"));
    var mainWindowCodePath = FindWorkspaceSourceFile(Path.Combine("LiubaiMfa.Windows", "MainWindow.xaml.cs"));
    var addAccountXamlPath = FindWorkspaceSourceFile(Path.Combine("LiubaiMfa.Windows", "Views", "AddAccountView.xaml"));
    var addAccountCodePath = FindWorkspaceSourceFile(Path.Combine("LiubaiMfa.Windows", "Views", "AddAccountView.xaml.cs"));
    var authenticatorXaml = File.ReadAllText(authenticatorXamlPath);
    var authenticatorCode = File.ReadAllText(authenticatorCodePath);
    var mainWindowCode = File.ReadAllText(mainWindowCodePath);
    var addAccountXaml = File.ReadAllText(addAccountXamlPath);
    var addAccountCode = File.ReadAllText(addAccountCodePath);
    var presentation = (System.Xml.Linq.XNamespace)"http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    var authenticatorDocument = System.Xml.Linq.XDocument.Parse(authenticatorXaml);
    var addAccountDocument = System.Xml.Linq.XDocument.Parse(addAccountXaml);
    var authenticatorRoot = ParseCSharpRoot(authenticatorCode, "AuthenticatorView.xaml.cs");
    var addAccountRoot = ParseCSharpRoot(addAccountCode, "AddAccountView.xaml.cs");
    var mainWindowRoot = ParseCSharpRoot(mainWindowCode, "MainWindow.xaml.cs");
    var authenticatorClass = FindUniqueClass(authenticatorRoot, "AuthenticatorView");
    var addAccountClass = FindUniqueClass(addAccountRoot, "AddAccountView");
    AssertNoConditionalCompilation(authenticatorClass);
    AssertNoConditionalCompilation(addAccountClass);

    Assert(!authenticatorDocument.Descendants(presentation + "Button")
            .Any(element => GetRuntimeXamlAttribute(element, "Click") == "ImportQrButton_Click"),
        "验证码首页不得保留独立二维码导入按钮。");
    Assert(!authenticatorClass.Members.OfType<MethodDeclarationSyntax>()
            .Any(method => method.Identifier.ValueText == "ImportQrButton_Click"),
        "验证码首页不得保留独立二维码导入处理器。");
    Assert(!GetEventNames(authenticatorClass).Any(eventName => eventName == "ImportQrRequested"),
        "验证码首页不得保留独立二维码导入事件。");
    Assert(!mainWindowRoot.DescendantTokens()
            .Any(token => token.IsKind(SyntaxKind.IdentifierToken) && token.ValueText == "ImportQrRequested"),
        "主窗口不得继续订阅已移除的二维码导入事件。");
    AssertPublicNullableEvent(authenticatorClass, "AddRequested");

    var authenticatorAddButtons = authenticatorDocument
        .Descendants(presentation + "Button")
        .Where(element => GetRuntimeXamlAttribute(element, "AutomationProperties.Name") == "添加账户")
        .ToArray();
    var topAddButtons = authenticatorAddButtons
        .Where(element => GetRuntimeXamlAttribute(element, "ToolTipService.ToolTip") == "添加账户"
            && element.Descendants(presentation + "FontIcon").Any())
        .ToArray();
    var emptyAddButtons = authenticatorAddButtons
        .Where(element => GetRuntimeXamlAttribute(element, "Content") == "添加账户")
        .ToArray();
    Assert(topAddButtons.Length == 1, "验证码首页顶部必须有且只有一个添加账户按钮。");
    Assert(emptyAddButtons.Length == 1, "验证码首页空状态必须有且只有一个添加账户按钮。");
    Assert(GetRuntimeXamlAttribute(topAddButtons[0], "Click") == "AddButton_Click",
        "验证码首页顶部添加按钮必须绑定 AddButton_Click。");
    Assert(GetRuntimeXamlAttribute(emptyAddButtons[0], "Click") == "EmptyAddButton_Click",
        "验证码首页空状态添加按钮必须绑定 EmptyAddButton_Click。");
    AssertHandlerOnlyForwards(authenticatorClass, "AddButton_Click", "AddRequested");
    AssertHandlerOnlyForwards(authenticatorClass, "EmptyAddButton_Click", "AddRequested");

    foreach (var intent in new[] { "BackRequested", "ScreenshotScanRequested", "ImportImageRequested", "ManualEntryRequested" })
    {
        AssertPublicNullableEvent(addAccountClass, intent);
    }

    var addAccountButtons = addAccountDocument.Descendants(presentation + "Button").ToArray();
    AssertButtonClickBinding(addAccountButtons, "返回验证码列表", "BackButton_Click");
    AssertButtonClickBinding(addAccountButtons, "截图并扫描二维码", "ScreenshotScanButton_Click");
    AssertButtonClickBinding(addAccountButtons, "从图片导入", "ImportImageButton_Click");
    AssertButtonClickBinding(addAccountButtons, "手动输入", "ManualEntryButton_Click");
    AssertHandlerOnlyForwards(addAccountClass, "BackButton_Click", "BackRequested");
    AssertHandlerOnlyForwards(addAccountClass, "ScreenshotScanButton_Click", "ScreenshotScanRequested");
    AssertHandlerOnlyForwards(addAccountClass, "ImportImageButton_Click", "ImportImageRequested");
    AssertHandlerOnlyForwards(addAccountClass, "ManualEntryButton_Click", "ManualEntryRequested");

    foreach (var text in new[]
    {
        "添加账户", "选择一种添加方式",
        "截图并扫描二维码", "框选屏幕上的二维码，识别后可确认账户信息",
        "其他方式", "从图片导入", "选择 PNG、JPG 或 BMP 图片",
        "手动输入", "输入服务、账户和密钥",
    })
    {
        Assert(addAccountXaml.Contains(text, StringComparison.Ordinal), $"添加账户页缺少文案：{text}");
    }
}

static void VerifyMainWindowAddAccountFlowSourceContract()
{
    var inactiveFakeRoot = ParseCSharpRoot(
        """
        class FakeWindow
        {
            FakeWindow()
            {
                // ExtendsContentIntoTitleBar = true;
                _ = "ExtendsContentIntoTitleBar = true";
        #if CONTRACT_DISABLED
                ExtendsContentIntoTitleBar = true;
        #endif
            }
        }
        """,
        "InactiveMainWindowContractFake.cs");
    var inactiveFakeConstructor = FindUniqueClass(inactiveFakeRoot, "FakeWindow")
        .Members.OfType<ConstructorDeclarationSyntax>().Single();
    Assert(!HasBooleanTrueAssignment(inactiveFakeConstructor, "ExtendsContentIntoTitleBar"),
        "活动 AST 契约不得把注释、字符串或 disabled #if 中的伪实现当成标题栏实现。");

    var mainWindowXamlPath = FindWorkspaceSourceFile(Path.Combine("LiubaiMfa.Windows", "MainWindow.xaml"));
    var mainWindowCodePath = FindWorkspaceSourceFile(Path.Combine("LiubaiMfa.Windows", "MainWindow.xaml.cs"));
    var authenticatorXamlPath = FindWorkspaceSourceFile(Path.Combine("LiubaiMfa.Windows", "Views", "AuthenticatorView.xaml"));
    var authenticatorCodePath = FindWorkspaceSourceFile(Path.Combine("LiubaiMfa.Windows", "Views", "AuthenticatorView.xaml.cs"));
    var addAccountXamlPath = FindWorkspaceSourceFile(Path.Combine("LiubaiMfa.Windows", "Views", "AddAccountView.xaml"));
    var addAccountCodePath = FindWorkspaceSourceFile(Path.Combine("LiubaiMfa.Windows", "Views", "AddAccountView.xaml.cs"));
    var settingsXamlPath = FindWorkspaceSourceFile(Path.Combine("LiubaiMfa.Windows", "Views", "SettingsView.xaml"));
    var settingsCodePath = FindWorkspaceSourceFile(Path.Combine("LiubaiMfa.Windows", "Views", "SettingsView.xaml.cs"));
    var mainWindowDocument = XDocument.Parse(File.ReadAllText(mainWindowXamlPath));
    var authenticatorDocument = XDocument.Parse(File.ReadAllText(authenticatorXamlPath));
    var addAccountDocument = XDocument.Parse(File.ReadAllText(addAccountXamlPath));
    var settingsDocument = XDocument.Parse(File.ReadAllText(settingsXamlPath));
    var mainWindowSource = File.ReadAllText(mainWindowCodePath);
    var authenticatorSource = File.ReadAllText(authenticatorCodePath);
    var addAccountSource = File.ReadAllText(addAccountCodePath);
    var settingsSource = File.ReadAllText(settingsCodePath);
    var mainWindowRoot = ParseCSharpRoot(mainWindowSource, "MainWindow.xaml.cs");
    var authenticatorRoot = ParseCSharpRoot(authenticatorSource, "AuthenticatorView.xaml.cs");
    var mainWindowClass = FindUniqueClass(mainWindowRoot, "MainWindow");
    var authenticatorClass = FindUniqueClass(authenticatorRoot, "AuthenticatorView");
    var presentation = (XNamespace)"http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    var xaml = (XNamespace)"http://schemas.microsoft.com/winfx/2006/xaml";

    var titleBarRegions = mainWindowDocument
        .Descendants(presentation + "Grid")
        .Where(element => element.Attribute(xaml + "Name")?.Value == "TitleBarDragRegion")
        .ToArray();
    Assert(titleBarRegions.Length == 1, "主窗口必须有且只有一个 TitleBarDragRegion 拖动区。");
    Assert(GetRuntimeXamlAttribute(titleBarRegions.Single(), "Height") == "32",
        "标题栏拖动区必须保持 32px 高度。");
    Assert(!mainWindowDocument.Descendants()
            .Any(element => GetRuntimeXamlAttribute(element, "Text") == "留白 MFA"),
        "主窗口不得在自定义标题栏或页面中渲染应用名称。");
    Assert(mainWindowDocument.Descendants()
            .Count(element => element.Name.LocalName == "AddAccountView"
                && element.Attribute(xaml + "Name")?.Value == "AddAccountView") == 1,
        "主窗口必须叠放唯一 AddAccountView。");
    Assert(authenticatorDocument.Descendants(presentation + "Button")
            .Count(element => element.Attribute(xaml + "Name")?.Value == "AddButton") == 1,
        "验证码列表顶部添加按钮必须可被返回导航恢复焦点。");
    Assert(authenticatorDocument.Descendants(presentation + "TextBlock")
            .Count(element => GetRuntimeXamlAttribute(element, "Text") == "留白 MFA") == 1,
        "主页面标题必须使用“留白 MFA”。");
    var settingsButtons = authenticatorDocument.Descendants(presentation + "Button")
        .Where(element => element.Attribute(xaml + "Name")?.Value == "SettingsButton")
        .ToArray();
    Assert(settingsButtons.Length == 1
            && GetRuntimeXamlAttribute(settingsButtons[0], "Click") == "SettingsButton_Click"
            && GetRuntimeXamlAttribute(settingsButtons[0], "Width") is null
            && GetRuntimeXamlAttribute(settingsButtons[0], "Height") is null
            && settingsButtons[0].Descendants(presentation + "FontIcon")
                .Any(icon => GetRuntimeXamlAttribute(icon, "Glyph") == ""),
        "顶部设置入口必须是 44px 规范齿轮图标按钮并进入独立设置页。");
    Assert(!authenticatorDocument.Descendants().Any(element => element.Name.LocalName is "MenuFlyout" or "MenuFlyoutItem")
            && !authenticatorSource.Contains("CreateSettingsFlyout", StringComparison.Ordinal),
        "主页面不得继续承载设置菜单浮层。");
    Assert(mainWindowDocument.Descendants()
            .Count(element => element.Name.LocalName == "SettingsView"
                && element.Attribute(xaml + "Name")?.Value == "SettingsView") == 1,
        "主窗口必须叠放唯一 SettingsView。");
    Assert(settingsDocument.Descendants(presentation + "Button")
            .Any(element => GetRuntimeXamlAttribute(element, "Click") == "BackButton_Click")
            && settingsDocument.Descendants(presentation + "Button")
                .Any(element => GetRuntimeXamlAttribute(element, "Click") == "LockButton_Click")
            && settingsDocument.Descendants(presentation + "Button")
                .Any(element => GetRuntimeXamlAttribute(element, "Click") == "StartupProtectionButton_Click")
            && settingsDocument.Descendants(presentation + "Button")
                .Any(element => GetRuntimeXamlAttribute(element, "Click") == "ImportBackupButton_Click")
            && settingsDocument.Descendants(presentation + "Button")
                .Any(element => GetRuntimeXamlAttribute(element, "Click") == "ExportBackupButton_Click")
            && settingsSource.Contains("BackRequested", StringComparison.Ordinal)
            && settingsSource.Contains("StartupProtectionRequested", StringComparison.Ordinal),
        "设置页必须提供返回、安全、启动保护和备份操作边界。");

    var addStatusBars = addAccountDocument.Descendants(presentation + "InfoBar")
        .Where(element => element.Attribute(xaml + "Name")?.Value == "StatusBar")
        .ToArray();
    Assert(addStatusBars.Length == 1, "添加账户页必须提供可访问的 InfoBar 状态区。");
    Assert(addAccountSource.Contains("public void ShowStatus(", StringComparison.Ordinal),
        "添加账户页必须公开最小 ShowStatus 边界，且不得自行访问服务。");

    var constructor = mainWindowClass.Members.OfType<ConstructorDeclarationSyntax>().Single();
    Assert(HasBooleanTrueAssignment(constructor, "ExtendsContentIntoTitleBar"),
        "主窗口必须启用扩展自定义标题栏。");
    Assert(HasInvocation(constructor, null, "SetTitleBar", "TitleBarDragRegion"),
        "主窗口必须将空白拖动区注册为标题栏。");
    Assert(HasSimpleAssignment(constructor, "AppWindow.TitleBar.ButtonBackgroundColor", "Colors.Transparent")
            && HasSimpleAssignment(constructor, "AppWindow.TitleBar.ButtonInactiveBackgroundColor", "Colors.Transparent"),
        "系统标题按钮必须使用透明且主题感知的背景。");
    foreach (var subscription in new[]
    {
        (Event: "AuthenticatorView.AddRequested", Handler: "ShowAddAccountView"),
        (Event: "AuthenticatorView.SettingsRequested", Handler: "ShowSettingsView"),
        (Event: "AddAccountView.BackRequested", Handler: "ShowAuthenticatorView"),
        (Event: "AddAccountView.ScreenshotScanRequested", Handler: "ScreenshotScan_Click"),
        (Event: "AddAccountView.ImportImageRequested", Handler: "ImportImage_Click"),
        (Event: "AddAccountView.ManualEntryRequested", Handler: "ManualEntry_Click"),
        (Event: "SettingsView.BackRequested", Handler: "ShowAuthenticatorView"),
        (Event: "SettingsView.LockRequested", Handler: "Lock_Click"),
        (Event: "SettingsView.StartupProtectionRequested", Handler: "Settings_Click"),
        (Event: "SettingsView.ImportBackupRequested", Handler: "ImportBackup_Click"),
        (Event: "SettingsView.ExportBackupRequested", Handler: "ExportBackup_Click"),
        (Event: "Closed", Handler: "MainWindow_Closed"),
    })
    {
        Assert(HasEventSubscription(constructor, subscription.Event, subscription.Handler),
            $"主窗口缺少活动事件接线：{subscription.Event} += {subscription.Handler}");
    }

    var protocolHandler = FindUniqueMethod(mainWindowClass, "HandleProtocolActivationAsync");
    Assert(HasInvocation(protocolHandler, "SnippingToolProtocol", "TryParseResponse")
            && HasInvocation(protocolHandler, "_snippingCaptureState", "Receive")
            && HasInvocation(protocolHandler, null, "ProcessSnippingCaptureDecisionAsync"),
        "协议回调必须严格解析、进入截图状态机并统一处理决策。");
    var enterMainView = FindUniqueMethod(mainWindowClass, "EnterMainViewAsync");
    Assert(HasInvocation(enterMainView, "_snippingCaptureState", "ResumeAfterUnlock")
            && enterMainView.DescendantNodes().OfType<IfStatementSyntax>().Any(statement =>
                statement.Condition is BinaryExpressionSyntax binary
                && binary.IsKind(SyntaxKind.NotEqualsExpression)
                && binary.Left.ToString() == "pendingCapture.Kind"
                && binary.Right.ToString() == "SnippingCaptureDecisionKind.Ignore"
                && HasInvocation(statement.Statement, null, "ShowAddAccountViewImmediately"))
            && HasInvocation(enterMainView, null, "ProcessSnippingCaptureDecisionAsync"),
        "新解锁会话建立后必须先恢复添加页，再只消费一次延期截图终态。");
    var forceLock = FindUniqueMethod(mainWindowClass, "ForceLock");
    Assert(!HasInvocation(forceLock, "_snippingCaptureState", "Abandon"),
        "自动锁定不得丢弃已验证的延期截图令牌。");
    var closedHandler = FindUniqueMethod(mainWindowClass, "MainWindow_Closed");
    AssertClosedHandlerContract(closedHandler);
    Assert(HasInvocation(mainWindowClass, "_sessionCancellation", "TryAcquire")
            && HasInvocation(mainWindowClass, "_qrImportCoordinator", "ImportAsync"),
        "截图和图片导入必须使用会话租约进入统一二维码协调器。");
    var saveQrAccount = FindUniqueMethod(mainWindowClass, "SaveQrAccountAsync");
    var caughtTypes = saveQrAccount.DescendantNodes()
        .OfType<CatchDeclarationSyntax>()
        .Select(declaration => declaration.Type?.ToString())
        .Where(type => type is not null)
        .ToArray();
    Assert(caughtTypes.SequenceEqual(["DuplicateMfaSecretException"]),
        "二维码保存适配器只能将专用 DuplicateMfaSecretException 分类为冲突。");
    var screenshotHandler = FindUniqueMethod(mainWindowClass, "ScreenshotScan_Click");
    Assert(screenshotHandler.DescendantNodes().OfType<IfStatementSyntax>().Any(statement =>
                statement.Condition is PrefixUnaryExpressionSyntax prefix
                && prefix.IsKind(SyntaxKind.LogicalNotExpression)
                && prefix.Operand is IdentifierNameSyntax { Identifier.ValueText: "launched" })
            && CountInvocations(screenshotHandler, "_snippingCaptureState", "Abandon", "correlationId") == 2,
        "截图工具返回 false 或异常时必须按关联 ID 条件放弃请求。");
    var unlockMethod = FindUniqueMethod(mainWindowClass, "UnlockAsync");
    var cancelledVerificationBranches = unlockMethod.DescendantNodes()
        .OfType<IfStatementSyntax>()
        .Where(statement => statement.Condition is PrefixUnaryExpressionSyntax
            {
                OperatorToken.RawKind: (int)SyntaxKind.ExclamationToken,
                Operand: IdentifierNameSyntax { Identifier.ValueText: "isVerified" },
            })
        .ToArray();
    Assert(cancelledVerificationBranches.Length == 1
            && HasInvocation(cancelledVerificationBranches[0].Statement, "_snippingCaptureState", "Abandon"),
        "用户取消 Windows Hello 解锁时必须永久放弃延期截图终态。");
    var processCapture = FindUniqueMethod(mainWindowClass, "ProcessSnippingCaptureDecisionAsync");
    var launchMessages = GetActiveStringLiterals(screenshotHandler);
    var captureDecisionSwitch = processCapture.DescendantNodes().OfType<SwitchStatementSyntax>()
        .Single(statement => statement.Expression.ToString() == "decision.Kind");
    var failedDecisionSections = captureDecisionSwitch.Sections
        .Where(section => section.Labels.OfType<CaseSwitchLabelSyntax>().Any(label =>
            label.Value.ToString() == "SnippingCaptureDecisionKind.Failed"))
        .ToArray();
    Assert(failedDecisionSections.Length == 1,
        "截图决策必须保留唯一的 Failed 分支。");
    var failedDecisionMessages = GetActiveStringLiterals(failedDecisionSections[0]);
    var coordinatorTries = processCapture.DescendantNodes().OfType<TryStatementSyntax>()
        .Where(statement => HasInvocation(statement.Block, "_qrImportCoordinator", "ImportAsync"))
        .ToArray();
    Assert(coordinatorTries.Length == 1,
        "截图兑换成功后必须通过唯一的二维码导入协调器 try/catch 处理。");
    var coordinatorGeneralCatches = coordinatorTries[0].Catches
        .Where(catchClause => catchClause.Filter is null
            && catchClause.Declaration?.Type.ToString() == "Exception")
        .ToArray();
    Assert(coordinatorGeneralCatches.Length == 1,
        "二维码导入协调器必须保留唯一通用异常边界。");
    var coordinatorFailureMessages = GetActiveStringLiterals(coordinatorGeneralCatches[0]);
    Assert(failedDecisionMessages.Contains("未能打开截图工具，请使用图片导入或手动输入")
            && !failedDecisionMessages.Contains("截图账户未导入，请稍后重试。")
            && coordinatorFailureMessages.Contains("截图账户未导入，请稍后重试。")
            && !coordinatorFailureMessages.Contains("未能打开截图工具，请使用图片导入或手动输入")
            && launchMessages.Contains("未能打开截图工具，请使用图片导入或手动输入"),
        "协议失败、启动失败与二维码导入异常必须显示各自阶段的活动文案。");
    foreach (var message in new[]
    {
        "截图功能暂时不可用，请使用图片导入或手动输入",
        "截图已超时，请重新截图",
        "未能打开截图工具，请使用图片导入或手动输入",
        "选区中没有可识别的 TOTP 二维码",
    })
    {
        Assert(GetActiveStringLiterals(mainWindowClass).Contains(message), $"截图流程缺少活动文案：{message}");
    }
    foreach (var rejectedMessage in new[]
    {
        "截图请求无效，请重新截图。",
        "截图未完成，请重新尝试。",
        "截图中没有可识别的 TOTP 二维码。",
    })
    {
        Assert(!GetActiveStringLiterals(mainWindowClass).Contains(rejectedMessage),
            $"截图流程仍保留未通过文案：{rejectedMessage}");
    }
    var focusAddAction = FindUniqueMethod(authenticatorClass, "FocusAddAction");
    Assert(focusAddAction.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PublicKeyword))
            && HasInvocation(focusAddAction, "AddButton", "Focus", "FocusState.Programmatic"),
        "验证码列表必须公开最小焦点恢复边界。");
    var parameterlessShowAuthenticator = mainWindowClass.Members
        .OfType<MethodDeclarationSyntax>()
        .Single(method => method.Identifier.ValueText == "ShowAuthenticatorView"
            && method.ParameterList.Parameters.Count == 0);
    Assert(HasInvocation(parameterlessShowAuthenticator, "AuthenticatorView", "FocusAddAction"),
        "从添加账户页返回列表后必须恢复到唯一添加入口。");
    Assert(!HasInvocation(parameterlessShowAuthenticator, null, "RunPageEntranceTransitionAsync"),
        "导入或手动添加成功后的程序化列表跳转不得错误播放返回动画。");
    var addNavigationHandler = FindUniqueMethod(mainWindowClass, "ShowAddAccountView");
    var backNavigationHandler = mainWindowClass.Members
        .OfType<MethodDeclarationSyntax>()
        .Single(method => method.Identifier.ValueText == "ShowAuthenticatorView"
            && method.ParameterList.Parameters.Count == 2);
    Assert(HasInvocation(addNavigationHandler, null, "IsKeyboardNavigation")
            && HasInvocation(addNavigationHandler, null, "NavigateToAddAccountViewAsync"),
        "进入添加页必须在事件边界判断键盘触发并调用真实页面过渡。");
    Assert(HasInvocation(backNavigationHandler, null, "IsKeyboardNavigation")
            && HasInvocation(backNavigationHandler, null, "NavigateToAuthenticatorViewAsync"),
        "返回列表必须在事件边界判断键盘触发并使用相反方向过渡。");
    var pageTransition = FindUniqueMethod(mainWindowClass, "RunPageEntranceTransitionAsync");
    Assert(HasInvocation(mainWindowClass, "TimeSpan", "FromMilliseconds", "180")
            && mainWindowClass.Members.OfType<FieldDeclarationSyntax>().Any(field =>
                field.Declaration.Type.ToString() == "UISettings"
                && field.Declaration.Variables.Any(variable => variable.Initializer is not null))
            && mainWindowClass.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                .Any(access => access.Name.Identifier.ValueText == "AnimationsEnabled")
            && mainWindowClass.DescendantNodes().OfType<RecursivePatternSyntax>()
                .Any(pattern => pattern.ToString().Contains("FocusState.Keyboard", StringComparison.Ordinal)),
        "页面过渡必须固定为 180ms，并同时遵循系统动画设置和键盘输入来源。");
    Assert(HasInvocation(pageTransition, "ElementCompositionPreview", "GetElementVisual")
            && HasInvocation(pageTransition, "compositor", "CreateScalarKeyFrameAnimation")
            && HasInvocation(pageTransition, "compositor", "CreateVector3KeyFrameAnimation")
            && HasSimpleAssignment(pageTransition, "visual.Opacity", "0f")
            && HasSimpleAssignment(pageTransition, "visual.Offset", "new Vector3(effectiveOffset, 0f, 0f)")
            && !pageTransition.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                .Any(access => access.Name.Identifier.ValueText is "Scale" or "Rotation"),
        "真实页面过渡只能通过 Composition 修改 opacity 和 offset。");
    var completionHandlers = pageTransition.DescendantNodes()
        .OfType<VariableDeclaratorSyntax>()
        .Where(variable => variable.Identifier.ValueText == "completedHandler")
        .ToArray();
    var transitionDelays = pageTransition.DescendantNodes().OfType<InvocationExpressionSyntax>()
        .Where(invocation => InvocationMatches(invocation, "Task", "Delay", Array.Empty<string>()))
        .ToArray();
    Assert(completionHandlers.Length == 1
            && HasEventAssignment(pageTransition, "batch.Completed", "completedHandler", SyntaxKind.AddAssignmentExpression)
            && pageTransition.DescendantNodes().OfType<FinallyClauseSyntax>().Any(finallyClause =>
                HasEventAssignment(finallyClause, "batch.Completed", "completedHandler", SyntaxKind.SubtractAssignmentExpression)
                && HasInvocation(finallyClause, "batch", "Dispose")
                && HasInvocation(finallyClause, null, "ResetPageVisual"))
            && HasInvocation(pageTransition, "Task", "WhenAny")
            && transitionDelays.Length == 1
            && transitionDelays[0].ArgumentList.Arguments.Single().Expression is BinaryExpressionSyntax delayAddition
            && delayAddition.IsKind(SyntaxKind.AddExpression)
            && delayAddition.Left is IdentifierNameSyntax { Identifier.ValueText: "PageTransitionDuration" }
            && delayAddition.Right is InvocationExpressionSyntax delayPadding
            && InvocationMatches(delayPadding, "TimeSpan", "FromMilliseconds", ["100"]),
        "Composition ScopedBatch 必须使用具名完成处理器、有限等待，并在 finally 解订阅、释放和复位视觉状态。");
    Assert(HasIdentifierReference(FindUniqueMethod(mainWindowClass, "NavigateToAddAccountViewAsync"), "PageTransitionOffset")
            && FindUniqueMethod(mainWindowClass, "NavigateToAuthenticatorViewAsync").DescendantNodes()
                .OfType<PrefixUnaryExpressionSyntax>()
                .Any(prefix => prefix.IsKind(SyntaxKind.UnaryMinusExpression)
                    && prefix.Operand is IdentifierNameSyntax { Identifier.ValueText: "PageTransitionOffset" }),
        "进入与返回页面的横向位移方向必须相反。");
}

static MethodDeclarationSyntax FindUniqueMethod(ClassDeclarationSyntax targetClass, string methodName)
{
    var methods = targetClass.Members
        .OfType<MethodDeclarationSyntax>()
        .Where(method => method.Identifier.ValueText == methodName)
        .ToArray();
    Assert(methods.Length == 1, $"{targetClass.Identifier.ValueText} 必须唯一声明 {methodName}。");
    return methods.Single();
}

static bool HasBooleanTrueAssignment(SyntaxNode node, string targetName) =>
    node.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(assignment =>
        assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
        && GetRightmostName(assignment.Left) == targetName
        && assignment.Right.IsKind(SyntaxKind.TrueLiteralExpression));

static bool HasSimpleAssignment(SyntaxNode node, string left, string right) =>
    node.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(assignment =>
        assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
        && assignment.Left.ToString() == left
        && assignment.Right.ToString() == right);

static bool HasEventSubscription(SyntaxNode node, string eventExpression, string handler) =>
    HasEventAssignment(node, eventExpression, handler, SyntaxKind.AddAssignmentExpression);

static bool HasEventAssignment(SyntaxNode node, string eventExpression, string handler, SyntaxKind assignmentKind) =>
    node.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(assignment =>
        assignment.IsKind(assignmentKind)
        && assignment.Left.ToString() == eventExpression
        && assignment.Right is IdentifierNameSyntax identifier
        && identifier.Identifier.ValueText == handler);

static bool HasInvocation(
    SyntaxNode node,
    string? receiver,
    string methodName,
    params string[] argumentExpressions) =>
    CountInvocations(node, receiver, methodName, argumentExpressions) > 0;

static int CountInvocations(
    SyntaxNode node,
    string? receiver,
    string methodName,
    params string[] argumentExpressions) =>
    node.DescendantNodes().OfType<InvocationExpressionSyntax>().Count(invocation =>
        InvocationMatches(invocation, receiver, methodName, argumentExpressions));

static bool InvocationMatches(
    InvocationExpressionSyntax invocation,
    string? receiver,
    string methodName,
    IReadOnlyList<string> argumentExpressions)
{
    string? actualReceiver;
    string? actualMethod;
    if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
    {
        actualReceiver = memberAccess.Expression.ToString();
        actualMethod = memberAccess.Name.Identifier.ValueText;
    }
    else if (invocation.Expression is IdentifierNameSyntax identifier)
    {
        actualReceiver = null;
        actualMethod = identifier.Identifier.ValueText;
    }
    else
    {
        return false;
    }

    return actualReceiver == receiver
        && actualMethod == methodName
        && (argumentExpressions.Count == 0
            || invocation.ArgumentList.Arguments.Select(argument => argument.ToString())
                .SequenceEqual(argumentExpressions));
}

static string? GetRightmostName(ExpressionSyntax expression) => expression switch
{
    IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
    MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
    _ => null,
};

static bool HasIdentifierReference(SyntaxNode node, string identifier) =>
    node.DescendantTokens().Any(token => token.IsKind(SyntaxKind.IdentifierToken)
        && token.ValueText == identifier);

static HashSet<string> GetActiveStringLiterals(SyntaxNode node) =>
    node.DescendantNodes().OfType<LiteralExpressionSyntax>()
        .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
        .Select(literal => literal.Token.ValueText)
        .ToHashSet(StringComparer.Ordinal);

static void AssertClosedHandlerContract(MethodDeclarationSyntax closedHandler)
{
    Assert(closedHandler.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PrivateKeyword))
            && !closedHandler.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.AsyncKeyword))
            && closedHandler.ReturnType is PredefinedTypeSyntax returnType
            && returnType.Keyword.IsKind(SyntaxKind.VoidKeyword)
            && closedHandler.Body is { Statements.Count: > 0 },
        "窗口关闭处理必须是同步 private void 命名方法。");
    var firstStatement = closedHandler.Body!.Statements[0];
    Assert(firstStatement is IfStatementSyntax closeGate
            && HasInvocation(closeGate.Condition, "Interlocked", "Exchange", "ref _isClosed", "1"),
        "窗口关闭处理必须先通过 _isClosed 原子门保证幂等。");

    var orderedPositions = new[]
    {
        FindInvocationPosition(closedHandler, "_sessionGuard", "Invalidate"),
        FindInvocationPosition(closedHandler, "_sessionCancellation", "Invalidate"),
        FindInvocationPosition(closedHandler, "Volatile", "Write", "ref _activeSession", "0"),
        FindAssignmentPosition(closedHandler, "_isUnlocked", "false"),
        FindInvocationPosition(closedHandler, "_timer", "Stop"),
        FindInvocationPosition(closedHandler, null, "CancelBackgroundLock"),
        FindInvocationPosition(closedHandler, "ViewModel", "Unload"),
        FindInvocationPosition(closedHandler, "_snippingCaptureState", "Abandon"),
        FindInvocationPosition(closedHandler, "_sessionCancellation", "Dispose"),
    };
    Assert(orderedPositions.All(position => position >= 0)
            && orderedPositions.SequenceEqual(orderedPositions.OrderBy(position => position)),
        "窗口关闭必须依次失效会话、停止计时、卸载视图模型、放弃截图并释放取消域。");
    Assert(!HasInvocation(closedHandler, null, "HideActiveDialog")
            && !closedHandler.DescendantNodes().OfType<AssignmentExpressionSyntax>()
                .Any(assignment => GetRightmostName(assignment.Left) == "Visibility"),
        "窗口关闭路径不得依赖已关闭窗口的可视 UI 调用。");
}

static int FindInvocationPosition(
    SyntaxNode node,
    string? receiver,
    string methodName,
    params string[] argumentExpressions) =>
    node.DescendantNodes().OfType<InvocationExpressionSyntax>()
        .Where(invocation => InvocationMatches(invocation, receiver, methodName, argumentExpressions))
        .Select(invocation => invocation.SpanStart)
        .DefaultIfEmpty(-1)
        .First();

static int FindAssignmentPosition(SyntaxNode node, string left, string right) =>
    node.DescendantNodes().OfType<AssignmentExpressionSyntax>()
        .Where(assignment => assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
            && assignment.Left.ToString() == left
            && assignment.Right.ToString() == right)
        .Select(assignment => assignment.SpanStart)
        .DefaultIfEmpty(-1)
        .First();

static string? GetRuntimeXamlAttribute(System.Xml.Linq.XElement element, string localName)
{
    var ambiguousAttributes = element.Attributes()
        .Where(attribute => attribute.Name.Namespace != System.Xml.Linq.XNamespace.None
            && attribute.Name.LocalName == localName)
        .Select(attribute => attribute.Name.ToString())
        .ToArray();
    Assert(ambiguousAttributes.Length == 0,
        $"运行时属性 {localName} 不得被带命名空间的同名属性伪造：{string.Join(", ", ambiguousAttributes)}");
    return element.Attribute(System.Xml.Linq.XNamespace.None + localName)?.Value;
}

static void AssertButtonClickBinding(
    IReadOnlyCollection<System.Xml.Linq.XElement> buttons,
    string automationName,
    string expectedHandler)
{
    var matches = buttons
        .Where(element => GetRuntimeXamlAttribute(element, "AutomationProperties.Name") == automationName)
        .ToArray();
    Assert(matches.Length == 1, $"添加账户页必须有且只有一个“{automationName}”按钮。");
    Assert(GetRuntimeXamlAttribute(matches[0], "Click") == expectedHandler,
        $"“{automationName}”按钮必须绑定 {expectedHandler}。");
}

static CompilationUnitSyntax ParseCSharpRoot(string source, string fileName)
{
    var tree = CSharpSyntaxTree.ParseText(source, path: fileName);
    var errors = tree.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
    Assert(errors.Length == 0, $"{fileName} 必须能被 Roslyn 正确解析：{string.Join(" | ", errors.Select(error => error.ToString()))}");
    return tree.GetCompilationUnitRoot();
}

static ClassDeclarationSyntax FindUniqueClass(CompilationUnitSyntax root, string className)
{
    var matches = root.DescendantNodes()
        .OfType<ClassDeclarationSyntax>()
        .Where(declaration => declaration.Identifier.ValueText == className)
        .ToArray();
    Assert(matches.Length == 1, $"必须能唯一定位 {className} 类。");
    return matches.Single();
}

static void AssertNoConditionalCompilation(ClassDeclarationSyntax targetClass)
{
    var conditionalDirectives = targetClass
        .DescendantTrivia(descendIntoTrivia: true)
        .Where(trivia => trivia.HasStructure)
        .Select(trivia => trivia.GetStructure())
        .OfType<DirectiveTriviaSyntax>()
        .Where(directive => directive.IsKind(SyntaxKind.IfDirectiveTrivia)
            || directive.IsKind(SyntaxKind.ElifDirectiveTrivia)
            || directive.IsKind(SyntaxKind.ElseDirectiveTrivia)
            || directive.IsKind(SyntaxKind.EndIfDirectiveTrivia))
        .ToArray();
    Assert(conditionalDirectives.Length == 0,
        $"统一添加入口类 {targetClass.Identifier.ValueText} 不得用条件编译定义受保护事件或 handler："
        + string.Join(", ", conditionalDirectives.Select(directive => directive.ToString().Trim())));
}

static string[] GetEventNames(ClassDeclarationSyntax targetClass) =>
    targetClass.Members
        .OfType<EventFieldDeclarationSyntax>()
        .SelectMany(declaration => declaration.Declaration.Variables)
        .Select(variable => variable.Identifier.ValueText)
        .ToArray();

static void AssertPublicNullableEvent(ClassDeclarationSyntax targetClass, string eventName)
{
    var matches = targetClass.Members
        .OfType<EventFieldDeclarationSyntax>()
        .Where(declaration => declaration.Declaration.Variables
            .Any(variable => variable.Identifier.ValueText == eventName))
        .ToArray();
    Assert(matches.Length == 1, $"{targetClass.Identifier.ValueText} 必须唯一声明 {eventName} 事件。");
    var declaration = matches.Single();
    Assert(declaration.Declaration.Variables.Count == 1
            && declaration.Declaration.Variables[0].Identifier.ValueText == eventName,
        $"{eventName} 必须是独立事件字段声明。");
    Assert(declaration.Modifiers.Count == 1 && declaration.Modifiers[0].IsKind(SyntaxKind.PublicKeyword),
        $"{eventName} 必须且只能使用 public 修饰符。");
    Assert(declaration.Declaration.Type is NullableTypeSyntax
        {
            ElementType: IdentifierNameSyntax { Identifier.ValueText: "EventHandler" },
        }, $"{eventName} 的类型必须精确为 EventHandler?。");
}

static void AssertHandlerOnlyForwards(ClassDeclarationSyntax targetClass, string methodName, string eventName)
{
    var matches = targetClass.Members
        .OfType<MethodDeclarationSyntax>()
        .Where(method => method.Identifier.ValueText == methodName)
        .ToArray();
    Assert(matches.Length == 1, $"{targetClass.Identifier.ValueText} 必须唯一声明 {methodName}。");
    var method = matches.Single();
    Assert(method.Modifiers.Count == 1 && method.Modifiers[0].IsKind(SyntaxKind.PrivateKeyword),
        $"{methodName} 必须且只能使用 private 修饰符。");
    Assert(method.ReturnType is PredefinedTypeSyntax returnType && returnType.Keyword.IsKind(SyntaxKind.VoidKeyword),
        $"{methodName} 的返回类型必须精确为 void。");
    Assert(method.ParameterList.Parameters.Count == 2,
        $"{methodName} 必须精确声明 object sender 和 RoutedEventArgs e 两个参数。");
    AssertExactHandlerParameter(method.ParameterList.Parameters[0], "sender", SyntaxKind.ObjectKeyword, null, methodName);
    AssertExactHandlerParameter(method.ParameterList.Parameters[1], "e", null, "RoutedEventArgs", methodName);
    ExpressionSyntax? expression = method.ExpressionBody?.Expression;
    if (expression is null
        && method.Body is { Statements.Count: 1 }
        && method.Body.Statements[0] is ExpressionStatementSyntax expressionStatement)
    {
        expression = expressionStatement.Expression;
    }

    Assert(expression is ConditionalAccessExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax
            {
                Expression: ThisExpressionSyntax,
                Name: IdentifierNameSyntax eventIdentifier,
            },
            WhenNotNull: InvocationExpressionSyntax
            {
                Expression: MemberBindingExpressionSyntax
                {
                    Name: IdentifierNameSyntax { Identifier.ValueText: "Invoke" },
                },
                ArgumentList.Arguments:
                [
                    { Expression: ThisExpressionSyntax },
                    {
                        Expression: MemberAccessExpressionSyntax
                        {
                            Expression: IdentifierNameSyntax { Identifier.ValueText: "EventArgs" },
                            Name: IdentifierNameSyntax { Identifier.ValueText: "Empty" },
                        },
                    },
                ],
            },
        } && eventIdentifier.Identifier.ValueText == eventName,
        $"{methodName} 必须且只能执行 this.{eventName}?.Invoke(this, EventArgs.Empty)。");
}

static void AssertExactHandlerParameter(
    ParameterSyntax parameter,
    string expectedName,
    SyntaxKind? expectedPredefinedType,
    string? expectedIdentifierType,
    string methodName)
{
    Assert(parameter.Identifier.ValueText == expectedName,
        $"{methodName} 的参数名必须精确为 {expectedName}。");
    Assert(parameter.AttributeLists.Count == 0 && parameter.Modifiers.Count == 0 && parameter.Default is null,
        $"{methodName} 的 {expectedName} 参数不得使用特性、params/ref/out/in/this/scoped 或默认值。");
    var hasExpectedType = expectedPredefinedType is SyntaxKind predefinedType
        ? parameter.Type is PredefinedTypeSyntax type && type.Keyword.IsKind(predefinedType)
        : parameter.Type is IdentifierNameSyntax identifierType
            && identifierType.Identifier.ValueText == expectedIdentifierType;
    Assert(hasExpectedType,
        $"{methodName} 的 {expectedName} 参数类型必须精确为 {expectedIdentifierType ?? "object"}。");
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
