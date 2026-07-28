# ADR 0005 — The ingestion library is separate from its worker host

**Status:** Accepted

## Context

Ingestion started as a single Worker-SDK project: adapters, scheduler and `Program.cs`
together. The web app referenced it to reuse the pipeline for the manual **Ingest now**
button and the on-call drills.

That worked when running from the SDK, and broke the moment the app was containerised:

```
error NETSDK1152: Found multiple publish output files with the same relative path:
src/LineOps.Ingestion/appsettings.json, src/LineOps.Web/appsettings.json
```

Two executable projects in one publish output, each with its own `appsettings.json`. The
error is the visible symptom; the real problem is that whichever file won would be
nondeterministic, so the containerised app could silently boot on the *worker's*
configuration.

## Decision

Split by role:

- **`LineOps.Ingestion`** — a plain `Microsoft.NET.Sdk` class library: adapters, services,
  the scheduler `BackgroundService`, and DI registration. No entry point, no configuration
  files.
- **`LineOps.Worker`** — a `Microsoft.NET.Sdk.Worker` executable: `Program.cs` and
  `appsettings.json`, nothing else. It composes the library.

`LineOps.Web` references only the library. Both hosts register the scheduler through the same
`AddLineOpsIngestionScheduler()` extension, so there is one code path regardless of which
process runs it.

## Consequences

- One `appsettings.json` per deployable, which is the only way config can be reasoned about.
- The layering is now honest: a web app referencing another app's executable project was a
  smell that the publish conflict simply made visible.
- The choice of topology — scheduler in-process with the UI, or as its own container — is a
  startup registration and a compose profile, not a code change.
- Suppressing the error with `ErrorOnDuplicatePublishOutputFiles=false` was rejected: it would
  have kept the ambiguity and hidden it.
