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

Use file-scoped namespaces, PascalCase types/methods and camelCase locals/parameters/private fields. Keep related small contract records together; place a substantial service in its own named file. Order imports as System, external packages, workspace namespaces. Prefer sealed classes unless extension is intentional. Use immutable records for value contracts and explicit nullable annotations; do not use null-forgiving operators to hide an unvalidated boundary.

Use primary constructors for simple dependency capture; use an explicit constructor when validation or setup makes dependencies clearer. Dependencies are constructor-supplied and assembled at composition. Private fields are readonly where possible. Do not add interfaces for trivial classes without a consumer boundary. Use expression bodies for trivial single expressions; expand control flow and keep one effect/statement per line in changed code.

Enable nullable reference types. Prefer explicit immutable message contracts. Use PascalCase types and public members, camelCase parameters and local variables.
Keep Windows and transport details outside the core. Represent expected protocol outcomes explicitly; preserve context for unexpected faults.
Pass cancellation through asynchronous operations. Define correlation at the boundary and distinguish a successful write from confirmed delivery.
Begin with a deterministic message round trip and important contract failures. A fixture is never a real AI-agent qualification.

Async operations returning Task use the Async suffix and accept CancellationToken last, except established external interface signatures. Dispose owned resources explicitly, release leases in finally/using, and distinguish cancellation of waiting from confirmation of external effects. Preserve port semantics when substituting an adapter.

Expected failures carry a stable code and explicit effect/recovery semantics. Translate external failures once at their boundary; UI text is a projection of that code. Serialize through the existing owner and choose explicit UTF-8 for redirected streams. Keep protocol timestamps invariant and human dates under the display owner.

At human/agent boundaries, surface expected refusals and unexpected errors with an identifiable reference and retrievable bounded technical details. Keep the user message separate from the diagnostic, exclude request bodies/tokens/credentials, and make diagnostic-storage failure explicit while preserving the primary error. Do not silently suppress an error or replace it with a later logging/rollback exception (evidence `2026-11-12-identifiable-errors`).

## Other source languages

Python uses snake_case functions/locals, PascalCase classes, explicit UTF-8 file I/O, pathlib and subprocess argument lists. Keep executable CLI behavior under main/entry-point guards when a module is reusable. Prefer normal multiline blocks over semicolon chains; use ordinary imports rather than dynamic imports for static dependencies.
For an Agent Relay operation already owned by C#, invoke the C# entry point from the Python procedure. Keep Python only where it owns useful orchestration or a distinct adapter. Extend the existing C# operation when the caller needs another supported state.

JavaScript uses camelCase functions/locals and const unless reassignment is necessary. Keep pure model helpers separate from rendering and action effects. Components own markup, escaping and accessibility; state belongs to the controller/model, not duplicated DOM assumptions. Preserve the existing module mechanism until a justified change requires another toolchain.

PowerShell uses approved Verb-Noun public functions, explicit parameters, terminating failures and checked native exit codes. Resolve paths from an explicit root/script location; use LiteralPath for operator paths and ArgumentList for processes. Never build a deletion command across shells or repurpose common environment variables.

JSON/schema/config files contain declarative values, not hidden business execution. Production consumes shared definitions; generated copies have a stated owner and reproducible check. A dependency on the current working directory or machine encoding must be explicit, not accidental.

## Comments, references and decisions

Comments explain intent, invariants or non-obvious constraints, not restate a method name. Use reciprocal references for complementary operations. C# uses XML see/seealso cref where resolvable; Python/JS use @see with stable symbol/file names; PowerShell uses comment-based help links for related entry points. Link a current rule owner instead of copying its explanation everywhere.

Record significant choices and exceptions with reasons, alternatives and status in the application's notes/decisions, and retain current rules in doc. A decision to depart from a convention is scoped and explicit. Apply conventions to changed areas; avoid unrelated mass formatting. The [design guide](design-guide.md) owns responsibility/dependency reasoning.

## Tools

Use explicit parameters, terminating errors and stable nonzero exits. Resolve paths from script locations, not the caller's working directory.
Check native process exit codes. Select one executable explicitly when PATH contains several matches; never concatenate discovered paths.
Keep progress text out of JSON output and installs out of discovery. Keep Python POC runtime data under the application-specific ignored local directory.

## Checks

Verify syntax and the normal command path. Test relevant boundaries: unknown application, missing selection, escaping path and missing prerequisites.
Before migrations, save a source snapshot and file map. Afterwards verify links and retained proof hashes. Keep rollback material until review.

Name checks after observable behavior and important failure, arrange/act/assert clearly, and use stable fictional inputs. Test coupled operations through their shared contract, rather than duplicating implementation formulas as the only oracle. Native, model and UI checks remain explicit and scoped. One relevant passing check is preferable to many repeated checks with no new evidence.
