using System.Collections.Concurrent;

namespace MyRLBridge.MyRLBridgeCode;

public static class ActionQueue
{
    private static readonly ConcurrentQueue<string> Pending = new();

    public static void Enqueue(string json)
    {
        Pending.Enqueue(json);
    }

    public static bool TryDequeue(out string json)
    {
        return Pending.TryDequeue(out json!);
    }
}