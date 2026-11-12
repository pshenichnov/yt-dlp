# ADR 0001: Runtime and Language Versions

- Status: accepted
- Date: 2026-07-25

## Context

Phase one requires ASP.NET Core services and one Python service. The developer
Mac currently has an out-of-support .NET SDK, while deployment is containerized.
The repository needs a current, reproducible baseline independent of host tools.

## Decision

- Target .NET 10.
- Pin the developer SDK with `global.json` to `10.0.302`.
- Build and run .NET containers from the .NET 10 image family.
- Target Python 3.12 in the extractor container and for local development.
- Pin Python dependencies with exact versions in the lock input.

.NET 10 is the active LTS release and is supported through November 2028.
Using the same Python minor version locally and in the extractor container
reduces environment-specific differences.

## Consequences

- Developers with older SDKs use the pinned container build or install the
  pinned SDK side by side.
- .NET package dependencies must support `net10.0`.
- The extractor cannot rely on Python features newer than 3.12.

## References

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)

