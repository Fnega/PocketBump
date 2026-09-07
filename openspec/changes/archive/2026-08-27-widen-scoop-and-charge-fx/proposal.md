## Why

The basket mouth is 0.02 narrower than a marble, so rim lips bounce a centered catch before the well trigger can collect. Charge needles grow from the player center and are mostly buried in the 1.56-diameter body, and collect/fire have no pulse, so scoop and shot both feel like nothing happened.

## What Changes

- Widen the U-mouth (and matching well) so the opening is clearly wider than a marble, with rim thickness unchanged. Body contact still does not collect.
- Anchor direction and power readings at the basket, outside the player mesh. The shortest power reading SHALL still extend past the body silhouette.
- Keep the locked direction reading visible during the power stage.
- Color the direction reading with that player's identity; use a high-contrast treatment for power (not gate yellow / dunk red).
- Pulse on successful collect (including steal) and on fire, reusing `GameFx`. No new VFX Graph.
- **BREAKING** for any test or authoring that assumes `BasketWidth` 0.74 or needles parented as short bars from the player origin.

## Capabilities

### New Capabilities

- (none)

### Modified Capabilities

- `marble-basket`: Empty-basket mouth is wide enough that a marble approaching the open face can overlap the well instead of bouncing off the rim lips.
- `visual-presentation`: Charge readings are readable outside the body; direction stays up in power stage; identity vs high-contrast coloring; collect and fire pulses.

## Impact

- `ArenaLayout` basket width (and well size derived from it). Length, rim, offset, and player radius stay unless play shows the well is still too shallow.
- `PlayerBasket` rebuild of visual, rims, and well after the new width.
- `PlayerChargePresentation` needle anchor, size, dual visibility in power stage, colors.
- `PlayerMotor` / collect and fire paths: one `GameFx.Pulse` each (steal uses the collect pulse on the new carrier).
- Authored Arena bake so baked baskets match the new width.
- EditMode/PlayMode tests that assume old basket size or that power hides the direction needle.
