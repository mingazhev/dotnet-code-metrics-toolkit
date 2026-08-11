#!/usr/bin/env bash
set -euo pipefail

package_dir="${1:-artifacts/packages}"

if [[ ! -d "$package_dir" ]]; then
  echo "Package directory does not exist: $package_dir" >&2
  exit 1
fi

packages=()
while IFS= read -r package; do
  packages+=("$package")
done < <(
  find "$package_dir" -maxdepth 1 -type f \
    -name 'CodeMetricsToolkit.Tool.*.nupkg' \
    ! -name '*.symbols.nupkg' \
    -print | sort
)

if [[ "${#packages[@]}" -ne 1 ]]; then
  echo "Expected exactly one CodeMetricsToolkit.Tool package in $package_dir; found ${#packages[@]}." >&2
  exit 1
fi

package_name="$(basename "${packages[0]}")"
tool_version="${package_name#CodeMetricsToolkit.Tool.}"
tool_version="${tool_version%.nupkg}"
package_dir="$(cd "$package_dir" && pwd)"
smoke_root="$(mktemp -d "${TMPDIR:-/tmp}/codemetrics-package-smoke.XXXXXX")"
tool_dir="$smoke_root/tool"
output_dir="$smoke_root/output"
nuget_config="$smoke_root/NuGet.config"

cleanup() {
  rm -rf -- "$smoke_root"
}
trap cleanup EXIT

cat >"$nuget_config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-package-smoke" value="$package_dir" />
  </packageSources>
</configuration>
EOF

dotnet tool install CodeMetricsToolkit.Tool \
  --tool-path "$tool_dir" \
  --configfile "$nuget_config" \
  --version "$tool_version" \
  --no-cache

actual_version="$("$tool_dir/codemetrics" --version)"
if [[ "$actual_version" != "$tool_version" ]]; then
  echo "Installed tool reported version '$actual_version'; expected '$tool_version'." >&2
  exit 1
fi

"$tool_dir/codemetrics" --help
"$tool_dir/codemetrics" list-metrics
"$tool_dir/codemetrics" explain cyclomatic_complexity
"$tool_dir/codemetrics" analyze tests/CodeMetricsToolkit.TestAssets/SimpleProject \
  --isolate-input \
  --output "$output_dir"
"$tool_dir/codemetrics" validate-output "$output_dir"
