# Achtung Live Testing

Achtung uses RimBridgeServer for named test contracts inside a real RimWorld
process and GABS for process lifecycle and bridge discovery. The workflow keeps
the normal mod DLL and the companion DLL paired, restarts the game when either
changes, and returns structured evidence from repeatable scenarios.

## Build and deployment

Build without deployment, even when your shell normally exports
`RIMWORLD_MOD_DIR`:

```bash
env -u RIMWORLD_MOD_DIR ./scripts/build-quiet.sh -c Release
```

Deploy to a stopped RimWorld installation:

```bash
./scripts/build-quiet.sh -c Release \
  -p:RIMWORLD_MOD_DIR="/path/to/RimWorld/RimWorldMac.app/Mods"
```

The main project automatically restores and builds
`Source/BridgeTools/Achtung.BridgeTools.csproj`. A deploy is one unit:

- `RIMWORLD_MOD_DIR/Achtung/1.6/Assemblies/Achtung.dll`
- `RIMWORLD_MOD_DIR/../BridgeTools/Achtung/Achtung.BridgeTools.dll`

The deploy guard rejects an invalid UserData alias and serializes destructive
copies to a physical Mods directory across repository clones and worktrees.
Both `scripts/build-quiet.sh` and the locked deploy path refuse a deploy while
RimWorld is running. Stop the game through GABS before deploying.

Companion DLL changes are not hot-reloaded. Restart RimWorld after every deploy
that changes either DLL, then reconnect and rediscover tools. Do not use or edit
`bridge.json` as a recovery mechanism and do not change GABS configuration for
an Achtung test run.

## Runtime mod configuration

The mixed-draft mech contracts require this minimal active set:

- `brrainz.harmony`
- the installed RimBridgeServer package
- `ludeon.rimworld`
- `ludeon.rimworld.biotech`
- `brrainz.achtung`

Enable the Biotech DLC and Achtung through RimBridgeServer's mod tools or
RimWorld's normal mod configuration, then restart. Other DLCs are not required
for this contract. Keep unrelated gameplay mods out of the baseline unless the
scenario is explicitly testing an interaction with them.

For Multiplayer compatibility tests, use this active order:

- `zetrith.prepatcher`
- `brrainz.harmony`
- the installed RimBridgeServer package
- `ludeon.rimworld`
- `ludeon.rimworld.biotech`
- `rwmt.multiplayer`
- `brrainz.achtung`

Create the test save with that exact list before hosting. Loading an older
singleplayer fixture with Multiplayer added is useful for migration testing,
but it is not a clean synchronization baseline. The compatibility project and
packaged API dependency are 1.6-only; do not edit the frozen 1.1 through 1.5
release directories while maintaining this integration.

## GABS lifecycle

Use the normal GABS lifecycle rather than launching a second game process:

1. Inspect the configured games and current ownership with `games_list` and
   `games_status`.
2. If RimWorld is running, stop it with `games_stop` and wait until its state is
   stopped.
3. Build and deploy the paired DLLs.
4. Start RimWorld with `games_start`, connect with `games_connect`, and wait for
   the bridge to become ready. Start a debug colony with
   `rimworld/start_debug_game_ready` at `visual` readiness before realtime
   contracts so loading overlays do not force-pause the simulation.
5. Discover tools again after every restart before calling an Achtung contract.

If start or reconnect fails, stop and restart the related game and bridge
processes. Do not rewrite GABS configuration to work around a stale process.

## Contract layers

Use the least expensive layer that answers the question:

1. Read source and inspect RimWorld assemblies when the relevant API behavior is
   unclear.
2. Run the no-tick contract for deterministic command eligibility and state
   transition checks.
3. Run the asynchronous scenario for actual jobs, pathing, and tick-driven
   behavior.
4. Use screenshots or manual interaction only for behavior that depends on
   visual UI state.

## Conditional localization release gate

Releases run `scripts/check-settings-layout-release-gate.py` before creating a
tag. The gate compares the release tree with the previous semantic release tag
and has exactly two outcomes:

- when no `Languages/*/Keyed/*.xml` wording changed, it reports `skipped` and
  requires no RimWorld localization matrix;
- when keyed wording changed, it requires `TestEvidence/SettingsLayout.json`
  to contain a passing, current live matrix for every shipped language.

The evidence digest covers all keyed language XML plus the settings renderer
and companion audit sources. Any wording or relevant layout/audit change makes
old evidence stale. Inspect the trigger and current digest with:

```bash
python3 scripts/check-settings-layout-release-gate.py --print-input-digest
```

The release script never estimates text dimensions. All font selection,
translation lookup, width measurement, and fit assertions come from the
companion-only `achtung/audit_settings_layout` Bridge tool; the script only
decides whether wording changed and rejects missing, stale, or failing Bridge
evidence.

When the matrix is required, deploy the paired Release build, start RimWorld
through GABS, and automate this sequence for every language directory:

1. call `rimworld/switch_language` with the installed language's recommended
   query;
2. wait for `rimbridge/wait_for_long_event_idle`;
3. call `achtung/audit_settings_layout` with that language as
   `expectedLanguage`;
4. require all 33 shared title/value states and eight fixed-height texts to
   pass.

Replace the single curated evidence file with the resulting 12-language
summary and the digest printed above, then run:

```bash
python3 scripts/check-settings-layout-release-gate.py
```

The audit uses RimWorld's active-language fonts and the proven logical widths:
399 pixels for shared title/value rows with a 12-pixel minimum gap, 423 pixels
for Medium-font column headers, and 387 pixels for Small-font subheadings.
Checkbox titles and explanations are deliberately excluded because their
measured height expands the scrollable column. No per-language dialog opening,
scrolling, or option cycling is required. Restore the original language and
stop RimWorld after recording evidence.

### Mixed draft menu contract

`achtung/test_mixed_draft_menu_contract` creates a temporary mechanitor, a real
Biotech centipede blaster, and a real cleansweeper. It snapshots the centipede
as drafted and the cleansweeper as undrafted, finds a plain Go-here cell, and
invokes Achtung's `Controller.ShowMenu` fallback and initial positioning path
without advancing time.

The contract passes only when:

- the originally drafted centipede is sufficient for `EveryoneHasGoto`;
- the implicit Go-here callback runs;
- positioning starts with both line endpoints on the clicked cell;
- the centipede receives a valid destination preview;
- the cleansweeper receives no destination preview;
- the centipede remains drafted; and
- the selected cleansweeper remains undrafted.

It also checks the adjacent eligibility matrix: an all-drafted selection still
qualifies, while an all-undrafted selection does not enter implicit positioning.

### Mixed draft modifier contract

`achtung/test_mixed_draft_modifier_contract` selects the same two temporary
mechs and calls Achtung's real `Controller.MouseDown` path twice. The first call
simulates no modifier and must position only the drafted centipede. The second
call simulates the configured Achtung modifier and must draft and position both
mechs.

GABS cannot make `Input.GetKey` report a held modifier merely by adding a Unity
event modifier. The contract therefore applies callback-scoped Harmony prefixes
to `Input.GetKey(KeyCode)` and `UI.MouseMapPosition()`. They force a deterministic
Alt-key state and target cell only while one serialized main-thread callback is
running. The tool removes both patches, restores the user's selection and
settings, and resets the fixture draft state before returning. No tick or frame
boundary occurs while either shim is installed.

### Mixed draft realtime scenario

`achtung/run_mixed_draft_mech_scenario` uses the same fixture but dispatches
Achtung's real line-order path. It then calls `IRimBridgeContext.Game.RunUntilAsync`
and observes the live game until the centipede has moved, the labor mech has
incorrectly reacted, or the game-tick deadline is reached.

The scenario passes only when the centipede moves under a player-forced GoTo
order while the cleansweeper is never drafted and never receives that target as
a player-forced GoTo job. The tool restores the previous game speed and Achtung
setting and removes all fixture pawns in a `finally` path.

These contracts deliberately test the behavioral boundary instead of the
Mech tab's implementation. The tab establishes draft state; Achtung must then
respect that state when a mixed map selection receives an implicit move.

### Forced-work neighbour propagation contract

`achtung/test_force_work_spread_at_cell` is a companion-only contract for the
force-work path. Prepare a connected line of buildable items through normal
Multiplayer-synchronized gameplay (for example, 12 or more wooden wall
blueprints), select a capable pawn with no existing Achtung prioritized work,
and call the contract on a blueprint near the middle of the line.

The companion resolves the same `ForcedFloatMenuOption` that the context menu
would display and calls `MultiplayerSupport.ForceWork`, which is the exact
synchronized command invoked when the lightning-button drag completes. It does
not call `ForcedMultiFloatMenuOption.ApplyForceWork`, mutate a `ForcedJob`, or
expand targets directly. After Multiplayer executes the command, the contract
advances deterministic ticks in 15-tick samples and records the complete cell
set used by Achtung's force-marker renderer.

The contract passes only when:

- Multiplayer executes the synchronized command and creates the forced job;
- the peak target count grows beyond the initial target set;
- at least one newly added marker cell neighbours the prior marker set; and
- the peak reaches `expectedMinimumTargets` (six by default).

The tool deliberately leaves the forced job active so it never clears
simulation state through an unsynchronized test-only path. Clear it with the
normal in-game `Clear prioritized work` command after collecting evidence.

### Forced-target save/load contract

`achtung/test_forced_target_save_load` creates one temporary prioritized-work
target through `ForcedWork.AddForcedJob`, saves the complete game with
`rimworld/save_game`, reloads it to playable readiness with
`rimworld/load_game_ready`, and reads the target back from the newly loaded
`ForcedWork` world component. It accepts `targetKind=blueprint`,
`targetKind=frame`, `targetKind=thing`, and `targetKind=cell`.

The blueprint contract passes only when the same player pawn and spawned wall
blueprint are resolved by their stable load IDs, the forced job still owns
exactly one valid Thing target at the original cell, and the saved material
score, workgiver, job kind, and last-assigned cell all match. The blueprint must
also exist independently in the loaded map, which distinguishes a lost
`LocalTargetInfo` cross-reference from a missing map object.

The frame case repeats that contract with a spawned wooden wall frame and
`ConstructDeliverResourcesToFrames`, proving that the fix covers the next
construction phase rather than only blueprint subclasses.

The ordinary Thing case uses a spawned steel item with `HaulGeneral`. It must
retain the exact item reference while preserving the legitimate default
material score of zero, covering non-construction targets and default-valued
serialized metadata.

The cell-only case uses a valid empty map cell with `CleanFilth`. It must reload
as a non-Thing job with the same cell and no phantom Thing ID. This is the
opposite side of the cross-reference boundary: `LoadingVars` recognizes the
existing coordinate representation directly, so it neither registers a null
Thing wanted ID nor replaces the complete cell during `ResolvingCrossRefs`.

Run this contract only in a paused singleplayer game. An installed but inactive
Multiplayer mod is allowed and exercises the compatibility fallback; a hosted
Multiplayer session is rejected because fixture setup and cleanup deliberately
mutate local state. The tool uses a unique save name and removes the forced job,
temporary target, and generated save in its `finally` path. It also brackets the
run with `rimbridge/list_logs` and fails when RimWorld reports unconsumed target
load IDs, even if the visible target state otherwise looks correct.

## Hosted Multiplayer pass

Presence in the mod list is not enough to test synchronization. Start a fresh
debug colony with the Multiplayer test list active, save it, open RimWorld's
in-game menu, choose `Host a server`, and start the host. Confirm the Multiplayer
session indicator is visible before calling an Achtung contract.

Run `achtung/run_mixed_draft_mech_scenario` after the host has unpaused. Then
prepare a long wall blueprint line through normal gameplay and run
`achtung/test_force_work_spread_at_cell` on its center. A valid hosted result
advances game ticks, assigns the drafted combat mech a player-forced `Goto`
without affecting the labor mech, and grows the wall job's marker cells through
neighbouring blueprints. Also inspect the post-contract log journal: successful
structured output is not enough if Multiplayer logged a map-command exception.

Multiplayer's current RimWorld 1.6 macOS build may fail inside its own native
deferred stack-trace walker while ordinary pawns and animals tick. If that
happens before any Achtung command, repeat the isolation run with the host's
`Desync traces` option disabled and record the upstream failure separately.
Achtung suppresses that optional collector only while its synchronized job
commands allocate deterministic IDs; it does not disable Multiplayer's state
synchronization.

## Evidence to retain

For a code change, record:

- the source build result;
- deployed mod and companion DLL hashes;
- the restarted GABS instance identity;
- fresh tool discovery showing all four named contracts;
- structured results from both no-tick contracts and the realtime contract; and
- relevant new errors from RimWorld's player log.

Treat source/build evidence and live-game evidence as separate claims. A clean
build proves the companion compiles; only a restarted, rediscovered contract run
proves the deployed pair behaves correctly in RimWorld.
