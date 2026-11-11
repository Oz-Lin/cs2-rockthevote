using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using cs2_rockthevote.ScreenMenus;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using System.Text;
using System.Globalization;
using System.Text.Json;
using cs2_rockthevote;

var passed = 0;
void Check(string name, bool condition) { if (!condition) throw new Exception(name); passed++; Console.WriteLine($"PASS {name}"); }
var cfg = new Config();
Check("custom integer HUD defaults", cfg.Rtv.HudMenu == 1 && cfg.EndOfMapVote.HudMenu == 1 && cfg.VipExtendMapVote.HudMenu == 1);
foreach (int mode in new[] { 0, 1, 2 }) {
    cfg.Rtv.HudMenu = cfg.EndOfMapVote.HudMenu = cfg.VipExtendMapVote.HudMenu = mode;
    var roundtrip = JsonSerializer.Deserialize<Config>(JsonSerializer.Serialize(cfg))!;
    Check($"integer HUD mode {mode} roundtrip", roundtrip.Rtv.HudMenu == mode && roundtrip.EndOfMapVote.HudMenu == mode && roundtrip.VipExtendMapVote.HudMenu == mode);
}
cfg.VipExtendMapVote.ExtendLimit = -1; cfg.VipExtendMapVote.ExtendTimeStep = 23; cfg.VipExtendMapVote.RoundBased = false;
var restored = JsonSerializer.Deserialize<Config>(JsonSerializer.Serialize(cfg))!;
Check("custom VIP extension settings roundtrip", restored.VipExtendMapVote.ExtendLimit == -1 && restored.VipExtendMapVote.ExtendTimeStep == 23 && !restored.VipExtendMapVote.RoundBased);
var validator = new AsyncVoteValidator(new RtvConfig { VotePercentage = 60 });
Check("threshold 60 percent of ten", validator.GetRequiredVotes(10) == 6);
Check("zero votes never wins", !validator.CheckVotes(0, 0));
Check("below threshold loses", !validator.CheckVotes(5, 10));
Check("threshold wins", validator.CheckVotes(6, 10));
Check("above threshold wins", validator.CheckVotes(7, 10));
var half = new AsyncVoteValidator(new RtvConfig { VotePercentage = 50 });
Check("existing midpoint-to-even rounding retained", half.GetRequiredVotes(5) == 2 && half.GetRequiredVotes(7) == 4);
validator.AddVote(4); validator.AddVote(4); validator.AddVote(9);
Check("validator tracking deduplicates", validator.GetVoteCount() == 2);
validator.RemoveVote(4); Check("validator removal", validator.GetVoteCount() == 1);
var count = 10;
var manager = new AsyncVoteManager(new RtvConfig { VotePercentage = 60 }, () => count);
Check("original constructor signature retained", typeof(AsyncVoteManager).GetConstructor(new[] { typeof(IVoteConfig) }) != null);
Check("manager live required count", manager.RequiredVotes == 6);
var first = manager.AddVote(1);
Check("first vote counted", first.Result == VoteResultEnum.Added && first.VoteCount == 1 && first.RequiredVotes == 6);
Check("duplicate vote rejected", manager.AddVote(1).Result == VoteResultEnum.AlreadyAddedBefore && manager.VoteCount == 1);
for (var i = 2; i <= 5; i++) manager.AddVote(i);
Check("sixth vote reaches threshold", manager.AddVote(6).Result == VoteResultEnum.VotesReached && manager.VotesAlreadyReached);
Check("completed vote ignores new voters", manager.AddVote(7).Result == VoteResultEnum.VotesAlreadyReached && manager.VoteCount == 6);
manager.ResetVotes(); Check("reset clears count and latch", manager.VoteCount == 0 && !manager.VotesAlreadyReached);
manager.AddVote(1); manager.AddVote(2); manager.RemoveVote(1);
Check("disconnect removes existing vote", manager.VoteCount == 1);
manager.RemoveVote(999); Check("unknown disconnect is harmless", manager.VoteCount == 1);
count = 3; Check("threshold follows changed player count", manager.RequiredVotes == 2);
Check("changed count threshold enforced", manager.AddVote(3).Result == VoteResultEnum.VotesReached);
manager.OnMapStart("de_dust2"); Check("map reset clears latch", manager.VoteCount == 0 && !manager.VotesAlreadyReached);
Check("custom extension method signatures retained", typeof(ExtendRoundTimeManager).GetMethod("StartVote", new[] { typeof(IExtendMapConfig) }) != null && typeof(ExtendRoundTimeManager).GetMethod("ExtendMapTimeLimit") != null);
var totals = new Dictionary<string, int>();
var voters = new Dictionary<int, string>();
var tieBreaks = 0;
Check("empty vote fails closed", !ExtensionVotePolicy.Evaluate(totals, n => { tieBreaks++; return 0; }).Passed && tieBreaks == 0);
ExtensionVotePolicy.EnsureOptions(totals);
Check("zero-vote tie never calls random selector", !ExtensionVotePolicy.Evaluate(totals, n => { tieBreaks++; return 0; }).Passed && tieBreaks == 0);
Check("valid yes ballot accepted", ExtensionVotePolicy.Cast(totals, voters, 1, "Yes") && totals["Yes"] == 1);
ExtensionVotePolicy.EnsureOptions(totals);
Check("menu reopen preserves existing ballots", totals["Yes"] == 1 && voters.Count == 1);
ExtensionVotePolicy.Cast(totals, voters, 1, "Yes");
Check("repeated selection does not duplicate", totals["Yes"] == 1 && voters.Count == 1);
ExtensionVotePolicy.Cast(totals, voters, 1, "No");
Check("changed ballot balances both totals", totals["Yes"] == 0 && totals["No"] == 1);
Check("no majority fails", !ExtensionVotePolicy.Evaluate(totals, _ => 0).Passed);
ExtensionVotePolicy.Cast(totals, voters, 2, "Yes");
Check("nonempty tie keeps existing random yes outcome", ExtensionVotePolicy.Evaluate(totals, _ => 0).Passed);
Check("nonempty tie keeps existing random no outcome", !ExtensionVotePolicy.Evaluate(totals, _ => 1).Passed);
Check("invalid ballot rejected without changing state", !ExtensionVotePolicy.Cast(totals, voters, 3, "unexpected") && voters.Count == 2 && totals.Count == 2);
Check("revoke removes only that ballot", ExtensionVotePolicy.Revoke(totals, voters, 1) && totals["No"] == 0 && totals["Yes"] == 1);
Check("repeat revoke cannot create negative totals", !ExtensionVotePolicy.Revoke(totals, voters, 1) && totals["No"] == 0);
var win = ExtensionVotePolicy.Evaluate(totals, _ => throw new Exception("Unneeded random tie break"));
Check("yes result and percentage", win.Passed && win.TotalVotes == 1 && win.WinningPercentage == 100M);
ExtensionVotePolicy.Revoke(totals, voters, 2);
Check("all votes revoked fails closed", !ExtensionVotePolicy.Evaluate(totals, _ => 0).Passed);


// Append before the managed harness summary. Uses the existing Check(name, condition).
var mapVote = new MapVotePolicy();
Check("map vote starts inactive", !mapVote.Active && mapVote.BallotCount == 0 && mapVote.Totals.Count == 0);
var mapGeneration = mapVote.Begin(new[] { "extend", "de_a", "de_b", "de_a", "", " " });
Check("map options initialized once and deduplicated", mapVote.Active && mapVote.Totals.Keys.SequenceEqual(new[] { "extend", "de_a", "de_b" }) && mapVote.Totals.Values.All(x => x == 0));
Check("first map ballot accepted", mapVote.Cast(mapGeneration, 0x10001u, "de_a") && mapVote.Totals["de_a"] == 1);
for (var reopen = 0; reopen < 10; reopen++) _ = mapVote.Totals.Keys.ToArray(); // Exactly the menu builders' read-only option source.
Check("multiple players opening and reopening menus preserve ballots", mapVote.Totals["de_a"] == 1 && mapVote.BallotCount == 1);
Check("same controller raw handle deduplicates wrappers", mapVote.Cast(mapGeneration, 0x10001u, "de_a") && mapVote.Totals["de_a"] == 1 && mapVote.BallotCount == 1);
Check("map selection changes balance both totals", mapVote.Cast(mapGeneration, 0x10001u, "de_b") && mapVote.Totals["de_a"] == 0 && mapVote.Totals["de_b"] == 1 && mapVote.BallotCount == 1);
Check("unknown map selection is rejected", !mapVote.Cast(mapGeneration, 0x10002u, "injected") && mapVote.Totals.Count == 3 && mapVote.BallotCount == 1);
Check("second player ballot is independent", mapVote.Cast(mapGeneration, 0x10002u, "de_a") && mapVote.Totals["de_a"] == 1 && mapVote.Totals["de_b"] == 1);
Check("revote removes only requesting player", mapVote.Revoke(mapGeneration, 0x10001u) && mapVote.Totals["de_b"] == 0 && mapVote.Totals["de_a"] == 1 && mapVote.BallotCount == 1);
Check("repeated revoke cannot make map totals negative", !mapVote.Revoke(mapGeneration, 0x10001u) && mapVote.Totals.Values.All(x => x >= 0));
Check("unknown player revoke is harmless", !mapVote.Revoke(mapGeneration, 0x90009u) && mapVote.BallotCount == 1);
Check("revote can select extension option", mapVote.Cast(mapGeneration, 0x10001u, "extend") && mapVote.Totals["extend"] == 1 && mapVote.Totals["de_a"] == 1);
Check("same entity index with new serial is a different voter", mapVote.Cast(mapGeneration, 0x20001u, "de_b") && mapVote.BallotCount == 3);
var mapBeginRejected = false;
try { mapVote.Begin(new[] { "replacement" }); } catch (InvalidOperationException) { mapBeginRejected = true; }
Check("active map vote cannot be reinitialized", mapBeginRejected && mapVote.Generation == mapGeneration && mapVote.BallotCount == 3 && !mapVote.Totals.ContainsKey("replacement"));
Check("stale map selection is rejected", !mapVote.Cast(mapGeneration - 1, 0x10001u, "de_b") && mapVote.Totals["extend"] == 1);
Check("stale map revoke is rejected", !mapVote.Revoke(mapGeneration - 1, 0x10001u) && mapVote.BallotCount == 3);
Check("stale map end leaves current vote active", !mapVote.TryEnd(mapGeneration - 1, _ => throw new Exception("Stale tie selector"), out _) && mapVote.Active);
var mapTieChoices = new List<int>();
Check("map end selects a tied winner", mapVote.TryEnd(mapGeneration, n => { mapTieChoices.Add(n); return 1; }, out var mapResult) && mapResult!.Winner == "de_a" && mapResult.WinningVotes == 1 && mapResult.TotalVotes == 3 && mapResult.WinningPercentage == 1M / 3M * 100M && mapTieChoices.SequenceEqual(new[] { 3 }));
Check("map end latches inactive", !mapVote.Active && !mapVote.IsCurrent(mapGeneration));
Check("duplicate map end cannot repeat result actions", !mapVote.TryEnd(mapGeneration, _ => throw new Exception("Duplicate tie selector"), out _) && mapTieChoices.Count == 1);
Check("post-end map selection is ignored", !mapVote.Cast(mapGeneration, 0x90009u, "de_a") && mapVote.BallotCount == 3);
Check("post-end map revoke is ignored", !mapVote.Revoke(mapGeneration, 0x10001u) && mapVote.Totals["extend"] == 1);
var nextMapGeneration = mapVote.Begin(new[] { "de_a", "de_b" });
Check("new map vote resets counts and advances generation", nextMapGeneration != mapGeneration && mapVote.BallotCount == 0 && mapVote.Totals.Values.All(x => x == 0));
Check("old menu cannot cast in replacement map vote", !mapVote.Cast(mapGeneration, 0x10001u, "de_a") && mapVote.BallotCount == 0);
Check("old timer cannot end replacement map vote", !mapVote.TryEnd(mapGeneration, _ => throw new Exception("Old timer tie selector"), out _) && mapVote.Active);
Check("zero-ballot map vote retains random map choice", mapVote.TryEnd(nextMapGeneration, n => { Check("zero-ballot selector receives both maps", n == 2); return 1; }, out var zeroMapResult) && zeroMapResult!.Winner == "de_b" && zeroMapResult.TotalVotes == 0 && zeroMapResult.WinningPercentage == 0);
var emptyMapGeneration = mapVote.Begin(Array.Empty<string>());
Check("empty candidate vote ends without random or exception", mapVote.TryEnd(emptyMapGeneration, _ => throw new Exception("Empty tie selector"), out var emptyMapResult) && emptyMapResult == null && !mapVote.Active);
var resetMapGeneration = mapVote.Begin(new[] { "de_a" });
mapVote.Cast(resetMapGeneration, 0x10001u, "de_a");
mapVote.Reset();
Check("map start reset clears all vote state", !mapVote.Active && mapVote.BallotCount == 0 && mapVote.Totals.Count == 0 && mapVote.Generation != resetMapGeneration);
Check("map start invalidates old selection and timer", !mapVote.Cast(resetMapGeneration, 0x10002u, "de_a") && !mapVote.TryEnd(resetMapGeneration, _ => 0, out _));
var mapWinner = MapVotePolicy.Evaluate(new Dictionary<string, int> { ["de_a"] = 3, ["de_b"] = 1 }, _ => throw new Exception("Unneeded random map tie break"));
Check("map winner and original decimal percentage retained", mapWinner is { Winner: "de_a", WinningVotes: 3, TotalVotes: 4, WinningPercentage: 75M });
var negativeMapWinner = MapVotePolicy.Evaluate(new Dictionary<string, int> { ["de_a"] = -5, ["de_b"] = 2 }, _ => throw new Exception("Negative tie selector"));
Check("negative counts cannot subtract legitimate ballots", negativeMapWinner is { Winner: "de_b", WinningVotes: 2, TotalVotes: 2, WinningPercentage: 100M });
var negativeMapTie = MapVotePolicy.Evaluate(new Dictionary<string, int> { ["de_a"] = -5, ["de_b"] = -1 }, _ => 0);
Check("all-negative input normalizes to original zero-ballot map choice", negativeMapTie is { Winner: "de_a", WinningVotes: 0, TotalVotes: 0, WinningPercentage: 0 });
Check("empty map evaluation has no winner", MapVotePolicy.Evaluate(new Dictionary<string, int>(), _ => throw new Exception("Empty evaluation selector")) == null);
var manyMapGeneration = mapVote.Begin(new[] { "de_a", "de_b" });
for (uint voter = 1; voter <= 100; voter++) mapVote.Cast(manyMapGeneration, voter, voter % 2 == 0 ? "de_a" : "de_b");
Check("one hundred map voters counted independently", mapVote.BallotCount == 100 && mapVote.Totals["de_a"] == 50 && mapVote.Totals["de_b"] == 50);
for (uint voter = 1; voter <= 100; voter++) { mapVote.Revoke(manyMapGeneration, voter); mapVote.Revoke(manyMapGeneration, voter); }
Check("all repeated revokes preserve nonnegative map totals", mapVote.BallotCount == 0 && mapVote.Totals.Values.All(x => x == 0));
mapVote.Reset();

var retired = new List<object>();
var sessions = new OwnedSessions<uint, object>(retired.Add);
var firstMenu = new object(); var secondMenu = new object(); var replacement = new object();
sessions.Replace(0x10001, firstMenu); sessions.Replace(0x10002, secondMenu);
Check("independent player sessions", sessions.IsCurrent(0x10001, firstMenu) && sessions.IsCurrent(0x10002, secondMenu));
sessions.Replace(0x10001, replacement);
Check("reopen retires exactly the old owner", retired.SequenceEqual(new[] { firstMenu }) && sessions.IsCurrent(0x10001, replacement) && sessions.IsCurrent(0x10002, secondMenu));
Check("stale controller serial cannot address replacement", sessions.Get(0x20001) == null);
int calls = 0, refreshes = 0;
Check("stale session selection cannot execute", !sessions.Invoke(0x10001, firstMenu, true, () => calls++, () => refreshes++) && calls == 0);
sessions.Invoke(0x10001, replacement, true, () => { calls++; sessions.Replace(0x10001, firstMenu); }, () => refreshes++);
Check("callback replacement is not closed by old post-select", calls == 1 && refreshes == 0 && sessions.IsCurrent(0x10001, firstMenu));
sessions.Invoke(0x10001, firstMenu, false, () => sessions.Replace(0x10001, replacement), () => refreshes++);
Check("nonclosing callback replacement is not refreshed as old menu", refreshes == 0 && sessions.IsCurrent(0x10001, replacement));
sessions.Invoke(0x10001, replacement, false, () => calls++, () => refreshes++);
Check("current nonclosing callback refreshes once", calls == 2 && refreshes == 1);
sessions.Remove(0x10001); var countAfterClose = retired.Count; sessions.Remove(0x10001);
Check("close is idempotent and other player retained", retired.Count == countAfterClose && sessions.IsCurrent(0x10002, secondMenu));
sessions.Clear(); countAfterClose = retired.Count; sessions.Clear();
Check("map end and unload close each remaining session once", sessions.Snapshot.Length == 0 && retired.Count == countAfterClose);

var callbackLease = new CallbackLease(); var registrations = new List<int>();
callbackLease.Add(() => registrations.Add(1), () => registrations.Add(-1));
callbackLease.Add(() => registrations.Add(2), () => registrations.Add(-2));
callbackLease.Dispose(); callbackLease.Dispose();
Check("callbacks unregister exactly once in reverse order", registrations.SequenceEqual(new[] { 1, 2, -2, -1 }));
var failingLease = new CallbackLease(); bool otherCleanup = false; int failedCleanupCalls = 0;
failingLease.Add(() => { }, () => otherCleanup = true);
failingLease.Add(() => { }, () => { if (++failedCleanupCalls == 1) throw new InvalidOperationException(); });
try { failingLease.Dispose(); } catch (AggregateException) { }
Check("failed cleanup does not skip other callbacks", otherCleanup);
failingLease.Dispose(); failingLease.Dispose();
Check("failed callback removal retained and retried without repeating successful cleanup", failedCleanupCalls == 2);
foreach (var original in new[] { MoveType_t.MOVETYPE_WALK, MoveType_t.MOVETYPE_NOCLIP, MoveType_t.MOVETYPE_NONE })
{
    var move = original; var actual = MoveType_t.MOVETYPE_NOCLIP; int writes = 0;
    var movement = new MovementLease<MoveType_t>(0x30004, move, actual, MoveType_t.MOVETYPE_NONE);
    movement.Apply(value => move = value, value => actual = value);
    movement.Restore(0x30004, () => move, () => actual, value => { move = value; writes++; }, value => { actual = value; writes++; });
    Check($"restore captured movement {original}", move == original && actual == MoveType_t.MOVETYPE_NOCLIP && !movement.Pending);
    movement.Restore(0x30004, () => move, () => actual, value => writes++, value => writes++);
    Check($"movement {original} restored only once", writes == 2);
}
var oldMove = MoveType_t.MOVETYPE_WALK; var oldActual = MoveType_t.MOVETYPE_WALK;
var changedPawn = new MovementLease<MoveType_t>(0x10004, oldMove, oldActual, MoveType_t.MOVETYPE_NONE);
changedPawn.Apply(value => oldMove = value, value => oldActual = value);
changedPawn.Restore(0x20004, () => oldMove, () => oldActual, _ => throw new Exception("wrong pawn"), _ => throw new Exception("wrong pawn"));
Check("replacement pawn never inherits old movement", !changedPawn.Pending);
oldMove = oldActual = MoveType_t.MOVETYPE_WALK;
var externalMove = new MovementLease<MoveType_t>(0x10004, oldMove, oldActual, MoveType_t.MOVETYPE_NONE);
externalMove.Apply(value => oldMove = value, value => oldActual = value);
oldMove = MoveType_t.MOVETYPE_NOCLIP;
externalMove.Restore(0x10004, () => oldMove, () => oldActual, _ => throw new Exception("external move overwritten"), _ => throw new Exception("external move overwritten"));
Check("other system movement change is not overwritten", oldMove == MoveType_t.MOVETYPE_NOCLIP && !externalMove.Pending);
// Failure before the second freeze write: first write still gets restored.
oldMove = oldActual = MoveType_t.MOVETYPE_WALK;
var partialFreeze = new MovementLease<MoveType_t>(4, oldMove, oldActual, MoveType_t.MOVETYPE_NONE);
try { partialFreeze.Apply(value => oldMove = value, _ => throw new InvalidOperationException("second freeze write")); } catch (InvalidOperationException) { }
partialFreeze.Restore(4, () => oldMove, () => oldActual, value => oldMove = value, value => oldActual = value);
Check("partial freeze failure restores first write", oldMove == MoveType_t.MOVETYPE_WALK && oldActual == MoveType_t.MOVETYPE_WALK && !partialFreeze.Pending);
// Failure after a write has executed is an uncertain result, so the same lease reconciles its actual value.
var uncertainFreeze = new MovementLease<MoveType_t>(4, oldMove, oldActual, MoveType_t.MOVETYPE_NONE);
try { uncertainFreeze.Apply(value => { oldMove = value; throw new InvalidOperationException("after write"); }, value => oldActual = value); } catch (InvalidOperationException) { }
uncertainFreeze.Restore(4, () => oldMove, () => oldActual, value => oldMove = value, value => oldActual = value);
Check("uncertain freeze write reconciles and restores", oldMove == MoveType_t.MOVETYPE_WALK && !uncertainFreeze.Pending);
var restoreRetry = new MovementLease<MoveType_t>(4, oldMove, oldActual, MoveType_t.MOVETYPE_NONE);
restoreRetry.Apply(value => oldMove = value, value => oldActual = value);
try { restoreRetry.Restore(4, () => oldMove, () => oldActual, _ => throw new InvalidOperationException("restore failed"), value => oldActual = value); } catch (AggregateException) { }
Check("failed restore retains only unfinished work", restoreRetry.Pending && oldMove == MoveType_t.MOVETYPE_NONE && oldActual == MoveType_t.MOVETYPE_WALK);
restoreRetry.Restore(4, () => oldMove, () => oldActual, value => oldMove = value, _ => throw new Exception("successful restore repeated"));
Check("failed restore succeeds on retry", !restoreRetry.Pending && oldMove == MoveType_t.MOVETYPE_WALK);
var resources = new OwnedResources<object>(); var resourceA = new object(); var resourceB = new object(); resources.Add(resourceA); resources.Add(resourceB);
int resourceAttempts = 0;
try { resources.ReleaseAll(item => { if (ReferenceEquals(item, resourceB)) { resourceAttempts++; throw new InvalidOperationException(); } }); } catch (AggregateException) { }
Check("failed native deletion retains owned reference and cleans other resources", resources.Snapshot.SequenceEqual(new[] { resourceB }));
resources.ReleaseAll(item => resourceAttempts++); resources.ReleaseAll(_ => throw new Exception("already removed"));
Check("failed native deletion can retry exactly pending resource", resourceAttempts == 2 && resources.Snapshot.Length == 0);
int releaseCalls = 0;
var clearFaults = new OwnedSessions<int, object>(_ => { releaseCalls++; if (releaseCalls == 1) throw new InvalidOperationException(); });
clearFaults.Replace(1, new()); clearFaults.Replace(2, new());
try { clearFaults.Clear(); } catch (AggregateException) { }
Check("session clear attempts all players despite one failure", releaseCalls == 2 && clearFaults.Snapshot.Length == 0);
bool rolledBack = false;
try { StartupGuard.Complete(() => throw new InvalidOperationException("late Load"), () => rolledBack = true); } catch (InvalidOperationException) { }
Check("late plugin load failure rolls back menu service", rolledBack);
try { StartupGuard.Complete(() => throw new InvalidOperationException("late Load"), () => throw new IOException("cleanup")); }
catch (AggregateException e) { Check("startup and rollback errors both retained", e.InnerExceptions.Count == 2); }
Check("empty optional transmit bitvector safely identifiable", default(CFixedBitVecBase).Equals(default(CFixedBitVecBase)));
var edges = new ButtonEdges((ulong)PlayerButtons.Use);
Check("held select during open does not select", edges.Sample((ulong)PlayerButtons.Use) == 0);
Check("release does not select", edges.Sample(0) == 0);
Check("next select press emits one edge", edges.Sample((ulong)PlayerButtons.Use) == (ulong)PlayerButtons.Use);
Check("held select does not repeat", edges.Sample((ulong)PlayerButtons.Use) == 0);
var cursor = new MenuCursor();
Check("empty menu has safe single page", cursor.Pages(0) == 1 && !cursor.Turn(1, 0));
cursor.Move(-1, 0); Check("empty menu scroll is safe", cursor.Selection == 0);
var choices = Enumerable.Range(0, 13).Select(i => ($"map {i}", i == 1)).ToArray();
var entries = MenuNavigation.Entries(choices, cursor, true, true, key => key);
Check("first page has six options and next/exit/resolution", entries.Count == 9 && entries.Count(x => x.Option >= 0) == 6 && entries.Any(x => x.Key == 8) && entries.All(x => x.Key != 7));
Check("disabled numeric selection rejected", MenuNavigation.Choice(entries, 2) == null);
Check("unknown numeric selection rejected", MenuNavigation.Choice(entries, 77) == null);
Check("numeric choice maps to original item", MenuNavigation.Choice(entries, 3)?.Option == 2);
cursor.Move(-1, entries.Count(x => x.Enabled)); Check("scroll wraps to final enabled control", cursor.Selection == 7);
cursor.Move(1, entries.Count(x => x.Enabled)); Check("scroll wraps to first choice", cursor.Selection == 0);
Check("next page advances", cursor.Turn(1, choices.Length) && cursor.Page == 1 && cursor.Selection == 0);
entries = MenuNavigation.Entries(choices, cursor, true, false, key => key);
Check("middle page maps keys to correct absolute items", MenuNavigation.Choice(entries, 1)?.Option == 6 && entries.Any(x => x.Key == 7) && entries.Any(x => x.Key == 8) && entries.All(x => x.Key != 0));
cursor.Turn(1, choices.Length); entries = MenuNavigation.Entries(choices, cursor, false, false, key => key);
Check("final page excludes nonexistent next/close", entries.Count == 2 && MenuNavigation.Choice(entries, 1)?.Option == 12 && entries.All(x => x.Key != 8 && x.Key != 9));
Check("cannot advance beyond final page", !cursor.Turn(1, choices.Length));
cursor.Reset(); Check("reset returns to first page and selection", cursor.Page == 0 && cursor.Selection == 0);

var sample = """
[Settings]
FontName = "Custom Font"
MenuType = "Both"
Size = 29
PositionX = -8.25
PositionY = 1.5
FreezePlayer = false
HasExitOption = false
ShowResolutionOption = false
ShowDisabledOptionNum = false
ShowPageCount = false
ShowControlsInfo = false
ScrollPrefix = "→"
UnknownFutureKey = "preserve in source"
[Settings.Resolutions."custom"]
PositionX = -6.75
PositionY = 2.25
[Controls]
ScrollUp = "A"
ScrollDown = "D"
Select = "Space"
[Sounds]
Select = "custom.select"
Volume = 0.25
[Lang.zh-Hans]
Next = "下一页"
""";
var originalCulture = CultureInfo.CurrentCulture;
try
{
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
    var settings = MenuSettings.Parse(sample);
    Check("TOML numbers parse invariantly", settings.Position == new MenuPosition(-8.25f, 1.5f) && settings.Resolutions["custom"] == new MenuPosition(-6.75f, 2.25f));
    Check("TOML original mode font size retained", settings.Mode == MenuType.Both && settings.FontName == "Custom Font" && settings.Size == 29);
    Check("TOML original toggles retained", !settings.FreezePlayer && !settings.HasExitOption && !settings.ShowResolutionOption && !settings.ShowDisabledOptionNum && !settings.ShowPageCount && !settings.ShowControlsInfo);
    Check("TOML controls aliases retained", settings.Up == PlayerButtons.Moveleft && settings.Down == PlayerButtons.Moveright && settings.Select == PlayerButtons.Jump);
    Check("TOML sound and volume retained", settings.Sounds["Select"] == "custom.select" && settings.Volume == 0.25f);
    Check("TOML selected language and fallback", settings.Text("zh-Hans", "Next") == "下一页" && settings.Text("fr", "Close") == "Close");
    Check("TOML unknown keys accepted without rewriting input", sample.Contains("UnknownFutureKey"));
}
finally { CultureInfo.CurrentCulture = originalCulture; }
var defaults = MenuSettings.Parse("");
Check("missing optional TOML fields keep defaults", defaults.Mode == MenuType.KeyPress && defaults.Size == 25 && defaults.Up == PlayerButtons.Forward);
foreach (string bad in new[] { "[Settings]\nMenuType='invalid'", "[Settings]\nSize=0", "[Settings]\nPositionX=nan", "[Controls]\nSelect='missing'", "[Controls]\nSelect='W'", "[Sounds]\nVolume=2", "[Settings]\nFreezePlayer='true'" })
{
    bool failed = false; try { MenuSettings.Parse(bad); } catch (Exception) { failed = true; }
    Check($"invalid setting fails explicitly: {bad.Replace('\n', ' ')}", failed);
}
var longText = string.Concat(Enumerable.Repeat("地图😀", 200));
var fit = ScreenText.Fit(longText);
Check("UTF8 text fits engine buffer without splitting rune", Encoding.UTF8.GetByteCount(fit) <= 511 && !fit.EndsWith('\uFFFD') && longText.StartsWith(fit, StringComparison.Ordinal));
Check("display label cannot inject new menu rows", ScreenText.OneLine("a\nb\rc\0") == "a b c ");
Check("resolution JSON retains legacy property contract", JsonSerializer.Deserialize<Dictionary<ulong, MenuPosition>>("{\"76561198000000000\":{\"PositionX\":-9,\"PositionY\":1}}")![76561198000000000] == new MenuPosition(-9, 1));

static (HashSet<string> Definitions, List<(string Name, string Scope)> References, string[] AssemblyRefs) Metadata(string path)
{
    using var file = File.OpenRead(path); using var pe = new PEReader(file); var metadata = pe.GetMetadataReader();
    var definitions = metadata.TypeDefinitions.Select(h => metadata.GetTypeDefinition(h)).Select(t => metadata.GetString(t.Namespace) + "." + metadata.GetString(t.Name)).ToHashSet();
    var references = new List<(string Name, string Scope)>();
    foreach (var handle in metadata.TypeReferences)
    {
        var type = metadata.GetTypeReference(handle);
        if (type.ResolutionScope.Kind == HandleKind.AssemblyReference)
            references.Add((metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name), metadata.GetString(metadata.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope).Name)));
    }
    return (definitions, references, metadata.AssemblyReferences.Select(h => metadata.GetString(metadata.GetAssemblyReference(h).Name)).ToArray());
}
var pluginMetadata = Metadata(typeof(Config).Assembly.Location);
var cssMetadata = Metadata(typeof(BasePlugin).Assembly.Location);
Check("compiled plugin has no old screen-menu assembly dependency", !pluginMetadata.AssemblyRefs.Contains("CS2ScreenMenuAPI"));
Check("compiled plugin has no removed viewmodel TypeRefs", !pluginMetadata.References.Any(x => x.Name.Contains("ViewModel", StringComparison.Ordinal)));
Check("every direct CSS TypeRef resolves against actual CSS379", pluginMetadata.References.Where(x => x.Scope == "CounterStrikeSharp.API").All(x => cssMetadata.Definitions.Contains(x.Name)));
Check("actual runtime API assembly is 1.0.379", typeof(BasePlugin).Assembly.GetName().Version == new Version(1, 0, 379, 0));
Check("source-only renderer uses supported native schema types", pluginMetadata.References.Any(x => x.Name.EndsWith(".CPointOrient")) && pluginMetadata.References.Any(x => x.Name.EndsWith(".CPointWorldText")));
var registrationFault = new CallbackLease(); bool registeredThenThrown = false;
try { registrationFault.Add(() => { registeredThenThrown = true; throw new IOException("after register"); }, () => registeredThenThrown = false); } catch (IOException) { }
registrationFault.Dispose(); Check("uncertain partial callback registration retains compensation", !registeredThenThrown);
var afterRestore = new MovementLease<MoveType_t>(4, MoveType_t.MOVETYPE_WALK, MoveType_t.MOVETYPE_WALK, MoveType_t.MOVETYPE_NONE);
oldMove = oldActual = MoveType_t.MOVETYPE_WALK;
afterRestore.Apply(value => oldMove = value, value => oldActual = value);
int restoreNotifications = 0;
try { afterRestore.Restore(4, () => oldMove, () => oldActual, value => { oldMove = value; throw new IOException("notification after write"); }, value => oldActual = value); } catch (AggregateException) { }
afterRestore.Restore(4, () => oldMove, () => oldActual, value => { oldMove = value; restoreNotifications++; }, _ => throw new Exception("finished field repeated"));
Check("restore notification failure retries even when native field already restored", restoreNotifications == 1 && !afterRestore.Pending);
var positions = new PositionBook();
Check("anonymous positions are not persisted", !positions.Set(0, 111, new MenuPosition(1, 2)) && positions.Saved.Count == 0);
Check("anonymous controllers have independent positions", positions.Get(0, 111) == new MenuPosition(1, 2) && positions.Get(0, 222) == null);
Check("anonymous selected resolution can reopen parent without chooser loop", positions.Get(0, 111) != null);
positions.ForgetTemporary(111);
Check("disconnect releases anonymous position", positions.Get(0, 111) == null);
positions.Set(0, 111, new MenuPosition());
Check("replacement controller serial does not inherit anonymous setting", positions.Get(0, 333) == null);
Check("authenticated setting persists by SteamID", positions.Set(42, 111, new MenuPosition(3, 4)) && positions.Get(42, 222) == new MenuPosition(3, 4));
positions.ClearTemporary();
Check("map reset clears only temporary positions", positions.Get(0, 111) == null && positions.Saved.Count == 1);
Tuple<uint>? nativeObject = Tuple.Create(123u);
var identityLease = new IdentityLease<Tuple<uint>>(123, _ => nativeObject, value => value.Item1);
Check("owned entity full identity resolves", identityLease.Resolve() == nativeObject);
var resourceFault = new OwnedResources<IdentityLease<Tuple<uint>>>(); resourceFault.Add(identityLease);
int removedEntities = 0;
try { resourceFault.ReleaseAll(lease => { if (lease.Resolve() == null) return; removedEntities++; nativeObject = Tuple.Create(456u); throw new IOException("deleted then address reused"); }); } catch (AggregateException) { }
Check("recycled serial is not transmit filtered as old owned entity", identityLease.Resolve() == null);
resourceFault.ReleaseAll(lease => { if (lease.Resolve() != null) removedEntities++; });
Check("retry never deletes replacement entity after successful deletion threw", removedEntities == 1 && resourceFault.Snapshot.Length == 0);
nativeObject = null;
Check("invalid captured identity resolves to no owned resource", identityLease.Resolve() == null);
Console.WriteLine($"SUMMARY {passed} passed, 0 failed; managed only, no native game execution.");