# 1.0 verification record

## Scope and identity

- Approved identity: 射球大战 / PocketBump, Fnega, version 1.0, PocketBump.exe.
- Final candidate: `Builds/Demo1.0-20261014-candidate3`.
- Release GUID: `b224be1f86f740199b1db86c11ac4ef0`; non-Development build.
- Candidate 2 GUID `214028b2717a401bb763c9f7da1daf56` is retained but superseded by the final Windows executable identity correction.
- Separate Development companion: `Builds/Demo1.0-20261014-diagnostics2`, GUID `697041162f2d48ab938e5206871eb384`.
- Superseded candidate 1 GUID `02b570e3843843c7930970642f41270e` and companion 1 GUID `1d97997ca159441992425bdae77111aa` are retained for the failed-run investigation, not final acceptance.
- Protocol 7; rules hash `7012a8bc484f3d522ace17ccc550c9572659c5d0d2f96fa122a83fa27831abb8`.
- No store upload, Internet matchmaking, or physical two-computer acceptance is claimed.

## Source and authored-content checks

- `Tools/ReleaseResults/20261014-1.0/editmode.xml`: 582 passed, 0 failed/skipped.
- `Tools/ReleaseResults/20261014-1.0/playmode.xml`: 144 passed, 0 failed/skipped.
- Third-party notice tooling: 62 fixture checks passed. The first sandboxed junction-creation attempt was denied; rerunning with the authorized filesystem access passed.
- `prepare-r2.log`: identity, scene and font migration completed. Initial compile failed on two NUnit collection assertions; both were corrected before the successful full test runs.
- Exact pre-migration backups retained under `.art-source/backups/release-identity/20261014-120740-8c1f1709` and `.art-source/backups/free-demo-1-0/20261014-200740-509-00c4c12f`. The icon rebuild also retains its previous bytes in the release-identity backup area.
- `build-candidate1.log`: both Release and Development builds succeeded with 0 errors and 0 warnings.

## Actual Release UI evidence

Ordinary Windows UI/keyboard input, not Development gameplay hooks, drives the final executable. Failed attempts remain retained and are not counted as passed acceptance:

- `Tools/ReleaseDemoResults/20261014-121725-023-c8a45d`: harness failed because Windows PowerShell 5.1 does not recognize `[ushort]`; corrected to `[uint16]`.
- `Tools/ReleaseDemoResults/20261014-122124-540-29840c`: local sequence completed, then Windows refused foreground focus when switching between owned windows. The harness aborted without sending input to another application. No Player log exceptions were found. This is not a passed LAN run.
- Independent review of that second attempt's screenshots 01–08: Chinese help and punctuation readable, home identity visible, local split-screen attack labels/minimaps fit, Esc menu readable, menu exit restored the home screen. Paused images 05 and 06 are byte-identical despite intervening movement input; image 07 shows resumed movement and cooldown progression.
- Review limitations at this point: only 1280×720; scoring ball not in the main-world views; actual LAN/fault UI and display switching still to verify.

### Candidate 1 production-only findings

- `Tools/ReleaseDemoResults/20261014-122848-023-a51e6c`: actual fullscreen at 2560×1600 and restoration to 1280×720 succeeded. The 16:9 game image is letterboxed on this 16:10 monitor. The LAN peers entered live play, but the acceptance observer timed out because production report snapshots stopped updating after their first write. This run is not a passed acceptance.
- The retained peer failure snapshot records 2,527 simulation ticks, 2,518 validated checksums, zero send/presentation failures and `PEER LEFT` when test cleanup closed the other process. Thus stale report state must not be misclassified as a failed handshake or proof of a background-thread stall.
- `Tools/ReleaseReportProbeResults/20261014-123220-095/report-status-menu.png` shows the actual production report error: `UnauthorizedAccessException: Access to the path is denied.` The report files are EFS-marked; an isolated same-directory native replacement also failed with Windows error 17. The exact filesystem-layer cause is not assumed from the encryption attribute alone. No ACL, encryption, firewall or user licensing changes were made.
- The repair uses fresh-name publication and bounded retained generations, rather than assuming in-place replacement works on every report directory. Existing complete evidence survives failed publication. The actual Release harness must select the newest capture within a connection.
- The candidate 1 ball close-up also revealed that the added LineRenderer ring was not visibly rendered. Repair is restricted to this release's named marker, using a persistent upward-facing mesh and the existing dark material; no gameplay geometry or global palette changes. A rendered-pixel regression is added.

### Development companion 1

- Restricted-launch attempt `Tools/DashArenaAcceptanceResults/20261014-202535-287-bdba3a`: first case failed. Logs explicitly report low-integrity execution and PlayerPrefs write failures; retain, do not count as pass.
- Normal-access rerun `Tools/DashArenaAcceptanceResults/20261014-202701-538-48aa30`: 3/3 smoke cases passed (ordinary changing-input movement, first control-packet loss, expected protocol mismatch). The corrected final candidate will be separately tested again.
- Complete candidate manifest/path protection fixture: 14 checks passed, no skips, under Windows PowerShell 5.1 with the owned junction fixture permitted. The earlier sandboxed fixture run passed 13 and explicitly skipped junction creation.

### Reference consulted for the report-write investigation

Microsoft's [ReplaceFileW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-replacefilew) and [MoveFileExW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-movefileexw) documentation informed the isolated probes. The selected production repair does not change ACLs or disable encryption and does not depend on those native replacement calls.

## Outstanding verification

- Physical two-computer LAN and subjective speaker/audio balance remain user checks.
- Actual Release UI acceptance, the separate Development regression and final packaging are complete; see the final evidence sections below. Watermark origin/authorization remains unresolved before public release.

### Resolved firewall permission and desktop blockers

- `Tools/ReleaseDemoResults/20261014-131508-190-5640cb`: final candidate 3 completed ordinary menu/help, fullscreen/window restoration and local play/pause/resume, but did not pass LAN acceptance. `failure-second.png` shows a Windows Security firewall permission dialog over the join-side UI. No matching join-side production session report exists; the host recorded no received datagrams and ultimately LAN HANDSHAKE TIMEOUT. This is not evidence of an established match disconnect or a passed Release LAN test.
- The candidate's local ball close-up visibly renders the corrected dark ring. Local pause screenshots 05/06 have identical hashes despite movement input; resumed screenshot 07 differs. Main menu/help and both split-screen minimaps/direction labels were reviewed.
- At that point no firewall rules or Windows security settings had been changed. The user subsequently explicitly approved Private-profile UDP 7777 rules for the candidate and final executable.
- `Tools/ReleaseResults/20261014-1.0/firewall-private-20261014-134857-*.json` records the completed administrator-confirmed setup. Exactly two enabled inbound Allow rules were added, program-scoped, Private only, UDP 7777, remote LocalSubnet/127.0.0.1, edge traversal blocked. ActiveStore readback confirms both. Existing rules, network categories and firewall enablement were not changed.
- Before setup, this machine's WLAN was already Public and the candidate already had two Public-profile query-user Allow rules. Those pre-existing rules were recorded and left untouched; the new Private rules do not imply this WLAN was changed to Private.
- `Tools/ReleaseDemoResults/20261014-134947-577-ea6f13`: retry stopped after the owned window became invisible/minimized. `Tools/ReleaseDemoResults/20261014-135207-059-5a985d`: bounded restoration allowed some progress, but repeated foreground switches to java PID 46112 disrupted fullscreen verification. Read-only executable-path inspection identified the process under the user's Minecraft runtime directory. No Java process or other application was modified or closed; no Player log exceptions were found. Neither attempt passed acceptance.
- The user subsequently confirmed they had been interacting with the desktop and would leave it idle for the retry. The final run below passed without failed screenshot attempts; no other application was closed or modified. Test players exited automatically.

## Corrected source regression

- `prepare-r3.log`: marker mesh persisted and formal arena upgraded successfully; additional exact backup `.art-source/backups/free-demo-1-0/20261014-203910-430-798b733a`.
- `editmode-r2.xml`: 586/586 passed, no failed or skipped tests, with graphics enabled. This includes actual marker pixels, mesh winding, non-destructive marker migration and generation-based report storage/retention/recovery.
- `playmode-r2.xml`: 146/146 passed, no failed or skipped tests. Includes two additional real-storage-failure/recovery UI tests; recovered automatic errors disappear without dismissing an unrelated manual directory-open error.
- `build-candidate2.log`: corrected Release and separate Development companion both built with 0 errors and 0 warnings. Candidate 1 is superseded and must not be packaged.
- `Tools/ReleaseDemoResults/20261014-124910-259-efea68`: candidate 2's first UI attempt captured a valid main menu, then an all-black help screenshot. The strict harness stopped rather than accepting it; no Player exceptions were reported. Desktop/focus availability is being checked, and this attempt is not counted as passed.

## Observed limitations

- The built window contains a small `trial version` watermark at the bottom right. It was observed in screenshots; its licensing/build-environment origin has not been resolved. No licensing configuration or watermark was bypassed or removed.
- A new Fnega/PocketBump Windows preference location may not inherit settings from older development identities; the player guide explains this.
- LAN uses direct local-network connectivity, not Internet relay/matchmaking. Opening a LAN menu does not pause the other player.

## Final packaging audit findings

- Candidate 2's runtime/menu identity is correct, but Windows file-version properties still show the original Unity launcher information. Querying its actual version-resource translation confirmed this is not a display-language fallback. A Release-only build step is being verified on isolated copies before producing a new candidate; it must preserve every non-version resource and the original Unity copyright, and reject certificate-bearing executables.
- The initial 11-file notice list covered art/fonts but omitted some libraries actually present in the player. The final list is being expanded against the exact installed package versions and locked MCPForUnity commit, without removing player files or inventing license text.
- Resource-update behavior reference: Microsoft [UpdateResourceW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-updateresourcew). This changes only the new unsigned launcher metadata, not the Unity engine DLL, license configuration or observed watermark.
- `Tools/ReleaseResults/20261014-1.0/windows-version.xml`: 7/7 additional EditMode tests passed, no failures or skips. Actual Windows properties, preserved non-version resources/copyright, and rejection of unexpected names/versions, repeat stamping, certificate-bearing files, locked outputs and non-PE files are covered using isolated copies.
- `build-candidate3.log`: final Release built successfully with 0 errors and 0 warnings. Its Windows EXE properties show Fnega / PocketBump / 射球大战 / PocketBump, product version 1.0 and file version 1.0.0.0. The original Unity copyright remains. Runtime assembly SHA256 is unchanged from candidate 2: `2FB8870FAA86B47D12D73B54C559B586BCF7F5329FBF9442D07100721B8CF73E`; no gameplay code changed for this build-only correction.
- The final notice inventory includes 44 original license/notice/source-record files, with 198 fixture checks passed. Exact installed Unity package versions, the locked MCPForUnity commit, and Onigwrap 1.0.10 provenance are retained in `Tools/ReleaseNotices/RuntimePackages-SOURCES.txt`. Onigwrap's upstream binary and Unity-signed binary have identical raw PE sections and identical normalized pre-certificate payloads; only read-only comparisons were performed. Matching upstream license and third-party notices were preserved verbatim. This resolves the specifically identified native notice gap without removing or modifying the signed library.
- `Tools/ReleaseDemoResults/20261014-131058-051-e6524e`: candidate 3's first ordinary-UI attempt passed menu/help/display switching, then lost foreground focus at local play. No Player log errors were found. Input and failure captures were stopped by ownership/foreground guards; this is not a passed acceptance. A bounded stable-focus retry is restricted to activation only, not replaying gameplay input.

## Final Development companion regression

- Diagnostics 2 (GUID `697041162f2d48ab938e5206871eb384`) passed all 8 selected same-machine two-process cases. Its runtime assembly SHA256 is `D2DC4D5578B747CB2AAFB79EAD9EF9D6EBA6ED01E320A60E94C0AD0E1E66F69C`; these are Development results, not actual Release UI or physical-LAN results.
- `Tools/DashArenaAcceptanceResults/20261014-205213-118-9300c1/acceptance-index.json`: 3/3 smoke cases passed: movement, first control-packet loss and expected protocol mismatch.
- `Tools/DemoDevelopmentResults/20261014-125538-272-d137b7/acceptance-index.json`: 5/5 deeper cases passed: host stall, join stall, natural full match, three rematches followed by host departure/rejoin, and the corresponding join departure/rejoin case.
- Each stall lasted approximately 0.75 seconds; the non-stalling peer observed wait then resume, and both agreed at the recovery checkpoint. Natural matches completed 8:1 at tick 2663 with matching peer checksums. Lifecycle cases each completed three natural results, held each result for at least 8 seconds, agreed on three rematches, and established a fresh connection after the expected LOCAL LEAVE/PEER LEFT outcomes.
- Send/presentation error counts were zero, and no Exception/Assertion/Crash log patterns were found. All diagnostic players were confirmed exited before releasing the test port for the actual Release run.

## Final actual Release acceptance

- `Tools/ReleaseDemoResults/20261014-142150-277-11136e/acceptance.json`: passed, completed 2026-10-14T14:23:12Z, ordinary UI/input, same-machine two-process scope. Uses final Release GUID `b224be1f86f740199b1db86c11ac4ef0`, not the Development companion.
- All 162 payload files were bound before/after the run and independently rehashed. Manifest SHA256: `0E0F88FE70B41784C9413AC43786EEEC413C70FCB3EF3A411C572F50AAC82C55`. EXE SHA256: `847865BFAC0C418101A4FA219546E3C99BD4F59F8C92BE4DDB424AB8026210A8`; runtime SHA256: `2FB8870FAA86B47D12D73B54C559B586BCF7F5329FBF9442D07100721B8CF73E`.
- First match `188985700`: host/join tick 303/301, validated checksums 290/294. With the host's personal menu open, its tick progressed 303 to 603 and validations 290 to 575. Host departure yielded peer-visible PEER LEFT, not a silent return to menu.
- Reversed roles established a distinct match `1655772263`: host/join tick 302/303, validations 300/296. Join departure yielded host-visible PEER LEFT. Nine reports agree on Release identity/protocol; both peer-left reports show their socket closed.
- All reports have zero send/presentation errors and desync tick -1. No Player log exception/assertion/crash patterns were found. A short join-side input wait recovered (maximum about 41.5 ms); this is not a claim of zero waits. Snapshots at different ticks are not compared as if their raw checksum integers should match.
- Nineteen screenshots passed first attempt. Independent screenshot inspection covered menu identity, readable Chinese help, P2 top-row 2 default, window/fullscreen restoration, dark scoring-ball marker, split-screen and LAN minimaps/direction labels, local pause/resume, the LAN-continues menu caption, report buttons and bottom-right peer-left panels. Fullscreen at 2560x1600 has 16:9 letterboxing and an external NVIDIA notification; game controls remain visible. `trial version` watermark remains noted, not removed.
- Paused images 05/06 are byte-identical despite movement input (SHA256 `5C6C2DC1EC7EDCE7075519551D711306DCF95DF842974B9374D12F5E830D19C4`); resumed image 07 differs (`51E775C9A4C5A9E575E2325F7E51578766DA9CF8ECD0A79125BF81F25D135D29`) and shows movement/cooldown progress.
- This section records the subsequent visual review; immutable automated acceptance fields `local_visual_review_pending` and `audio_listening_review_pending` are not rewritten. Subjective listening and physical two-PC acceptance remain pending. Automated success is not proof of universal LAN reliability.

## Resolved packaging pause: Windows PowerShell ZIP path separators

- The first packaging attempt created the new `D:/uniry Assets/射球大战/PocketBump1.0` directory and `PocketBump1.0.zip` but failed its final ZIP lookup. Windows PowerShell 5.1's `ZipFile.CreateFromDirectory` produced entry names with backslashes; the verifier looked up slash-separated names. No game build or accepted payload was changed, and no prior version was overwritten.
- Read-only independent inspection verified all 209 manifest-listed disk files and all 210 ZIP entries (including the manifest) after normalizing separators for comparison. Their content hashes match. This identifies a ZIP path compatibility/validation failure, not missing payload bytes, but does not convert the failed packaging workflow into a successful receipt.
- The user explicitly approved correcting the ZIP writer and renaming the unaccepted outputs to recoverable backups before regeneration. `Tools/ReleaseResults/20261014-1.0/package-backup-receipt.json` records the verified rename to `PocketBump1.0-packaging-backup-20261014-01` and its matching `.zip` in the same delivery parent. Backup ZIP SHA256 remains `84A4766A5E8DCE6DFC75A230E83169212DF4B6988B6FE1635DF9ACD8CB805470`; no prior game version was overwritten or deleted.
- `Tools/ReleaseZipArchive.ps1` now creates entries with explicit forward-slash names, preserves a single source-folder root, rejects existing destinations/source-contained outputs/reparse paths, and closes all streams even on failure. The package verifier keeps its strict slash lookup and per-entry hashes; it does not silently accept an unverified archive.
- The first isolated clean Windows PowerShell 5.1 test exposed the need to load `System.IO.Compression` explicitly; this was corrected in the helper before successful regeneration. `Tools/ReleaseResults/20261014-1.0/zip-fixture.json` records the final 73 passed assertions under PowerShell 5.1.26100.9444, including nested/Chinese/space names, empty/binary/hidden files, hashes, no overwrite, and directory junction/ancestor rejection. One file-symbolic-link branch (five assertions) could not run because existing Windows policy requires privilege (native error 1314); it is explicitly skipped, not counted as passed. No security policy was changed.

## Final delivery

- `Tools/ReleaseResults/20261014-1.0/package-receipt.json`: successful packaging exit 0, version 1.0, final accepted Release GUID `b224be1f86f740199b1db86c11ac4ef0`.
- Delivered directory: `D:/uniry Assets/射球大战/PocketBump1.0`; program: `PocketBump.exe`. Delivered archive: `D:/uniry Assets/射球大战/PocketBump1.0.zip` (46,683,634 bytes).
- ZIP SHA256: `2898D8B32C977A62E518566E0080E6B6F0C2D8C6F35C64D2696E66E176A8EC05`.
- 210 files total: 162 accepted game payload files, 44 original third-party notice/source records, three Chinese player/acceptance/verification documents, and `SHA256SUMS.txt`. All copies and archive entries were hash-verified, and the game payload matches the exact accepted manifest. No rebuild or runtime file edit occurred after actual Release acceptance.
- Delivered EXE Windows properties independently read back as Fnega / PocketBump / 射球大战 / PocketBump / product 1.0 / file 1.0.0.0. The original Unity copyright and unmodified engine/native libraries remain intact.
- A separate read-only final delivery audit confirmed all 209 manifest entries, all 210 unique slash-separated ZIP entries, all 162 accepted payload hashes, and the final ZIP digest. `build-info.json` and `boot.config` agree on the accepted GUID; the EXE debug flag is false. No `_DoNotShip`, test/tool directories, PDB/MDB files, WinPix or test logs are included. No game process remained running after handoff checks.
- Task 4.3 is complete; task 4.4 remains unchecked because the user/friend must provide the physical two-PC results. No store upload, public release, OpenSpec archive, universal LAN guarantee or subjective listening acceptance is claimed.
