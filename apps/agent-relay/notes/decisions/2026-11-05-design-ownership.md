# Design ownership and shared contracts

**Utility:** preserves the engineering rationale for the approved design-foundations work.  
**Usage:** consult with the current architecture; read the scoped workstream for implementation and verification status.

## Context and status

05/11/2026, selected for implementation within the five approved audit lots. This is an engineering decision, not operator acceptance of measured behavior. Current guide: root doc/developing/design-guide.md. Shared facts diverged across languages and native session creation/control; one adapter also assembled and implemented unrelated execution/import responsibilities.

## Decision

Keep coherent application services and the existing Core ports. Put one native-layout definition in packaged source, accessed by small language facades and checked by a common corpus. Keep OS-specific security checks at the OS boundary. Extract substantial inbox and command capabilities from session management, assemble them at composition, and retain public use cases. Own expected native failures at the bridge and use explicit UTF-8 on stdio. Host process discovery is a deployment capability; the data lock remains authoritative.

## Alternatives and consequences

Separate handwritten constants plus tests still duplicate authoritative knowledge. A generic path engine or cross-OS service call is unnecessary. Generated facades remain an option if direct packaged-definition loading proves impractical. A mandatory ORM, DI container and service per method add no needed capability. Focused collaborators make execution/recovery easier to evolve together while preserving policy revalidation. Shared definitions must be included in both Windows payloads and provisioned Linux helpers; packaging is part of their contract.

## Maintenance and supersession

Update the shared owner before consumers and paired operations; keep reciprocal references at non-obvious boundaries. Record deviations or replacement choices here or in a new superseding decision. The workstream is notes/workstreams/2026-11-05-design-foundations.md at workspace root; audit findings remain a dated proposal. No new product adapter, paid model use or publication is implied.
