# Glossary

**Utility:** defines the project's fixed French and English terms.  
**Usage:** read before naming a concept in documentation, interfaces or code.

## Terms

The bilingual vocabulary is fixed by the operator (evidence `2026-10-28-project-vocabulary-keeps-french-and-english`).

### Installation directory — Dossier d'installation

Directory containing Agent Relay application files. Read [Application locations](../target-spec/technical/configuration.md#application-locations) for its relationship to application data.

### Data directory — Dossier de données

Directory containing Agent Relay configuration and durable application data. Read [Application locations](../target-spec/technical/configuration.md#application-locations).

### Workspace — Espace de travail

Directory in which an agent is authorized to work. Read [Workspace boundaries](../target-spec/functional/configuration.md#workspace-boundaries).

### Relay project — Projet relayé

Association of workspaces, endpoints and access rules. Read [Project membership](../target-spec/functional/configuration.md#project-membership).

### Endpoint — Extrémité

Identified participant associated with an agent and relay capabilities. Read [Endpoint roles](../target-spec/functional/agent-session.md#endpoint-roles).

### Role — Rôle

Function assigned to an endpoint within a relay project. Read [Endpoint roles](../target-spec/functional/agent-session.md#endpoint-roles).

### Task — Tâche

Durable request addressed to an authorized endpoint. Read [Durable exchanges](../target-spec/functional/agent-session.md#durable-exchanges).

### Main — Principal

Participant role assigned within a Relay project / Projet relayé. It does not imply Windows, a particular agent product or automatic administrative permission.

### Secondary — Secondaire

Participant role assigned within a Relay project / Projet relayé. Explicit capabilities and workspace grants determine its authority.

### Attempt — Tentative

One execution of a task. Read [Durable exchanges](../target-spec/functional/agent-session.md#durable-exchanges).

## Banned vocabulary

Avoid an unqualified "project directory" or "dossier du projet" when it could mean application files or a working directory. Use installation directory, data directory or workspace as appropriate.
