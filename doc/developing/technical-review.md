# On-demand technical review (TR)

**Utility:** defines the operator's TR shortcut and its analysis-only authority.  
**Usage:** apply when the operator invokes TR, generally or for a named scope. Registering this rule does not run a review.

Operator authority: evidence `2026-11-13-tr-review-shortcut`.

## Discover before proposing

Read applicable instructions, architecture and constraints within the useful scope. Do not presume layout, tools or procedures. Reuse retained findings before asking again or repeating tests. Review architecture, reuse and responsibilities; code simplicity and maintainability; application/build performance; formats, data, compatibility and migrations; errors, diagnostics, recovery and preservation; tests, exports, documentation and delivery; cross-project/agent operation; context, tool calls and token consumption. Challenge existing methods/formats when a concrete benefit justifies it.

Distinguish reproduced defects, plausible risks and investigation candidates. Measurements/gains must be observed or clearly marked as estimates; never fabricate them.

## Decision-ready plan

Keep the user-facing plan short, prioritized and sufficient to decide. Every lot includes a stable identifier, full name, concrete subject, type, expected benefit, effort, risk, confidence, dependencies and verifiable success criterion. Explain the evaluation scale. Use dependency IDs and distinguish satisfied dependencies. Qualitative effort/risk/confidence need defined meanings; numerical time/token estimates are estimates with a stated basis, not measurements.

Include proportionate reusable procedures for code, tests, exports, reporting and recovery. Seek exceptions, omissions and failures; state their handling and remaining limits. Follow the existing target-first and just-in-time point-planning workflow rather than creating a parallel process.

## Authorization and follow-through

TR authorizes analysis/proposals only. Wait for explicit approval of the implementation lots. The shortcut alone authorizes no commit, push, deployment or data change, notwithstanding the standing delivery rule for approved implementation. Preserve review notes locally under the appropriate documentation/history surface.

During approved work, maintain durable progress and update the operator at important checkpoints. At completion, report changes, verification and limits, then propose the next scope with the same evaluation fields. Never refer to the proposed next lot by its ID alone.
