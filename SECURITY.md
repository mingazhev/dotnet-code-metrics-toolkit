# Security Policy

## Analysis is not a sandbox

Syntax-only analysis reads C# source files and does not intentionally execute the target
repository's build. Semantic analysis is different: it can run `dotnet restore` and load
projects through MSBuild. NuGet and MSBuild evaluation can execute repository-controlled
targets or tooling.

Do not run semantic analysis on an untrusted repository on a developer workstation or
CI runner that contains valuable credentials. Use `--syntax-only`, or place semantic
analysis in an isolated container/VM with restricted network, filesystem, and secrets.

`--isolate-input` protects against accidental MSBuild configuration inherited from
parent directories. It is not a security boundary and does not make a malicious project
safe.

## Sensitive output

- Paths, symbols, diagnostics, and graph edges can reveal repository structure.
- `--include-chunk-text` copies source text into `chunks.ndjson`; leave it disabled unless
  the consumer explicitly needs source text.
- Treat artifact directories with the same confidentiality as the analyzed source.

## Reporting a vulnerability

Do not disclose a suspected vulnerability in a public issue. Contact the repository
owner through the private GitHub repository or use GitHub's private vulnerability
reporting channel if it is enabled. Include the affected version, reproduction, impact,
and any suggested mitigation.
