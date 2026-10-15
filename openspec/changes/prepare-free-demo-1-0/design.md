## Context

The current formal scenes use the approved Dash world, Chinese Noto font, minimaps and a single deterministic simulation. They contain user-authored art and a dirty worktree. Existing fault injection and rich smoke automation compile only in Development/Editor. Real two-PC acceptance is still pending, and must remain visibly distinct from loopback checks.

The user approved the identity 射球大战 / PocketBump, executable PocketBump.exe, developer Fnega, and original synthesized effects without external downloads or music. The requested final version is 1.0 in a new PocketBump1.0 directory.

## Goals / Non-Goals

**Goals:** Deliver a reviewable, playable free-demo Release candidate, clear newcomer flows, event-safe sound, useful production reports, and reproducible package/verification evidence.

**Non-Goals:** Gameplay/physics retuning, new network protocol, global LAN pause, reconnection, internet matchmaking, store upload, new maps/models/music, or claiming a physical-network test has happened on one machine.

## Decisions

1. **Independent presentation and reporting components.** Subscribe to confirmed events for audio; observe existing session counters for reports. No report/audio state enters the deterministic checksum. A report failure must never interrupt a match. Confirmed-event identities and per-epoch resets prevent rollback duplicates. Synthesize short sounds once and reuse clips; persistent volume is local only.
2. **Esc menu belongs to the director/HUD.** Local mode skips simulation advancement while the menu is open, without using global Time.timeScale. LAN continues Pump/Advance and sends neutral local input while the menu is open; its text explicitly says the match continues. Clear latched input on both transitions. Terminal network pause retains precedence. Leave uses existing clean-session shutdown.
3. **Chinese onboarding and readable controls.** Add an accessible quick-start card in the authored menu and match menu; use actual bindings in controls. New installs use top-row 2 for P2 Dash, existing valid preferences remain untouched. Show attack direction/opponent goal ownership and enhance scoring-ball contrast without changing collision geometry or camera rules.
4. **Production diagnostics remain useful in Release.** Save local version/build/rules identity, role, endpoints, tick/confirmed progress, lifecycle, errors and counters at bounded intervals and on failure/leave. Provide folder access. Do not include dev fault injection, developer state mutation or a hidden network authority path. Reports stay on the player's PC and are shared manually.
5. **Build once, then package that exact candidate.** Add an explicit Release build entry point, preserve Development mode for detailed regression. Use version 1.0, approved identity and a project-authored robot/ball icon. Bundle docs, license notices and a SHA256 manifest; exclude debug-only shipping artifacts. Never overwrite an existing release folder or zip.
6. **Final-build verification uses ordinary game entry points.** Run EditMode/PlayMode regression and comprehensive two-process Development diagnostics where required. Exercise the actual Release exe with ordinary UI/keyboard automation and production reports; verify Debug.isDebugBuild is false, launch/menu/local/LAN/leave behavior, identity and no runtime errors. Retain screenshots and exact tested hashes. Do not relabel companion Development evidence as Release evidence.
7. **Incremental authoring.** Back up the exact menu, arena, font and settings bytes before explicit migration. Add only release UI/components/glyphs and targeted ball/icon assets; do not call broad art bakers. Existing author-owned colors/transforms and meta identities survive. User can edit the saved scene content in Unity.

## Risks / Trade-offs

- [LAN menus cannot stop the peer] → Keep simulation/network running, suppress local control and label this behavior clearly.
- [Chinese glyph coverage/UI clipping] → Extend the approved font asset and verify actual windows at supported resolutions.
- [Audio confirmation delay] → Prefer correct one-shot confirmed feedback to duplicated rollback effects; keep clips short and volume adjustable.
- [Changed company/product creates a new Windows preference location] → Document the 1.0 profile change; do not silently edit unrelated registry keys. Valid bindings within the active profile remain preserved.
- [Release lacks developer drivers] → Use external UI automation and production reports on the exact shipped bits; retain full developer regression separately.
- [Reports expose local network endpoints] → Keep reports local, document their contents, and share only on user action.
- [Physical two-PC testing is unavailable to the agent] → Deliver a clear acceptance checklist and retain that task as pending for the user, not falsely completed.

## Migration Plan

Create source/tests and docs, back up and incrementally author scenes/font/settings, run regression, build an isolated candidate, validate the actual executable, then copy/package the exact candidate to the authorized new 1.0 destination. Retain prior versions and failed evidence. Rollback means launching the prior complete folder or restoring the recorded exact backups; no destructive cleanup.

## Open Questions

None blocking implementation. Distribution platform and public feedback address are not chosen; provide a portable Windows zip and instructions to return reports to the demo distributor. Physical two-PC acceptance is user-owned and outstanding.
