## 1. Build the Three-Region Home Layout

- [x] 1.1 Add titled StartRegion, ServerRegion, and ExitRegion containers under MainPanel with distinct card boundaries, vertical spacing, and reserved shared error space.
- [x] 1.2 Place Start in StartRegion, HostLan/JoinLan in ServerRegion, and Exit in ExitRegion while preserving existing button names and behavior.
- [x] 1.3 Replace flat EnsureLanUi repair with idempotent region construction/legacy reparenting, and update reference resolution and listener wiring without duplicate objects or callbacks.

## 2. Preserve Navigation and Authored UI

- [x] 2.1 Verify local setup, host setup, join entry, Back, and direct Exit retain existing behavior and restore/hide all home regions together.
- [x] 2.2 Regenerate only the authored MainMenu through the existing baking path (add a menu-only entry if needed), preserving unrelated scenes and inspecting the serialized diff.

## 3. Test Hierarchy and Layout

- [x] 3.1 Add automated tests for action parentage, authored/runtime parity, and repeated legacy repair without duplicates.
- [x] 3.2 Retain or extend PlayMode navigation tests for Start, Host LAN, Join LAN, and returning to the complete three-region home menu.
- [x] 3.3 Visually verify the home screen at the Canvas reference resolution, 16:9, and 4:3, including a populated error message; fix clipping or overlaps.

## 4. Validate

- [x] 4.1 Run relevant Unity EditMode and PlayMode suites and check compilation/console errors.
- [x] 4.2 Validate split-main-menu-regions with OpenSpec and confirm its implementation checklist accurately reflects completed work.

Validation: EditMode 24/24 and PlayMode 9/9 passed at 1920x1080; Unity Console had no errors. The camera framing test initially failed in the editor's free-aspect Game View and passed without camera changes at the reference resolution. Visual captures at 1920x1080, 1280x720, and 1024x768 included long error feedback and showed no overlaps or clipping. Only MainMenu was baked; Arena was unchanged. OpenSpec strict validation passed.
