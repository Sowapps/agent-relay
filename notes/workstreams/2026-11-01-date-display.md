# Date and time display

**Utility:** retains the authorized date/time formatting correction and verification.  
**Usage:** resume this narrow UI scope; no publication, global MCP edits or agent/model calls.

## Scope

Florent requires human-facing dates DD/MM/YYYY with2/2/4digits. Hours use24-hour HH:mm or HH:mm:ss, with2digits per component; seconds are optional. Relative wording is allowed only if self-updating. Explicit GO covers audit, central formatting, correction, UI verification and local update. Use absolute dates here, avoiding relative labels that could go stale. Original protocol/raw diagnostic timestamps remain machine data.

## Checkpoint

Audit found abbreviated day/month and ISO dates in task/activity/message/session/conversation/detail surfaces plus native legacy/technical histories and event diagnostics. Shared JS formatters and native DisplayDateTime now own human presentation; structured timestamps retain ISO/invariant culture. Goal creation refused because an unrelated historical blocked objective occupies the slot; its counters do not measure this work.

## Completed

Browser/native date formatting is centralized and applied to all identified human timestamp surfaces, including titles/accessibility labels and technical-console/event histories. Date-only strings retain their local calendar day; midnight/evening values use padded24-hour times. No relative date labels are introduced. Browser list date columns use intrinsic width; native HistoryRow measures the actual timestamp width instead of its old100px rectangle. This avoids clipping/overlap caused by the added year.

Debug/Release builds pass with0warnings/errors. DateDisplayScenarios passes under fr-FR, en-US and th-TH (fixed Gregorian year, zero padding,00:05/00:05:09/23:06:02). The actual native browser scenario passes general/tasks/activity/messages/sessions at1120and816px, conversation dates with seconds, no horizontal overflow and explicit date/arrow separation. Captures at local/apps/agent-relay/qualification/date-display-final/render were inspected, including activity816px and conversation. Source audit finds no old short-date formatting; shared verification and whitespace checks pass. Verification is an observation pending operator review.

Guarded local update completed with final host29136, transaction var/apps/agent-relay/deployment/v1-qualification/deployment-621cbb37428947eaa068af9d9dc07157. Project/profile/review files were hash-preserved during the UI update; staged browser assets matched source. Documentation records the display contract. Previous storage, hover and toast work remains preserved. No global MCP registration, WSL/native-agent/model call, commit, tag, push or publication. Scoped objective complete; the historical API goal remains unrelated and blocked.
