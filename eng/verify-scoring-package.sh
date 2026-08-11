#!/usr/bin/env bash
set -euo pipefail

package_dir="${1:-artifacts/packages}"
package_dir="$(cd "$package_dir" && pwd)"

packages=()
while IFS= read -r package; do
  packages+=("$package")
done < <(
  find "$package_dir" -maxdepth 1 -type f \
    -name 'CodeMetricsToolkit.Scoring.*.nupkg' \
    ! -name '*.symbols.nupkg' \
    -print | sort
)

if [[ "${#packages[@]}" -ne 1 ]]; then
  echo "Expected exactly one CodeMetricsToolkit.Scoring package; found ${#packages[@]}." >&2
  exit 1
fi

package_name="$(basename "${packages[0]}")"
package_version="${package_name#CodeMetricsToolkit.Scoring.}"
package_version="${package_version%.nupkg}"
smoke_root="$(mktemp -d "${TMPDIR:-/tmp}/codemetrics-scoring-smoke.XXXXXX")"
consumer_dir="$smoke_root/consumer"
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

dotnet new console --framework net10.0 --output "$consumer_dir" --no-restore
dotnet add "$consumer_dir" package CodeMetricsToolkit.Scoring \
  --version "$package_version" \
  --no-restore

cat >"$consumer_dir/Program.cs" <<'EOF'
using CodeMetricsToolkit.Scoring;

Console.WriteLine(ScoringProfileContract.Current);
EOF

dotnet restore "$consumer_dir" --configfile "$nuget_config"
actual_version="$(dotnet run --project "$consumer_dir" --no-restore)"

if [[ "$actual_version" != "1.0.0" ]]; then
  echo "Scoring package smoke returned '$actual_version'; expected profile contract '1.0.0'." >&2
  exit 1
fi
