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

Include and exclude globs are matched against normalized source paths. The manifest
records the resolved input kind, explicit selected path, source-population hash, and all
options that can affect output.

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
