# CLI Reference

`codemetrics` is the installable command from the `CodeMetricsToolkit.Tool` package.
Run `codemetrics help <command>` for the same reference from the installed tool.

## Commands

```text
codemetrics analyze <path> [options]
codemetrics list-metrics
codemetrics explain <metric-id|metric-id@version>
codemetrics validate-output <artifact-dir>
codemetrics help [command]
codemetrics --version
```

`analyze` accepts a directory, one `.csproj`, or one `.sln`. An explicit project or
solution restricts the selected project population instead of silently analyzing its
siblings.

## Analyze options

| Option | Meaning |
| --- | --- |
| `-o`, `--output <dir>` | Output directory; default `artifacts/codemetrics` |
| `--include <glob>` | Include matching source paths; repeatable |
| `--exclude <glob>` | Exclude matching source paths; repeatable |
| `--include-generated` | Include recognized generated C# files |
| `--include-chunk-text` | Embed source text in `chunks.ndjson` |
| `--semantic` | Request semantic analysis; this is the default |
| `--syntax-only` | Skip restore and semantic analysis |
| `--no-restore` | Skip the pre-analysis `dotnet restore` |
| `--isolate-input` | Analyze a private temporary copy of the selected tree |
| `--top <count>` | Hotspots retained in `summary.json`; default 20 |
| `--allow-degraded` | Return success for degraded artifacts; does not make them trusted |
| `--allow-empty` | Return success when no C# files match |

Include and exclude globs are matched against normalized root-relative source paths.
`*` does not cross directories, `**` does, and `**/` also matches zero directories.
For example, `*.cs` matches only root files while `**/*.cs` matches C# files at any depth.
The manifest
records the resolved input kind, explicit selected path, source-population hash, and all
options that can affect output.

## Resource limits

The CLI fails closed when structured inputs exceed fixed safety limits. `validate-output`
accepts at most 128 MiB for each JSON artifact and 1 GiB for each NDJSON artifact. NDJSON
is read incrementally and is limited to 8 Mi characters per line, 1,000,000 physical
lines, and 1,000,000 non-empty records per artifact. JSON nesting is limited to 64.

`analyze` applies the same tree ceilings whether or not `--isolate-input` is used: at most
4 GiB across 250,000 files and 50,000 directories, 256 MiB per file, and 96 directory
levels. Non-regular files (FIFOs, devices, sockets, and reparse points) are rejected.
`--isolate-input` copies at those same limits and removes the temporary copy when a quota
is exceeded. Excluded `.git`, `.vs`, `bin`, `obj`, and `artifacts` trees do not consume
the copy quota.

These limits are intentionally not CLI-tunable: they are hard resource-exhaustion
boundaries. Analyze unusually large repositories in scoped project/solution slices.

The output directory may be a descendant of the analyzed root, as with the default
`artifacts/codemetrics`. It cannot be the input root or one of its ancestors, and every
existing parent component must be a real directory rather than a symbolic link or
reparse point. Keep the input tree unchanged until analysis completes.

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Success |
| 1 | Invalid command, input, arguments, or file-system operation |
| 2 | Artifact validation failed |
| 3 | Artifacts were written, but trust or empty-input gates rejected the run |
| 70 | Unexpected internal failure |
| 130 | Operation canceled |

Console text is for operators. Automation should consume the versioned artifacts and
exit code rather than scrape descriptive output.
