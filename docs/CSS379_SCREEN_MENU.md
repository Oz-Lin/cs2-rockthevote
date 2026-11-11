# CounterStrikeSharp 379 screen-menu migration

This branch targets **CounterStrikeSharp.API 1.0.379 and .NET 10**. It preserves the ScreenMenu branch's integer `HudMenu` settings, VIP extension settings, nomination, map voting, RTV and revote behavior. It does not use the old CS2ScreenMenuAPI DLL or package.

## Rendering and controls

Mode 2 uses plugin-owned `point_worldtext` entities mounted to `CPointOrient` through the supported CSS schema/API. It is a real screen-text menu, not a chat or center-HTML replacement. The existing center vote tally remains.

- `KeyPress`: numbered CSS commands, such as `!1` through `!9` and `!0` in chat, or `css_1` through `css_9`/`css_0` in the console. These preserve the former library's command contract. Physical weapon-slot binds are not intercepted or rewritten.
- `Scrollable`: configured movement/use buttons (default W/S/E). Selection is edge-triggered; holding E when opening a menu does not immediately vote.
- `Both`: both input methods.
- Six options per page, 7 Back, 8 Next, 9 Close, 0 Change Resolution when applicable. Disabled choices cannot vote. Long labels are shortened by UTF-8 byte budget to leave navigation visible within the engine's 512-byte text buffers; callbacks retain the full map name.

The old engine viewmodel schema no longer exists. Camera-follow rendering can differ in weapon/HUD occlusion, fast turns, crouching, FOV and spectating. A build or managed test run is **not** game-server acceptance. See the native gates below.

## Existing customization

Settings are read on each menu open, in this order:

1. `plugins/RockTheVote/screenmenu.toml`, if supplied.
2. Existing `shared/CS2ScreenMenuAPI/config.toml`, read-only.
3. Defaults matching the documented legacy settings (KeyPress, Tahoma Bold, size 25, W/S/E, freeze enabled for scroll modes).

The legacy library is not loaded. Its directory may remain only as a configuration import source. No existing TOML is rewritten. Unknown keys remain in the source file. Invalid modes, controls, numeric ranges or malformed TOML are logged and the screen menu fails closed instead of silently changing controls.

Supported sections/keys:

- `[Settings]`: FontName, MenuType, Size, PositionX, PositionY, HasExitOption, ShowResolutionOption, ShowPageCount, ShowDisabledOptionNum, FreezePlayer, ShowControlsInfo, ScrollPrefix.
- `[Settings.Resolutions."name"]`: PositionX, PositionY.
- `[Controls]`: ScrollUp, ScrollDown, Select. Original aliases W/A/S/D/E/R/Space/Shift and CSS button names are accepted.
- `[Sounds]`: Select, Next, Prev, Close, ScrollUp, ScrollDown, Volume.
- `[Lang.<language>]`: Prev, Next, Close, SelectRes, ChangeRes, ControlInfo, ScrollKeys, SelectKey. Player `css_lang` is respected, with English/default fallback.

SteamID-keyed `player_resolutions.json` is imported from the legacy directory if a plugin-local file does not exist. New selections are saved atomically in `plugins/RockTheVote/player_resolutions.json`; the old file is never modified. Corrupt imported state is logged and not overwritten. Anonymous players (SteamID 0) use temporary controller-serial-keyed positions, never a shared/persisted zero-ID setting. The resolution selector itself uses the new screen renderer. With no resolution list, the configured PositionX/PositionY are used directly.

The example `screenmenu.example.toml` is optional. Rename a copy only if you want a plugin-local configuration; it does not override a server's existing file automatically.

## Ownership and voting fixes

- Nomination and votemap create an independent menu for each player, not a globally cached instance bound to the first player.
- One service owns callbacks for a plugin load. Reopening menus does not register more listeners. Controller/pawn identities include their entity serial.
- Selection retires the old session before a closing callback, so a callback cannot close its replacement menu.
- Movement is leased from the exact original pawn. Both movement fields are captured, partial writes can be rolled back, failures retain pending work for retry, and an unrelated movement change or replacement pawn is not overwritten.
- Owned text/mount entities capture full serial handles and are freshly resolved before deletion or transmit filtering, so a recycled entity is never treated as owned. They are filtered to their owner. Closing immediately retires input. Failed native deletions stay in the ownership/transmit registry, are disabled where possible and retried. Failed callback removals are retained for retry. Persistent cleanup failure is an error, not a successful unload.
- Map change, round start, disconnect, respawn/pawn replacement and unload release menus. Changing observer target rebuilds the mount. A menu closed on pawn replacement can be reopened with the original command.
- End-map options are initialized once per ballot. Reopening/revoting cannot reset other players' totals. Ballots use controller serial identity and generation checks; stale menu/timer callbacks cannot alter a later ballot.
- Each vote closes only its own menus. An inactive vote no longer closes unrelated nomination/votemap menus every tick.
- Existing no-vote random map selection and nonempty ties remain; an empty candidate list ends without scheduling a map change. Extension votes with no actual votes fail closed.

## Build and managed regression

With .NET 10 SDK and normal approved package access:

```sh
dotnet restore tests/RockTheVote.ManagedTests.csproj
dotnet build RockTheVote.csproj -c Release --no-restore
dotnet run --project tests/RockTheVote.ManagedTests.csproj -c Release --no-restore
```

The regression executable tests the policies used by the live plugin, using the real compiled plugin and CSS379 assembly, without substitute CounterStrikeSharp classes. It includes prior configuration/vote regressions, multiplayer ballot behavior, session replacement, callback ownership, key edges, disabled choices/paging, TOML compatibility, movement and cleanup fault injection, and compiled TypeRef checks. It does not execute native CS2 calls.

This migration was validated locally with cached packages and an empty-source NuGet config, without a new network vulnerability audit. That local machine-specific config is intentionally not committed.

CI on the migration branch performs restore/build/managed tests only, with read-only repository permission. It does not upload build artifacts or contact a game server. The preexisting release workflow is tag-only; this change does not create tags or releases.

## Native acceptance gates before deployment

Use a backed-up test instance matching the target CSS/runtime/game build. Check server logs and entity counts before/after each flow:

1. Load, open/close and repeat menus; both clients open independent menus; only the owner sees text, including spectators/HLTV behavior.
2. KeyPress, Scrollable and Both; configured aliases; opening with E held; disabled items; pagination; resolution changes; localized text and sounds.
3. Nomination, votemap, RTV, end-map, VIP extend and revote, including two players voting, ties, no votes, timeouts and old callbacks.
4. Walk, noclip and pre-frozen players; close, disconnect, death/respawn, round restart, map change and hot reload. No player may remain menu-frozen and no unrelated movement mode may change.
5. Crouch, rapid mouse turns, FOV changes, observer target changes and free camera. Verify readable positioning and acceptable occlusion.
6. Unload/reload repeatedly. No owned entities/listeners may accumulate. A cleanup error blocks deployment; do not treat retry logging as a pass.
7. Verify only RockTheVote owns the intended voting/menu workflow and that rollback restores the exact previous plugin and settings.

No server deployment or native acceptance is claimed by this source branch.

## Source and dependency boundary

`ScreenMenus/` is independently authored plugin code. No T3Marius menu implementation, source file, binary, assets or undocumented offsets are included. Public configuration/API names are retained for compatibility. No fake removed schema classes are provided.

References used to verify available APIs and the camera-mount approach:

- [CSS379 source](https://github.com/roflmuffin/CounterStrikeSharp/tree/f58e383): generated CPointOrient/CPointWorldText schemas, entity and callback APIs. CounterStrikeSharp's package declares GPL-3.0-only.
- [GameHUD camera-mount implementation](https://github.com/darkerz7/CS2-GameHUD/blob/a80689a3a77435f4495514adb3341a9bf188e39f/CS2-GameHUD/HUD.cs): a reference for the supported point_orient/point_worldtext approach, not copied source. Its repository has GPL-3.0 license text.
- [Legacy public menu contract](https://github.com/T3Marius/CS2ScreenMenuAPI/tree/c4aceda528a5146709a31645174304e60313c0ba) and [removed-type issue](https://github.com/T3Marius/CS2ScreenMenuAPI/issues/7).

Tomlyn 0.19.0 is explicitly referenced for TOML parsing, matching the CSS379 dependency. Existing repository authorship/credits are retained. No license is assigned to the entire preexisting repository by this migration, and no blanket legal clearance is claimed for its historical code. The unrelated local merge fragments and unknown-license old DLL are not included in this branch.
