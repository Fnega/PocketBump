## Context

Player radius is 0.78 (mesh scale 1.56). The basket sits at offset 1.08 with `BasketWidth` 0.74, `BasketLength` 0.56, `BasketRim` 0.08. Inner mouth between rims is 0.58; marble diameter is 0.60. Collect is well-trigger only; rims are solid `BoxCollider2D`. A marble aimed at the open face often hits rim lips and bounces before the well overlaps.

Charge needles are player children: direction length 1.15 / width 0.08 (gate yellow), power length 0.35–1.45 / width 0.12 (dunk red), emission `color * 0.32`. Most of the bar sits inside the sphere. Power stage hides the direction needle. `GameFx.Pulse` exists for goals, dangerous impacts, and the kickoff gate; collect and fire do not call it.

`enlarge-player-and-smooth-turn` left well size unchanged on purpose. That trade-off is now in scope.

## Goals / Non-Goals

**Goals:**

- Mouth opening wider than a marble so a front-on scoop can reach the well instead of bouncing off the lips.
- Direction and power readings anchored at the basket, sized so the shortest power bar still reads outside the player silhouette.
- Locked direction remains visible during the power stage.
- Direction reading uses that player's identity color; power uses a high-contrast treatment that is not gate yellow or dunk red.
- `GameFx.Pulse` on successful collect (including steal) and on fire.

**Non-Goals:**

- Collect on rim hit or a magnet volume in front of the mouth.
- VFX Graph, particles, or screen-space charge HUD.
- Changing facing turn rate, player radius, mass, speeds, marble radius, basket length, rim thickness, or offset.
- Auto-aiming the basket at nearby marbles.

## Decisions

### 1. Widen width 1.5×; keep length, rim, and offset

`BasketWidth` 0.74 → **1.11**. Inner opening 1.11 − 2 × 0.08 = **0.95** vs marble diameter 0.60. Well width follows the existing formula (`BasketWidth - BasketRim * 2.2` → ~0.93). `BasketLength` 0.56, `BasketRim` 0.08, `BasketOffset` 1.08 stay. Rebuild visual, rims, and well from those constants (baker already does this).

**Why:** Same scale factor as the body, so the U matches the sphere. Opening is no longer smaller than the marble. Length/offset stay so the well does not clip the body or grow into walls.

**Alternative:** Only bump width to ~0.80 (opening 0.64) — technically “wider than the marble,” still a grazing catch. Rejected; 1.5× is the deferred option from the enlarge change. Collect-on-rim — rejected; user asked for a wider mouth, not a rule change.

### 2. Needles live on the basket, grow forward, compensate parent scale

Parent charge bars under the basket (already `localScale = 1 / playerScale`). Place them along basket local +Y (facing) starting at the well. Direction: identity color, width **0.28**, length **1.8**. Power: high-contrast (`VisualPalette.Text` / off-white, emission ≥ 1), width **0.36**, length **1.0 + power × 1.2** (min 1.0, max 2.2). Lift toward −Z more than `NeedleLift` 0.18 if the basket mesh z-fights (try ~0.28).

**Why:** Basket is already outside the sphere. Parenting there avoids the player inverse-scale fight and keeps the bar in front of the mouth.

**Alternative:** Keep player-parented bars and add an origin offset — more scale bugs. HUD bars — two players share one screen; world-space at the shooter is unambiguous.

### 3. Power stage shows both needles

Direction stage: direction needle only, still tracking the oscillating shot preview. Power stage: direction needle frozen on `LockedShotDirection`; power needle on the same axis, wider and closer to the camera so it does not fully occlude the identity line.

**Why:** The two-press charge is two readings. Hiding direction after lock forces the player to remember the angle.

**Alternative:** Side-by-side offset bars — noisier. Fan/arc for ±18° — later if a stick is still too weak.

### 4. Pulses reuse `GameFx.Pulse`

On `Attach` (collect or steal): pulse at the **new** carrier's basket, identity color, short (e.g. 0.4 → 1.6, ~0.28s). On fire: pulse at the launch point, high-contrast or identity, slightly larger (e.g. 0.5 → 2.0, ~0.32s). No pulse on body bounce, occupied-basket reject, or charge cancel.

**Why:** Same helper as goals/impacts. Steal is a collect from the thief's point of view.

**Alternative:** Mesh ring — only if the sprite pulse reads as paper under the perspective camera after play.

## Risks / Trade-offs

- [Wider mouth also makes steal easier] → Accept; scoop and steal share the well. Do not add a steal cooldown.
- [1.11 width may graze walls near dunk pockets] → Offset unchanged; play-check east/west. Shrink toward 0.96 opening only if it snags.
- [Two needles on one axis can merge visually] → Power is wider, brighter, and closer to camera; identity line stays thinner.
- [`GameFx` sprite pulse looks flat next to meshes] → Reuse first; swap to a short mesh ring only if play says it is invisible.
- [Empty `catch-on-basket-contact` change still sits in `openspec/changes`] → Ignore this change; archive or delete that scaffold separately.

## Migration Plan

1. Set `BasketWidth` 1.11; rebuild basket visual/rims/well.
2. Reparent and retune charge needles; keep direction up in power stage; identity vs high-contrast colors.
3. Pulse on attach and fire.
4. Bake Arena.
5. Play: front-on scoop collects instead of rim bounce; needles read outside the body; collect and fire flash.
6. Rollback: restore 0.74 width and player-centered short needles; remove the two Pulse calls.

## Open Questions

- None. Mouth geometry, needle anchor, dual visibility, colors, and Pulse reuse are locked.
