using System.Text;

namespace cs2_rockthevote.ScreenMenus;

// These managed policies are used by the live service and tested without a game host.
public sealed class OwnedSessions<TKey, TValue>(Action<TValue> release) where TKey : notnull where TValue : class
{
    private readonly Dictionary<TKey, TValue> _items = new();
    public TValue[] Snapshot => _items.Values.ToArray();
    public bool IsCurrent(TKey key, TValue value) => _items.TryGetValue(key, out var current) && ReferenceEquals(current, value);
    public TValue? Get(TKey key) => _items.GetValueOrDefault(key);
    public void Replace(TKey key, TValue value) { Remove(key); _items.Add(key, value); }
    public void Remove(TKey key) { if (_items.Remove(key, out var old)) release(old); }
    public void Clear()
    {
        List<Exception> errors = new();
        foreach (var key in _items.Keys.ToArray()) { try { Remove(key); } catch (Exception e) { errors.Add(e); } }
        if (errors.Count != 0) throw new AggregateException(errors);
    }
    public bool Invoke(TKey key, TValue value, bool closeFirst, Action callback, Action refresh)
    {
        if (!IsCurrent(key, value)) return false;
        if (closeFirst) Remove(key);
        callback();
        if (IsCurrent(key, value)) refresh();
        return true;
    }
}

public sealed class CallbackLease : IDisposable
{
    private readonly List<Action> _release = new();
    public void Add(Action register, Action unregister)
    {
        // Registration can throw after changing external state. Keep its compensating action first.
        _release.Add(unregister);
        register();
    }
    public void Dispose()
    {
        var pending = _release.ToArray();
        List<Exception> errors = new();
        foreach (var action in pending.Reverse()) { try { action(); _release.Remove(action); } catch (Exception e) { errors.Add(e); } }
        if (errors.Count != 0) throw new AggregateException(errors);
    }
}

public sealed class MovementLease<T>(uint identity, T move, T actual, T frozen) where T : struct, Enum
{
    private bool _movePending;
    private bool _actualPending;
    private bool _restoring;
    public bool Pending => _movePending || _actualPending;
    public void Apply(Action<T> writeMove, Action<T> writeActual)
    {
        if (Pending || _restoring) throw new InvalidOperationException("Movement lease cannot be reused.");
        // Mark before each call: an exception may occur after the native write.
        _movePending = true; writeMove(frozen);
        _actualPending = true; writeActual(frozen);
    }
    public void Abandon() { _movePending = _actualPending = false; _restoring = true; }
    public void Restore(uint currentIdentity, Func<T> readMove, Func<T> readActual, Action<T> writeMove, Action<T> writeActual)
    {
        if (!Pending) return;
        if (identity != currentIdentity) { Abandon(); return; }
        if (!_restoring)
        {
            // Interference before restoration relinquishes the entire lease. Partial Apply owns only its attempted fields.
            if ((_movePending && !Equal(readMove(), frozen) && !Equal(readMove(), move)) ||
                (_actualPending && !Equal(readActual(), frozen) && !Equal(readActual(), actual))) { Abandon(); return; }
            _restoring = true;
        }
        List<Exception> errors = new();
        if (_movePending)
        {
            try { var current = readMove(); if (Equal(current, frozen) || Equal(current, move)) writeMove(move); _movePending = false; }
            catch (Exception e) { errors.Add(e); }
        }
        if (_actualPending)
        {
            try { var current = readActual(); if (Equal(current, frozen) || Equal(current, actual)) writeActual(actual); _actualPending = false; }
            catch (Exception e) { errors.Add(e); }
        }
        if (errors.Count != 0) throw new AggregateException(errors);
    }
    private static bool Equal(T a, T b) => EqualityComparer<T>.Default.Equals(a, b);
}

public sealed class OwnedResources<T> where T : class
{
    private readonly List<T> _items = new();
    public T[] Snapshot => _items.ToArray();
    public void Add(T resource) => _items.Add(resource);
    public void ReleaseAll(Action<T> release)
    {
        List<Exception> errors = new();
        foreach (var item in _items.ToArray().Reverse())
        { try { release(item); _items.Remove(item); } catch (Exception e) { errors.Add(e); } }
        if (errors.Count != 0) throw new AggregateException(errors);
    }
}

// Resolve a captured full identity afresh. Never act on a replacement object at the same slot/address.
public sealed class IdentityLease<T>(uint identity, Func<uint, T?> resolve, Func<T, uint> readIdentity) where T : class
{
    public T? Resolve()
    {
        var value = resolve(identity);
        return value != null && readIdentity(value) == identity ? value : null;
    }
}

public sealed class PositionBook
{
    private readonly Dictionary<ulong, MenuPosition> _saved = new();
    private readonly Dictionary<uint, MenuPosition> _temporary = new();
    public IReadOnlyDictionary<ulong, MenuPosition> Saved => _saved;
    public MenuPosition? Get(ulong steamId, uint controller) => steamId == 0 ? _temporary.GetValueOrDefault(controller) : _saved.GetValueOrDefault(steamId);
    public bool Set(ulong steamId, uint controller, MenuPosition position)
    {
        if (steamId == 0) { _temporary[controller] = position; return false; }
        _saved[steamId] = position; return true;
    }
    public void ForgetTemporary(uint controller) => _temporary.Remove(controller);
    public void ClearTemporary() => _temporary.Clear();
}

public static class StartupGuard
{
    public static void Complete(Action start, Action rollback)
    {
        try { start(); }
        catch (Exception startError)
        {
            try { rollback(); } catch (Exception cleanupError) { throw new AggregateException(startError, cleanupError); }
            throw;
        }
    }
}

public sealed class ButtonEdges(ulong initial)
{
    private ulong _previous = initial;
    public ulong Sample(ulong current) { var pressed = current & ~_previous; _previous = current; return pressed; }
}

public readonly record struct MenuEntry(int Key, int Option, string Text, bool Enabled);

public static class MenuNavigation
{
    public static List<MenuEntry> Entries(IReadOnlyList<(string Text, bool Disabled)> options, MenuCursor cursor, bool exit, bool resolution, Func<string, string> label)
    {
        var entries = new List<MenuEntry>();
        int start = cursor.Page * MenuCursor.PageSize;
        for (int i = start; i < Math.Min(start + MenuCursor.PageSize, options.Count); i++)
            entries.Add(new(i - start + 1, i, options[i].Text, !options[i].Disabled));
        if (cursor.Page > 0) entries.Add(new(7, -1, label("Prev"), true));
        if (cursor.Page + 1 < cursor.Pages(options.Count)) entries.Add(new(8, -1, label("Next"), true));
        if (exit) entries.Add(new(9, -1, label("Close"), true));
        if (resolution) entries.Add(new(0, -1, label("ChangeRes"), true));
        return entries;
    }
    public static MenuEntry? Choice(IEnumerable<MenuEntry> entries, int key) => entries.Where(x => x.Key == key && x.Enabled).Select(x => (MenuEntry?)x).FirstOrDefault();
}

public sealed class MenuCursor
{
    public int Page { get; private set; }
    public int Selection { get; private set; }
    public const int PageSize = 6;
    public int Pages(int count) => Math.Max(1, (count + PageSize - 1) / PageSize);
    public bool Turn(int direction, int count)
    {
        int next = Math.Clamp(Page + direction, 0, Pages(count) - 1);
        if (next == Page) return false;
        Page = next; Selection = 0; return true;
    }
    public void Reset() { Page = Selection = 0; }
    public void Move(int direction, int enabledCount)
    {
        Selection = enabledCount == 0 ? 0 : (Selection + direction + enabledCount) % enabledCount;
    }
    public void Clamp(int enabledCount) => Selection = Math.Clamp(Selection, 0, Math.Max(0, enabledCount - 1));
}

public static class ScreenText
{
    // The engine's message field has 512 bytes, including its terminator.
    public static string Fit(string text, int bytes = 511)
    {
        var output = new StringBuilder();
        int used = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (used + rune.Utf8SequenceLength > bytes) break;
            output.Append(rune); used += rune.Utf8SequenceLength;
        }
        return output.ToString();
    }
    public static string OneLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ').Replace('\0', ' ');
}
