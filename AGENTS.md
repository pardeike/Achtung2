# Project workflow

- Run `./scripts/build-quiet.sh -c Release` for a local build.
- Set `RIMWORLD_MOD_DIR` to the RimWorld 1.7 Mods path to deploy and create the ZIP.
- Read `scripts/README.md` for the published companion SDK and deployment rules.
- Preserve the published `1.6/Assemblies/Achtung.dll`. Build current code into `1.7/`.
- Keep released shared resources unchanged. Put 1.7 resource changes under `1.7/`.

- Deployment preserves existing older-version DLLs and shared resources. A mismatched frozen payload stops deployment.
