#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
mkdir -p .builds
log="$repo_root/.builds/build-quiet.log"
exec 3>&1
exec >"$log" 2>&1
trap 'build_exit=$?; if (( build_exit != 0 )); then printf "Build or verification failed (exit %s). Log: %s\n" "$build_exit" "$log" >&3; tail -25 "$log" >&3; fi' EXIT

released_16_sha=ff8d60361a124b76bfc48a635c890c4907bbd52354a526fd8e9b4316ef683d96
actual_16_sha=$(shasum -a 256 1.6/Assemblies/Achtung.dll | cut -d ' ' -f 1)
if [[ "$actual_16_sha" != "$released_16_sha" ]]; then
	printf 'The 1.6 DLL differs from the published release.\n' >&2
	exit 1
fi

dotnet_args=()
rimworld_mod_dir_arg=""
rimworld_mod_dir_property_seen=false

record_rimworld_mod_dir_property() {
	local assignment="$1"
	local property_name property_value normalized_name

	[[ "$assignment" == *=* ]] || return 0
	property_name="${assignment%%=*}"
	property_value="${assignment#*=}"
	normalized_name="$(printf '%s' "$property_name" | tr '[:lower:]' '[:upper:]')"
	[[ "$normalized_name" == "RIMWORLD_MOD_DIR" ]] || return 0

	if [[ "$rimworld_mod_dir_property_seen" == true && "$rimworld_mod_dir_arg" != "$property_value" ]]; then
		printf 'Refusing build because RIMWORLD_MOD_DIR is specified more than once with different values:\n' >&2
		printf '  first:  %s\n' "$rimworld_mod_dir_arg" >&2
		printf '  second: %s\n' "$property_value" >&2
		exit 2
	fi
	rimworld_mod_dir_property_seen=true
	rimworld_mod_dir_arg="$property_value"
}

record_property_list() {
	local property_list="$1"
	local assignment
	local IFS=';'
	local assignments=()

	read -r -a assignments <<< "$property_list"
	for assignment in "${assignments[@]}"; do
		record_rimworld_mod_dir_property "$assignment"
	done
}

expect_property_list=false
for arg in "$@"; do
	dotnet_args+=("$arg")
	if [[ "$expect_property_list" == true ]]; then
		record_property_list "$arg"
		expect_property_list=false
		continue
	fi

	case "$arg" in
		-p|/p|--property|-property|/property)
			expect_property_list=true
			;;
		-p:*|/p:*|--property:*|-property:*|/property:*)
			record_property_list "${arg#*:}"
			;;
		-p=*|/p=*|--property=*|-property=*|/property=*)
			record_property_list "${arg#*=}"
			;;
	esac
done

effective_rimworld_mod_dir="${RIMWORLD_MOD_DIR:-}"
if [[ "$rimworld_mod_dir_property_seen" == true ]]; then
	if [[ -n "$effective_rimworld_mod_dir" && "$effective_rimworld_mod_dir" != "$rimworld_mod_dir_arg" ]]; then
		printf 'Refusing deploy build because RIMWORLD_MOD_DIR differs between environment and MSBuild property:\n' >&2
		printf '  environment: %s\n' "$effective_rimworld_mod_dir" >&2
		printf '  property:    %s\n' "$rimworld_mod_dir_arg" >&2
		exit 2
	fi
	effective_rimworld_mod_dir="$rimworld_mod_dir_arg"
fi

if [[ -n "$effective_rimworld_mod_dir" ]]; then
	"$repo_root/scripts/rimworld-deploy-guard.sh" check-stopped
fi

if (( ${#dotnet_args[@]} )); then
	dotnet build Source/Achtung.csproj -v:q -clp:ErrorsOnly "${dotnet_args[@]}"
else
	dotnet build Source/Achtung.csproj -v:q -clp:ErrorsOnly
fi

actual_16_sha=$(shasum -a 256 1.6/Assemblies/Achtung.dll | cut -d ' ' -f 1)
if [[ "$actual_16_sha" != "$released_16_sha" ]]; then
	printf 'The build changed the published 1.6 DLL.\n' >&2
	exit 1
fi
if [[ -n "$effective_rimworld_mod_dir" ]]; then
	cmp 1.6/Assemblies/Achtung.dll "$effective_rimworld_mod_dir/Achtung/1.6/Assemblies/Achtung.dll"
	cmp 1.7/Assemblies/Achtung.dll "$effective_rimworld_mod_dir/Achtung/1.7/Assemblies/Achtung.dll"
	python3 - "$effective_rimworld_mod_dir" <<'PY'
from pathlib import Path
from zipfile import ZipFile
import sys
root = Path(sys.argv[1])
with ZipFile(root / 'Achtung.zip') as archive:
    for name in ('1.6/Assemblies/Achtung.dll', '1.7/Assemblies/Achtung.dll', 'About/About.xml', 'About/Manifest.xml', 'LoadFolders.xml'):
        if archive.read(name) != (root / 'Achtung' / name).read_bytes():
            raise SystemExit(f'ZIP does not match deployed file: {name}')
PY
fi
printf 'ok\n' >&3
