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

## Evidence to retain

For a code change, record:

- the source build result;
- deployed mod and companion DLL hashes;
- the restarted GABS instance identity;
- fresh tool discovery showing all three named contracts;
- structured results from both no-tick contracts and the realtime contract; and
- relevant new errors from RimWorld's player log.

Treat source/build evidence and live-game evidence as separate claims. A clean
build proves the companion compiles; only a restarted, rediscovered contract run
proves the deployed pair behaves correctly in RimWorld.
