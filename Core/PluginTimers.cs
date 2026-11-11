using CounterStrikeSharp.API.Core;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace cs2_rockthevote;

// CSS379 exposes BasePlugin.Timers publicly and Dispose kills every retained wrapper.
// Forget only this exact managed wrapper, never another timer reusing its raw pointer.
public static class PluginTimers
{
    public static void Forget(BasePlugin? plugin, Timer timer)
    {
        if (plugin != null) RemoveReference(plugin.Timers, timer);
    }
    public static void Kill(BasePlugin? plugin, Timer timer)
    {
        if (plugin == null) timer.Kill();
        else Release(plugin.Timers, timer, owned => owned.Kill());
    }
    public static void RemoveReference<T>(IList<T> registry, T owned) where T : class
    {
        for (int index = registry.Count - 1; index >= 0; index--)
            if (ReferenceEquals(registry[index], owned)) registry.RemoveAt(index);
    }
    public static void Release<T>(IList<T> registry, T owned, Action<T> kill) where T : class
    {
        kill(owned);
        RemoveReference(registry, owned);
    }
}
