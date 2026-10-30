# Configuration — Technical Target

**Utility:** states the decided storage and authority boundaries of configuration.  
**Usage:** read before selecting configuration locations or handling credentials.

Configuration belongs to the relay rather than to an agent's working files.

**Functional counterpart:** [Configuration functional target](../functional/configuration.md).

## Application locations

The installation directory contains application files. The data directory contains central configuration and durable application data. A workspace is separate from both. The canonical names and
translations are defined in the [glossary](../../developing/glossary.md) (evidence `2026-10-28-project-vocabulary-keeps-french-and-english`).

## Central configuration

Relay project membership, authorized workspaces, endpoints and permissions are administered centrally. Secrets use separate protected storage; configuration retains references rather than embedding
those secrets. No workspace configuration file is required by the MVP (evidence `2026-10-28-central-configuration-and-extensible-model-are-accepted`).

## Extensible model

The model represents workspaces and endpoints as collections, with project-specific roles and restrictions. The restricted execution profile limits operation without reducing the underlying model
to a hard-coded Windows pilot and Linux worker pair (evidence `2026-10-28-central-configuration-and-extensible-model-are-accepted`).
