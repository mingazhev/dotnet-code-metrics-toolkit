# Cleanup and metric expansion smoke

Recorded on 2026-10-18 (Asia/Yekaterinburg). These are two local samples per mode,
not a statistical benchmark or a throughput guarantee.

## Compared builds and input

- Baseline: `dd63a7fa41e1213e62bbf3f44f9e39b832dc227a`, tool 0.1.0 with the
  cryptography dependency patch required for a clean locked restore.
- Final: the cleanup branch after `6342306`, packaged as 0.2.0 with the release
  metadata and style fixes accompanying this report. Both tools were installed
  from local NuGet packages and invoked through their `codemetrics` executable.
- Host: macOS 26.6.2 arm64, SDK 10.0.401, runtime 10.0.12. Dependencies were
  restored before measurement; runs used `--no-restore`.
- Fixed input: `src`, `Directory.Build.props`, `Directory.Packages.props`,
  `global.json`, `README.md`, `LICENSE` and `schemas` exported from
  `5507fb5d75dac9fe1f344b8743c8771befcd04bc` into a directory outside the checkout.
  Both builds analyzed exactly that directory, without changing its sources.
- Input inventory: 81 raw C# files, 434,181 bytes; discovery selected 79 files in
  3 projects. Both builds reported 141 types and 668 members in both modes.
- C# input SHA-256: `9d4261d5b11901d672bc71cc1e9d215589822b426b6dfea4435e41e6e38733fb`.
  The digest concatenates each sorted relative C# path and its bytes, excluding
  `bin` and `obj`.

## Measurements

Wall time includes process startup, analysis, artifact serialization and validation.
RSS is `/usr/bin/time -l` maximum resident memory for the main tool process, not
an aggregate of the tool and every MSBuild child process. MiB means 1,048,576 bytes.

| Mode | Build | Wall seconds, samples 1 / 2 | Max RSS MiB, samples 1 / 2 | Metric rows |
| --- | --- | --- | --- | --- |
| Syntax | Baseline | 1.529 / 1.489 | 120.7 / 120.5 | 7,573 |
| Syntax | Final | 2.049 / 1.712 | 128.5 / 128.6 | 8,445 |
| Semantic | Baseline | 5.946 / 5.791 | 321.1 / 285.8 | 16,259 |
| Semantic | Final | 6.574 / 6.404 | 282.4 / 327.4 | 17,187 |

The additional collection and output cost about 0.2–0.5 seconds in syntax mode
and 0.6 seconds in semantic mode on this input. These measurements do not support
a claim that the complete feature change is faster. The RSS samples vary too much
to infer a semantic-mode memory improvement.

Semantic health remained `trusted`, with one diagnostic in both builds; syntax
health remained `syntax_only`, with no diagnostics. Final repeated runs produced
identical six-artifact contents after removing only manifest/root timing fields
(`rootPath`, `startedAt`, `completedAt`, `durationMs`). Array and NDJSON order was
preserved. The installed final CLI validated both modes' output directories.

## Reproduction

Export the fixed input into a fresh temporary directory outside this checkout:

```bash
input_dir="$(mktemp -d /tmp/codemetrics-perf-input.XXXXXX)"
git archive 5507fb5d75dac9fe1f344b8743c8771befcd04bc \
  src Directory.Build.props Directory.Packages.props global.json README.md LICENSE schemas \
  | tar -x -C "$input_dir"
dotnet restore "$input_dir/src/CodeMetricsToolkit.Cli/CodeMetricsToolkit.Cli.csproj" \
  --locked-mode --disable-parallel -m:1
dotnet restore "$input_dir/src/CodeMetricsToolkit.Scoring/CodeMetricsToolkit.Scoring.csproj" \
  --locked-mode --disable-parallel -m:1
```

Build/package/install each revision into a separate tool directory, then run each
command twice with distinct output directories:

```bash
/usr/bin/time -l /path/to/tool/codemetrics analyze "$input_dir" \
  --no-restore --syntax-only --output /tmp/codemetrics-syntax-sample
/usr/bin/time -l /path/to/tool/codemetrics analyze "$input_dir" \
  --no-restore --output /tmp/codemetrics-semantic-sample
```

This input does not exercise the duplication ceilings or thousands of projects.
The duplication pass fails the whole analysis at its documented resource limits;
its bounded collector is not a global complexity bound for every existing projector.
The existing medium-fixture CLI smoke also passed within its 60-second ceiling.
