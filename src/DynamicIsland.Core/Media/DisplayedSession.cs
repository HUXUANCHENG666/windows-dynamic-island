namespace DynamicIsland.Core.Media;

public readonly record struct SessionObservation(string Id, bool IsPlaying, bool IsSystemCurrent);

/// <summary>
/// 多条系统媒体会话里，岛该显示哪一条。
/// 有正在播放的，就显示最晚开始播放的那条。都停着，就回到系统当前会话。
/// 展开时用户可以用左右切换，这个选择会钉住，直到那条会话消失，或者岛收起。
/// 播放控制只打到当前显示的这一条。
/// </summary>
public sealed class DisplayedSession
{
    readonly Dictionary<string, long> _started = new();
    readonly Dictionary<string, bool> _playing = new();
    readonly List<SessionObservation> _slots = new();
    long _sequence;

    public string? PinnedId { get; private set; }
    public string? SelectedId { get; private set; }
    public int Index { get; private set; }
    public int Count => _slots.Count;
    public string? CommandTarget => SelectedId;

    public void Update(IReadOnlyList<SessionObservation> sessions)
    {
        var live = new HashSet<string>(sessions.Count);
        var newly = new List<string>();
        foreach (var session in sessions)
        {
            live.Add(session.Id);
            bool was = _playing.TryGetValue(session.Id, out var previous) && previous;
            if (session.IsPlaying && !was)
                newly.Add(session.Id);
            _playing[session.Id] = session.IsPlaying;
            if (!session.IsPlaying)
                _started.Remove(session.Id);
        }

        if (newly.Count > 0)
        {
            long batch = ++_sequence;
            foreach (var id in newly)
                _started[id] = batch;
        }

        var stale = new List<string>();
        foreach (var id in _playing.Keys)
        {
            if (!live.Contains(id))
                stale.Add(id);
        }

        foreach (var id in stale)
        {
            _playing.Remove(id);
            _started.Remove(id);
        }

        if (PinnedId != null && !live.Contains(PinnedId))
            PinnedId = null;

        _slots.Clear();
        _slots.AddRange(sessions);
        SelectedId = Choose(PinnedId);
        Index = PositionOf(SelectedId);
    }

    public bool TrySwitch(int delta)
    {
        if (_slots.Count < 2 || delta == 0)
            return false;
        int index = PositionOf(SelectedId);
        if (index == 0)
            index = 1;
        int next = index - 1 + delta;
        next %= _slots.Count;
        if (next < 0)
            next += _slots.Count;
        PinnedId = _slots[next].Id;
        SelectedId = PinnedId;
        Index = next + 1;
        return true;
    }

    public void ClearPin()
    {
        if (PinnedId == null)
            return;
        PinnedId = null;
        SelectedId = Choose(null);
        Index = PositionOf(SelectedId);
    }

    string? Choose(string? pinned)
    {
        if (pinned != null)
        {
            foreach (var slot in _slots)
            {
                if (slot.Id == pinned)
                    return pinned;
            }
        }

        string? bestId = null;
        long bestOrder = 0;
        bool bestCurrent = false;
        foreach (var slot in _slots)
        {
            if (!slot.IsPlaying || !_started.TryGetValue(slot.Id, out var order))
                continue;
            bool better = bestId == null
                || order > bestOrder
                || (order == bestOrder && slot.IsSystemCurrent && !bestCurrent);
            if (!better)
                continue;
            bestId = slot.Id;
            bestOrder = order;
            bestCurrent = slot.IsSystemCurrent;
        }

        if (bestId != null)
            return bestId;
        foreach (var slot in _slots)
        {
            if (slot.IsSystemCurrent)
                return slot.Id;
        }

        return _slots.Count > 0 ? _slots[0].Id : null;
    }

    int PositionOf(string? id)
    {
        if (id == null)
            return 0;
        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].Id == id)
                return i + 1;
        }

        return 0;
    }
}

/// <summary>
/// 系统每次重新给出会话列表时，尽量沿用原来的编号。同一个应用开了两份，就按出现顺序对上。
/// </summary>
public static class SessionIdentity
{
    public static string[] Assign(
        IReadOnlyList<string> previousIds,
        IReadOnlyList<string> previousAumids,
        IReadOnlyList<string> incomingAumids,
        ref int counter)
    {
        int previous = Math.Min(previousIds.Count, previousAumids.Count);
        var assigned = new string?[incomingAumids.Count];
        var used = new bool[previous];
        for (int i = 0; i < incomingAumids.Count; i++)
        {
            int matches = 0;
            int match = -1;
            for (int p = 0; p < previous; p++)
            {
                if (used[p] || !string.Equals(previousAumids[p], incomingAumids[i], StringComparison.Ordinal))
                    continue;
                matches++;
                match = p;
            }

            if (matches != 1)
                continue;
            used[match] = true;
            assigned[i] = previousIds[match];
        }

        for (int i = 0; i < incomingAumids.Count; i++)
        {
            if (assigned[i] != null)
                continue;
            int match = -1;
            for (int p = 0; p < previous; p++)
            {
                if (used[p] || !string.Equals(previousAumids[p], incomingAumids[i], StringComparison.Ordinal))
                    continue;
                match = p;
                break;
            }

            if (match >= 0)
            {
                used[match] = true;
                assigned[i] = previousIds[match];
            }
            else
            {
                assigned[i] = "s" + (++counter);
            }
        }

        var ids = new string[assigned.Length];
        for (int i = 0; i < assigned.Length; i++)
            ids[i] = assigned[i] ?? "s" + (++counter);
        return ids;
    }
}
