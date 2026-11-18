# Agent Relay rules

**Utility:** defines application-specific boundaries for development and operation.  
**Usage:** read after root AGENTS.md and before acting on Agent Relay.

Read the shared root instructions first. Preserve the fixed [bilingual vocabulary](doc/developing/glossary.md).
Before implementation, read the [production ownership map](doc/developing/projects-and-adapters.md) and relevant [design decision](notes/decisions/README.md), then the active root workstream. Consumer MCP onboarding does not replace this development route.
Keep direct Windows-to-WSL commands distinct from AI-agent requests. WSL-to-Windows relay capability is messages only.
Keep project workflows, Git worktrees and coordination of shared files in the consuming project. Apply the execution and transfer boundaries in [production ownership](doc/developing/projects-and-adapters.md).
Run qualification in an isolated data scope. If an approved stable check needs a test project, record its ID; reuse an archived qualification project when suitable. Archive it through the normal project lifecycle after the check and verify cleanup before closing the work.
POC experiments require isolated persistent workspaces and explicit model-call authorization. Do not use existing agent sessions or `/home/sowapps/projects`.
Historical permission for one Claude response does not authorize more calls. No model call or WSL operation is authorized by shared verification commands.
