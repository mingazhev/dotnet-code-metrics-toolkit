# ADR 0001: Stable Target IDs

## Status

Accepted

## Context

Autoresearch needs repeatable identifiers for baseline diff, top-N movement, graph traversal, and source-range lookup. Display names are not enough: overloads, generic methods, constructors, properties, and partial types collide quickly if ids are hand-built from names only.

The broad metrics plan treated ids as a reporting detail. For autoresearch they are part of the product contract.

## Decision

Use semantic ids when Roslyn symbols are available:

```text
solution:root
project:<project-path-hash>
file:<repo-relative-path>
type:<assembly-name>/<xml-doc-id>
member:<assembly-name>/<xml-doc-id>
chunk:<target-id>#<chunk-kind>
```

For `type` and `member`, `<xml-doc-id>` is `ISymbol.GetDocumentationCommentId()`. The assembly name is included because XML doc ids are only unique inside an assembly.

`solution:root` is the artifact-local singleton root. Project ids hash the
repository-relative project path. These structural targets report
`targetIdStability=syntax_fallback`; neither id depends on line positions.
Member fallback identities that embed `:start-line` report `line_fallback`.

Fallback ids are allowed only when semantic loading is unavailable:

```text
type:<project-path-hash>/<namespace>.<type-name>@<file-path>
member:<project-path-hash>/<containing-type>.<member-name>#<parameter-count>@<file-path>:<start-line>
```

Every metric result and chunk must include `targetIdStability`:

```text
semantic
syntax_fallback
line_fallback
```

Partial types produce one logical type node with multiple declaration spans. Member metrics remain member-scoped unless a metric explicitly documents aggregation across partial declarations.

## Required Examples

```text
type:GenericsAndOverloadsProject/T:GenericsAndOverloadsProject.Repository`1
member:GenericsAndOverloadsProject/M:GenericsAndOverloadsProject.Repository`1.Find``1(System.String)
member:GenericsAndOverloadsProject/M:GenericsAndOverloadsProject.OverloadService.Format(System.Int32)
member:GenericsAndOverloadsProject/M:GenericsAndOverloadsProject.OverloadService.Format(System.String)
member:ComplexityProject/M:ComplexityProject.DecisionSamples.#ctor(System.Int32)
member:ComplexityProject/P:ComplexityProject.Order.Status
type:PartialTypesProject/T:PartialTypesProject.PartialOrder
```

## Consequences

- Semantic ids are stable across line moves and formatting-only changes.
- Syntax fallback ids are explicitly less stable and must not be silently mixed with semantic ids.
- Baseline diff can treat stability changes as a meaningful signal.
- Analyzer implementation must keep source ranges separate from identity.
