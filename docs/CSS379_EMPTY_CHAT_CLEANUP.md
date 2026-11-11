# Empty chat cleanup guard

This narrow follow-up to `99e96de59cc4401600e320eed83b089029cde0da` changes only two chat-menu cleanup paths and build identification. ModuleVersion is `1.9.7-css379-votefixes.1`.

- `EndMapVoteManager.CloseVoteMenus` skips player/entity enumeration when its owned chat-menu dictionary is empty.
- `ExtendRoundTimeManager.KillTimer` does the same for its own chat-menu dictionary.
- Screen-menu cleanup, logical state invalidation, timer cancellation and the surrounding cleanup sequence still run. There is no early return from the entire cleanup operation.
- The real production entry points still use `Utilities.GetPlayers`. Private overloads permit fault-injected enumeration in tests without a fabricated CSS class or native controller.
- For nonempty chat ownership, existing enumeration, per-item logging/catch, aggregate failure and independent timer cleanup behavior are unchanged. This patch does not newly guarantee retryable nonempty chat-menu cleanup: the preexisting dictionary-clear behavior remains outside this narrow scope. Genuine native resource cleanup failures must still be investigated.

The complete managed suite has **342 checks**: the earlier 333 plus 9 checks against these actual cleanup bodies and real no-argument Stop routes. Empty-resource cleanup succeeds without a native entity system; nonempty injected failures keep their original boundaries. These are not game-server unload/visual acceptance tests.

This is **not a CounterStrikeSharp late-load fix**. CSS379 must complete its native startup lifecycle before entity-dependent plugins can operate. The original missing entity-system prerequisite and the initial GameRules readiness gap are not changed here. Loading a framework mid-map, then seeing quieter empty cleanup, is not an operational pass. All existing native gates in the other migration notes still apply.

The existing source/configuration/menu/timer policy and shared map-list contract are retained. No server deployment is part of this source commit. Keep the preceding approved artifact available for rollback; verify the exact new Release hash before any separately authorized deployment.
