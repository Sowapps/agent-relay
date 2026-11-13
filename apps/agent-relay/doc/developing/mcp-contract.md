# Stable MCP contracts

**Utility:** identifies the fixed transport contract and the application's versioned operation registry.  
**Usage:** consult before changing MCP schemas, adding an operation or comparing package compatibility. Generic facade integration is a separate implementation step; these declarations alone do not change a connected client's tool inventory.

## Owners and authority

`AgentRelay.Protocol` owns the fixed envelope, bounded schema subset, wire limit and compatibility records. It has no application, SQLite or platform dependency. `McpToolCatalog` owns application operation declarations and typed route translations. `McpOperationRegistry` selects an exact operation/revision and validates its arguments using the protocol validator. It returns a `ProjectAction`; it never grants authority or executes work. Existing application use cases recheck current project, participant, session and workspace rights at admission/execution.

Public operation schemas are protocol metadata, like `tools/list`. They do not expose project data or confer permission. Authorized project context must come from the existing discovery use case. Do not build a second permission policy from operation descriptions.

## Fixed surface and revisions

The fixed surface is `get_connection_status`, `discover_operations`, `invoke_operation` and `get_result`. Invocation carries `call_id`, `operation_id`, a positive `revision` and an opaque `arguments` object. That object's schema is validated again against the selected operation before routing. `call_id` is correlation only; it supplies neither authority nor idempotency. Operations that submit retained work keep their existing business `request_id` inside `arguments`. Never replay an uncertain invocation merely because transport ownership changed.

Legacy typed tools and generic operations share the declarations/routes. Typed tools select the latest supported revision; generic calls select an explicit revision. Unknown operations and unsupported revisions fail before routing with `operation_unknown` or `operation_revision_unsupported`; rediscover rather than guessing a replacement. Schema changes require a new revision. Retaining a previous revision requires retaining its actual compatible route, not just its name. `ValidateRevisionHistory` rejects changed schemas under an existing ID/revision and inconsistent schema hashes. It does not prove semantic compatibility of implementation changes.

## Identity and inspection

Compatibility separates transport protocol/version and wire hash, operation-registry hash, documentation hash and package/build identity. Wire identity includes the four schemas and frame limit, excluding operation declarations and descriptions. Schema hashes sort object keys recursively and ignore whitespace; array ordering and numeric representations remain significant. This is a defined project encoding, not a claim to implement a general JSON canonicalization standard. The validator supports the bounded schema subset used by this catalog, not arbitrary JSON Schema.

`mcp-contract --data-dir <path>` exports package identity/compatibility; `mcp-operations --data-dir <path>` exports the operation manifest. Both return metadata before host/database initialization. Package identity retains legacy full-contract/generation fields for older connections. A matching wire hash alone is not proof that a client has reconnected, that a transport has handed over, or that a complete update is activated. Those lifecycle checks belong to the transport/update procedure.
