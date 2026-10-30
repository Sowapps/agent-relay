# Source conventions

**Utility:** keeps source and verification consistent without heavy controls.  
**Usage:** apply when adding code, contracts and tools.

## General

Use English identifiers and prose; preserve exact operator quotations and fixed bilingual terms.
Apply `.editorconfig`: UTF-8, LF, final newline, spaces and intentional Markdown trailing spaces.
Organize by capability and ownership; avoid speculative abstractions and generic utility layers.
Apply DRY and SOLID: give shared behavior one owner, keep responsibilities focused and invert external dependencies behind small use-case ports.
Factor repeated real behavior instead of multiplying abstractions for hypothetical reuse (evidence `2026-10-28-first-application-slice-and-reusable-tools`).

## C# target

Enable nullable reference types. Prefer explicit immutable message contracts. Use PascalCase types and public members, camelCase parameters and local variables.
Keep Windows and transport details outside the core. Represent expected protocol outcomes explicitly; preserve context for unexpected faults.
Pass cancellation through asynchronous operations. Define correlation at the boundary and distinguish a successful write from confirmed delivery.
Begin with a deterministic message round trip and important contract failures. A fixture is never a real AI-agent qualification.

## Tools

Use explicit parameters, terminating errors and stable nonzero exits. Resolve paths from script locations, not the caller's working directory.
Check native process exit codes. Select one executable explicitly when PATH contains several matches; never concatenate discovered paths.
Keep progress text out of JSON output and installs out of discovery. Keep Python POC runtime data under the application-specific ignored local directory.

## Checks

Verify syntax and the normal command path. Test relevant boundaries: unknown application, missing selection, escaping path and missing prerequisites.
Before migrations, save a source snapshot and file map. Afterwards verify links and retained proof hashes. Keep rollback material until review.
