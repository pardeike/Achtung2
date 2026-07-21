# Achtung!

Achtung! is a RimWorld control mod focused on better colonist commanding: force work, line formations, multi-colonist orders, and lower-friction moment-to-moment control.

## Install

### Easiest

1. Install Harmony.
2. Download the latest Achtung! release from:
   `https://github.com/pardeike/Achtung2/releases/latest`
3. Unzip the `Achtung` folder into your RimWorld `Mods` directory.
4. Enable `Harmony` and `Achtung!` in RimWorld.

Steam Workshop is also available:
`https://steamcommunity.com/sharedfiles/filedetails/?id=730936602`

## Requirements

- RimWorld 1.6
- Harmony (`brrainz.harmony`)

The repository still carries compatibility assets for older RimWorld versions, but the current release and test workflow target RimWorld 1.6.

## Build From Source

```bash
git clone https://github.com/pardeike/Achtung2.git
cd Achtung2
env -u RIMWORLD_MOD_DIR ./scripts/build-quiet.sh -c Release
./scripts/build-quiet.sh -c Release -p:RIMWORLD_MOD_DIR=/path/to/RimWorld/Mods
```

The first command builds without deploying. The second deploys the mod and its
RimBridge companion as one paired unit. A deploy build refuses to overwrite a
running RimWorld process. See [TESTING.md](TESTING.md) for the live test workflow.

## What The Mod Adds

- Force-work commands that keep important jobs moving
- Better multi-colonist command handling
- Formation and line movement tools
- Smarter drafting and undrafting behavior around issued commands
- Utility commands such as room cleaning, firefighting, and sowing workflows

## Live Developer Workflow

The reproducible mod-debugging stack is:

1. `Achtung2` for the mod under test
2. `RimBridgeServer` for live RimWorld inspection and control
3. `GABS` for starting RimWorld and exposing the bridge tools cleanly to an AI client
4. `DecompilerServer` for reading RimWorld's managed assemblies while debugging job flow

The companion DLL under `Source/BridgeTools` contains named, reusable contracts
that exercise Achtung against a running game. The main project restores and
builds it automatically. Build, deploy, lifecycle, required mod configuration,
and contract details are documented in [TESTING.md](TESTING.md).

Toolchain projects:

- RimBridgeServer: `https://github.com/pardeike/RimBridgeServer`
- GABS: `https://github.com/pardeike/GABS`
- DecompilerServer: `https://github.com/pardeike/DecompilerServer`

## Notes

- Achtung! is designed to minimize save dependencies, but an active custom Achtung job can still be serialized into a save. If you want to remove the mod cleanly from a save, cancel current Achtung jobs first and save again.
- Force-work jobs are intentionally aggressive. They are meant for priority intervention, not soft scheduling.

## Feedback

- GitHub: `https://github.com/pardeike/Achtung2`
- Steam Workshop: `https://steamcommunity.com/sharedfiles/filedetails/?id=730936602`
- Ludeon Forums: `https://ludeon.com/forums/index.php?topic=22130.0`

## License

Free as in free beer. Copy, learn, and be respectful.
