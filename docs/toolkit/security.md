# Security and Trust Model

## Trusted input is the default assumption

The default semantic pipeline may invoke `dotnet restore` and then load MSBuild projects.
MSBuild and NuGet are extensible execution environments: a target repository can contain
custom targets, tasks, SDK resolvers, package sources, or plugins that execute code.

Therefore:

- use semantic mode only for repositories you trust;
- use an isolated container or VM for untrusted semantic analysis;
- do not expose secrets or a writable host filesystem to that environment;
- restrict outbound network access when restores are not required;
- use `--syntax-only` for a non-executing first pass.

`--isolate-input` copies input to a temporary directory to avoid accidental parent-level
`Directory.Build.*`, `Directory.Packages.props`, or NuGet configuration. It does not
neutralize malicious files inside the input. Isolation copies only regular files, aborts
above 4 GiB total, 256 MiB per file, 250,000 files, 50,000 directories, or 96 directory
levels, and cleans up the partial temporary copy. Source discovery and syntax-only file
reads use the same ceilings even when isolation is off.

Semantic restore resolves an absolute `dotnet` host (`DOTNET_HOST_PATH`, a muxer
`ProcessPath`, `DOTNET_ROOT`, or the runtime layout). It does not search the process
current directory or the analyzed tree for `dotnet`.

Keep the input tree immutable for the complete analysis run. Discovery and later file
reads are separate operations; portable handle-relative, no-follow traversal is not yet
implemented, so concurrent path or symlink replacement is outside the supported threat
model.

For an explicit solution, referenced projects may be outside the solution's directory
only when they remain inside a recognized repository boundary (`.git`, `global.json`, or
`Directory.Build.props`). A crafted solution cannot broaden the isolation copy to an
arbitrary ancestor. Source files linked from outside that boundary are reported, but the
run is degraded because the isolated and live source populations cannot be treated as
equivalent.

## Trust signals

`summary.json.analysisHealth` reports analysis quality, semantic availability, restore and
build status, diagnostic trust, and explanatory messages. Automation must fail closed when
its policy requires semantic or diagnostic facts and those facts are not trusted.

The CLI does this by default for degraded runs. An explicit override can retain degraded
artifacts for investigation, but an override does not make them trusted.

## Output handling

Artifacts can expose names, paths, dependency structure, diagnostic messages, hashes, and
optionally source text. Store and transmit them under the same access policy as source code.
Avoid `--include-chunk-text` unless a downstream consumer truly needs it.

Use a single writer for each output path and start readers only after analysis completes.
Publication is rollback-safe for ordinary exceptions, but a process crash during a
directory swap can leave a hidden backup generation that requires operator cleanup.
The output may be inside the analyzed root, including the default
`artifacts/codemetrics`, but it cannot equal the input root or contain it after symlink
and reparse resolution. Existing symbolic links or reparse points in the output's parent
path are rejected. An existing output directory is replaced only when it is empty or
contains a regular `manifest.json` that parses under the same JSON size and depth
ceilings as `validate-output`.

Structured artifact consumers fail closed at fixed resource ceilings: 128 MiB per JSON
artifact, 1 GiB per NDJSON artifact, 8 Mi characters per NDJSON line, 1,000,000
lines/records, 1 MiB per scoring profile, and JSON depth 64. These limits bound resource
consumption; schema and cross-artifact validation remain separate requirements.
