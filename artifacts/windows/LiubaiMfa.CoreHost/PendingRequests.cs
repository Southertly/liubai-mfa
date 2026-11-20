using System.Collections.Concurrent;
namespace LiubaiMfa.CoreHost;
internal sealed class PendingRequests
{
    private readonly ConcurrentDictionary<string,byte> ids = new();
    public bool TryEnter(string id) => ids.TryAdd(id,0);
    public void Exit(string id) => ids.TryRemove(id,out _);
}
