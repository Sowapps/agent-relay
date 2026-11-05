# Agent Relay rules

**Utility:** defines application-specific boundaries for development and operation.  
**Usage:** read after root AGENTS.md and before acting on Agent Relay.

Read the shared root instructions first. Preserve the fixed [bilingual vocabulary](doc/developing/glossary.md).
Before implementation, read the [production ownership map](doc/developing/projects-and-adapters.md) and relevant [design decision](notes/decisions/README.md), then the active root workstream. Consumer MCP onboarding does not replace this development route.
Keep direct Windows-to-WSL commands distinct from AI-agent requests. WSL-to-Windows relay capability is messages only.
POC experiments require isolated persistent workspaces and explicit model-call authorization. Do not use existing agent sessions or `/home/sowapps/projects`.
Historical permission for one Claude response does not authorize more calls. No model call or WSL operation is authorized by shared verification commands.
