using LiubaiMfa.Core.Models;
using LiubaiMfa.Windows.ViewModels;

var checks = new List<(string Name, Action Run)>
{
    ("搜索覆盖服务、账户和分组", VerifySearch),
    ("分组过滤与搜索叠加", VerifyGroupAndSearch),
    ("排序忽略大小写且稳定", VerifyStableSort),
    ("分组计数包含全部和非空分组", VerifyGroupCounts),
};

foreach (var check in checks)
{
    check.Run();
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

static MfaAccount[] CreateFixture() =>
[
    new MfaAccount { Id = "1", Issuer = "GitHub", Account = "alice", Secret = "A", Group = "个人", CreatedAt = 1 },
    new MfaAccount { Id = "2", Issuer = "Google", Account = "bob", Secret = "B", Group = " ", CreatedAt = 2 },
    new MfaAccount { Id = "3", Issuer = "GitHub", Account = "charlie", Secret = "C", Group = " 工作 ", CreatedAt = 3 },
    new MfaAccount { Id = "4", Issuer = "github", Account = "Charlie", Secret = "D", Group = "工作", CreatedAt = 4 },
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
