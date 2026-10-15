## Why

The current UDP rollback session sends each handshake message and each input tick only once, so a single dropped datagram can prevent connection or leave an input hole that aborts an otherwise healthy match after the 12-tick rollback window. Out-of-order input can also replace the prediction baseline with older intent, including replaying one-shot shoot edges, making LAN play fragile under normal Wi-Fi packet loss and reordering.

## What Changes

- Retry `Join`, `JoinAck`, and `Ready` control messages until the corresponding handshake state is acknowledged or the existing handshake deadline expires.
- Carry a bounded history of recent input samples in every input datagram so later packets can recover isolated losses without waiting for explicit retransmission.
- Use acknowledgements to track contiguous remote progress and stop treating the highest isolated tick as confirmation of every earlier tick.
- Accept duplicate and out-of-order input idempotently, reject invalid match/seat/tick data, and update prediction state only from the newest accepted remote tick.
- Predict held movement and boost state while clearing one-shot shoot edges on unconfirmed ticks.
- Add deterministic unit and loopback integration coverage for loss, duplication, reordering, delayed delivery, handshake retries, and unrecoverable input holes.
- Preserve the existing behavior of aborting when missing input cannot be recovered before it leaves the rollback window.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `rollback-netplay`: Strengthen the existing LAN handshake, input transport, prediction, acknowledgement, and failure requirements so ordinary UDP loss, duplication, and reordering do not terminate or corrupt a recoverable match.

## Impact

- Runtime networking and rollback code in `LanUdpPeer`, `MatchDirector`, `RollbackSession`, and related timing/protocol constants.
- The UDP wire format and protocol version; peers running the old format will be rejected by version negotiation.
- EditMode protocol/rollback tests and new PlayMode or loopback transport integration tests.
- No new external package or service dependency is required.
