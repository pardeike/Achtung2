# Scripts

## build-quiet.sh

Run `RIMWORLD_MOD_DIR="/absolute/path/to/RimWorld/Mods" ./scripts/build-quiet.sh -c Release`
to build Achtung for RimWorld 1.7, deploy it, and create the ZIP. The command
checks that the published 1.6 DLL remains unchanged. Omit `RIMWORLD_MOD_DIR`
for a local build only. Full output goes to `.builds/build-quiet.log`.

The build also builds and deploys the matching RimBridge companion. It uses
`RimBridgeServer.Sdk` 2.3.0 from the sibling `.nuget-local` feed. If that
package is missing, create it with `../RimBridgeServer/scripts/pack-sdk.sh`.
Pass `-p:BuildBridgeTools=false` to omit the companion when packaging a release.

When `RIMWORLD_MOD_DIR` is set in the environment or passed using any normal
MSBuild property spelling, the script treats the command as a deployment. It
rejects conflicting values and refuses to deploy while RimWorld is running.
Stop the game before deployment and restart it afterwards.

## rimworld-deploy-guard.sh

Validates the Mods root, rejects the 1.6 app path and the known UserData alias that would separate a
mod from its sibling BridgeTools directory, and serializes the destructive copy
and zip targets across repository clones and worktrees. The process check is
repeated while holding that lock, immediately before the destructive targets.
The guard is called by the project and is not normally invoked by hand.

The paired destinations are:

- `Mods/Achtung`
- the physical Mods root's sibling `BridgeTools/Achtung`

Both paths must come from the same `RIMWORLD_MOD_DIR` value.

## check-settings-layout-release-gate.py

Enforces the conditional localization gate used by the GitHub release workflow.
It compares the intended release with the previous semantic release tag. It
skips when no keyed language wording changed and otherwise requires the current
`TestEvidence/SettingsLayout.json` digest and all 12 live RimWorld language
results to pass before a release tag can be created.

It does not reproduce RimWorld layout calculations. Those are owned by the
companion-only `achtung/audit_settings_layout` Bridge tool; this script handles
only Git history, evidence freshness, and release gating.

Run it without arguments against the working tree. Use
`--print-input-digest` when recording fresh evidence, or `--target-ref REF` for
a committed release target such as GitHub Actions' `GITHUB_SHA`.
