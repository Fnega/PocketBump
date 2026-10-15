## Why

LAN play currently stops sending input when the simulation reaches `MatchOver`, but continues applying the peer timeout; the result screen can therefore be mistaken for a lost connection. Rematch currently reloads only the local scene and sends a goodbye from the old director, so successful gameplay does not yet provide a reliable way for both players to finish and play another match together.

## What Changes

- Confirm a corrected, input-complete final result before enabling LAN rematch; continue recovering and reconciling late input while a result is provisional.
- Keep a healthy connection alive independently of gameplay ticks during results and rematch negotiation, without concealing missing gameplay input or genuine peer failure.
- Require an explicit rematch choice from both players, preserve negotiated seats and match settings, and start exactly one fresh match through a retried, idempotent transition.
- Make post-match navigation mode-aware, show waiting/confirmation status, and preserve the existing frozen-Arena diagnostic panel for actual LAN failures or peer departure. As explicitly selected by the user, LAN rematches keep the current rules; changing rules requires leaving the connection, returning to the main menu, and having the host create a new room. Local Modify Rules behavior remains unchanged.
- Isolate new matches from delayed traffic belonging to previous matches; reset per-match input, rollback, checksum, presentation, and diagnostic state deliberately.
- Extend automated coverage and the packaged two-process diagnostic runner to exercise result confirmation, idle results, repeated rematches, expected departure, and a fresh host/join session, with explicit evidence of the stages actually exercised.
- **BREAKING**: advance protocol compatibility from version 5 to version 6 for lifecycle messages and match-generation validation; reject older peers before gameplay rather than mixing incompatible session behavior.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `rollback-netplay`: confirmed match completion, independent validated liveness, reliable bilateral rematch, match-generation isolation, and lifecycle-aware compatibility.
- `menu-flow`: local versus LAN result actions, waiting feedback, and deliberate post-match navigation.
- `lan-diagnostic-runner`: stage-based lifecycle scenarios, fault injection, repeated-session evidence, and whole-scenario pass/fail reporting.

## Impact

- Runtime integration: `MatchDirector`, `RollbackSession`, `LanUdpPeer`, `LanHandshake`, `GameSession`, `MatchHud`, and a testable lifecycle coordinator if needed.
- Diagnostics and automation: `LanDiagnostics`, development-only `LanSmokeRunner`, `Tools/Run-LanDiagnostics.ps1`, and EditMode/PlayMode/socket regression tests.
- Presentation: authored and generated result UI must use the same mode-aware actions without changing arena art or gameplay rules.
- No collision, shot, scoring, stealing, input-binding, or balance changes. No in-session rule editing, disconnect recovery, authoritative state resynchronization, host migration, matchmaking service, or new dependency.
- Implementation will preserve existing releases and use the next available `PocketBump<version>` directory under `D:\uniry Assets\射球大战` for a major build. Same-machine automation and two-computer LAN playtesting will be reported separately.
