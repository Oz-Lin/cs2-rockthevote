# Semantic backports from Oz-Lin/main

Audited heads: migration `152cf61cf7e7d86a04cfcf2a307f64350f3595b2`; main `bba6dba74836751e25ab4c80270f6b90fe7f255f`. Their merge base is `70b531912dd81e45f18193407b11d3bedb780275`.

This is a semantic backport, **not a merge of main**. The GitHub ahead/behind graph can still show the 11 main-only commits. Integer `HudMenu` (0/1/2), VIP settings and the independent CSS379 screen renderer are retained. No server files, shared map list, other map chooser or server installation is changed by these source commits.

## Every main-only commit

| Original commit | Original purpose | Coverage in this branch |
| --- | --- | --- |
| [abdcab83](https://github.com/Oz-Lin/cs2-rockthevote/commit/abdcab83a2278069ede146a445f3c6e9b6093192) | Upgrade configuration warning | Preserved with a safer backup-and-review reminder, not an instruction to delete custom settings. |
| [31bc4abf](https://github.com/Oz-Lin/cs2-rockthevote/commit/31bc4abf2d6b85dd6a9ddb48d946e7ac82653c51) | Read `ModuleDirectory/maplist.txt` | Backported through the live MapListPolicy. No fallback, auto-create, migration or write-back. |
| [7b287fb7](https://github.com/Oz-Lin/cs2-rockthevote/commit/7b287fb7ad4196eb63d6e08d04b3a8e2c2734a03) | API 315 to 335 | Already superseded by CSS379/net10; not downgraded. |
| [72eac5ad](https://github.com/Oz-Lin/cs2-rockthevote/commit/72eac5ad43af0e40c16cece5128fe7a7b30be96f) | Merge PR39 | No separate merge-only behavior. Covered by the preceding changes. |
| [41ae9053](https://github.com/Oz-Lin/cs2-rockthevote/commit/41ae905393767a1bb4c2d8bfa643342e0f6ccb79) | Set nextlevel; finish vote at match panel | Safely rewritten: Pre hook finishes the active generation synchronously, and validated real map targets use synchronous typed ConVar setter/readback. No mutable timeLeft shortcut, stale winner field or unconditional finally assignment. Temporary debug chat from this commit is not included. |
| [119236f8](https://github.com/Oz-Lin/cs2-rockthevote/commit/119236f8b023e3f703554a67635aacf3ecb6841d) | Remove debug chat; optional match pause | Debug chat already absent. Pause option backported with a per-generation request lease, initial-state checks and explicit cleanup. See limitations below. |
| [90e2189c](https://github.com/Oz-Lin/cs2-rockthevote/commit/90e2189cf06a804f434684f4e6cc600976cd73cd) | Remove unnecessary ChangeMapManager injection | Already covered: EndOfMapVote does not add that dependency. |
| [d1bc945c](https://github.com/Oz-Lin/cs2-rockthevote/commit/d1bc945c8652c61f5e5e569907c1caf6e95accc1) | Keep pause option only in EndOfMapConfig | Preserved. The new property defaults to false; integer HUD types remain unchanged. |
| [6b6b5f30](https://github.com/Oz-Lin/cs2-rockthevote/commit/6b6b5f30aa8c63f87cd150b80502f8315138681f) | Read global EndOfMapVote pause option in shared manager | Preserved: when enabled it also applies to RTV map ballots. The lease captures each request instead of re-reading config to decide what to unpause. |
| [6e8f5c35](https://github.com/Oz-Lin/cs2-rockthevote/commit/6e8f5c35c97903666a4f843cbc433e89b8069e33) | Do not set nextlevel for Workshop-ID transition | Preserved for `host_workshop_map ID`. Valid local maps and name-only workshop transitions use nextlevel. |
| [bba6dba7](https://github.com/Oz-Lin/cs2-rockthevote/commit/bba6dba74836751e25ab4c80270f6b90fe7f255f) | Merge PR40 | No separate merge-only behavior. The useful behavior above is backported without the unsafe extension/null nextlevel assignment. |

## Map list and transition contract

- `maplist.txt` is read only from the plugin module directory. Name/Workshop numeric ID entries, comments, blank lines, line endings, ordering and exact duplicate rows retain their parsing behavior. The JSON configuration directory is separate.
- A map transition must resolve a loaded real map and a safe command token/positive numeric Workshop ID. Localized extension labels, unknown map choices and command separators are not dispatched.
- The selected map is an immutable snapshot. Replacement schedules, map changes and unload invalidate old tickets; both delay timers stop on map change. Duplicate callbacks cannot execute twice.
- `nextlevel` is assigned only for a real eligible map and read back immediately through [CSS379 ConVar<string>](https://github.com/roflmuffin/CounterStrikeSharp/blob/f58e383/managed/CounterStrikeSharp.API/Modules/Cvars/ConVarOfT.cs). Missing convars, native setter errors and readback mismatches are logged as failures. This does not prove when the engine consumes the value.
- Replacing/cancelling a schedule restores only this plugin's still-current nextlevel assignment. A later different external value is preserved; old-map ownership is abandoned rather than restored into a new map.
- Extension results and empty/failed ballots never write the localized option or a stale winner to nextlevel. Configured extension limits remain in use across maps.

## Optional match pause and its limits

`EndOfMapVote.PauseMatchWhenVote` defaults to **false**, as on main. Main uses this single setting for both end-of-map and RTV ballots; that behavior is retained.

When enabled, a pause request is acquired only with known game rules that are initially unpaused and have no team/technical timeout. A preexisting administrative pause is not acquired or released. Cleanup for the same vote/map generation queues an unpause after normal completion, startup/timer failure or unload. A team/technical timeout takes precedence. Map changes abandon the old lease without issuing an old-map unpause into the new map. Failed enqueue cleanup remains pending rather than being claimed successful.

This is **request ownership**, not an engine-provided ownership token. [CSS379 Server.ExecuteCommand](https://github.com/roflmuffin/CounterStrikeSharp/blob/f58e383/managed/CounterStrikeSharp.API/Server.cs) queues engine commands. Its public API does not distinguish a later identical `mp_pause_match` issued by RCON or another plugin. The code does not claim to preserve an indistinguishable later administrative re-pause. Keep the setting disabled when multiple systems control match pausing, or coordinate a single pause controller and validate concurrency on a test server before enabling it. No guessed offsets, synchronous-dispatch assumptions or hidden marker commands are used.

## Validation

The first backport set adds 74 managed checks to the prior 159, for **233** checks. They exercise the actual MapListPolicy, MapTarget, MapTransitionPolicy, NextLevelPolicy, VotePauseLease and CleanupActions used in production, plus existing ballot policies and compiled CSS379 TypeRefs. They cover map-list path/read-only/missing-file behavior, command safety, Workshop branches, synchronous setter/readback failure, immutable generation tickets, empty/zero ballots, pause ownership/fault cases and all-cleanup execution.

Run the commands in [CSS379 screen-menu notes](CSS379_SCREEN_MENU.md). These are managed regressions, not game-server acceptance. Before deployment, test the actual `game_type`/`game_mode`, required time/round/nextlevel convars, normal and workshop map changes, match-panel ordering, pause queues/interference, and map change → extension → later automatic vote. Historical custom/zombie-mode reports are test prompts, not evidence of any current server's mode.
