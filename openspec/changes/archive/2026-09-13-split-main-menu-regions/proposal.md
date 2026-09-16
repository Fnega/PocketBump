## Why

The main menu currently groups local start, LAN host/join, and exit in one undifferentiated panel. Separating them into three visibly distinct regions on the same home screen will make each action's purpose clear without adding navigation steps.

## What Changes

- Split the home screen into a Start region, a Server region containing Host LAN and Join LAN, and an Exit region.
- Give each region its own named UI container, heading, spacing, and visual boundary; keep all three visible together on the home screen.
- Preserve existing local rule setup, LAN host rule setup, join-address input, return navigation, and direct quit behavior.
- Keep authored MainMenu scenes and runtime UI generation/repair consistent so legacy repair does not recreate the old mixed layout.
- Keep error feedback readable without overlapping the three regions.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `menu-flow`: Require three independent home-screen regions with existing actions grouped by purpose and unchanged navigation behavior.

## Impact

- `MainMenuController` UI construction, reference resolution, legacy repair, and button binding.
- Authored `Assets/Scenes/MainMenu.unity` and the existing scene-baking workflow.
- Menu hierarchy/layout and navigation regression tests.
- No new server discovery, dedicated-server functionality, exit confirmation, dependencies, or gameplay changes.
