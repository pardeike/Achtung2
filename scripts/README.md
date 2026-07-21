# Scripts

## build-quiet.sh

Builds `Source/Achtung.csproj` with concise output. The main project owns
companion restore and build, so do not build the BridgeTools project separately
in the normal workflow.

When `RIMWORLD_MOD_DIR` is set in the environment or passed using any normal
MSBuild property spelling, the script treats the command as a deployment. It
rejects conflicting values and refuses to deploy while RimWorld is running.
Stop and restart the game through GABS around deployment.

## rimworld-deploy-guard.sh

Validates the Mods root, rejects the known UserData alias that would separate a
mod from its sibling BridgeTools directory, and serializes the destructive copy
and zip targets across repository clones and worktrees. The process check is
repeated while holding that lock, immediately before the destructive targets.
The guard is called by the project and is not normally invoked by hand.

The paired destinations are:

- `Mods/Achtung`
- the physical Mods root's sibling `BridgeTools/Achtung`

Both paths must come from the same `RIMWORLD_MOD_DIR` value.
