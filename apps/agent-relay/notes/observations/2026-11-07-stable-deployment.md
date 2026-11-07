# Stable v1.1.0 deployment to Pilotage AP

**Utility:** records the published package, the scoped cutover and the status of the live Codex chat check.  
**Usage:** distinguish successful package and host verification from the real chat round trip.

On 07/11/2026, the exact verified `agent-relay-1.1.0-win-x64.zip` was published from commit `7a94bd4d49f3eca410d6a7adc26807b77e56ffdb`. The downloaded ZIP had SHA-256 `46282f74be4a8860805d9066f4d1e99efd1b326ceab0e38f58e63072f5579cac`, and the downloaded checksum file matched the local file byte for byte. [Published release](https://github.com/Sowapps/agent-relay/releases/tag/v1.1.0)

A fresh authenticated connection to the deployed host returned version `1.1.0`, the scoped project and its preserved membership. The project and rights file matched byte for byte. All 22 preexisting work records, eight sessions and four bindings were compared and preserved.

The existing Pilotage AP Codex chat still loaded the older MCP catalog. Its verification stopped before opening a session or submitting a WSL request. The cutover receipt therefore records `awaiting-chat`. The actual successful native reply is recorded in the [isolated RC6 observation](2026-11-07-rc6-native-reply.md).

Copies of the package, publication and cutover receipts are indexed in `local/apps/agent-relay/evidence/2026-11-07-stable-qualification/SHA256SUMS.txt`. The manifest SHA-256 is `fa6611efce2caa270e348588bf04e8f70f29336b4805e446530fbc4f1cb2c55d`.
