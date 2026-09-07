## Context

Players are rotation-frozen circles. Marbles score when any `Marble`-tagged collider enters a north/south pocket. Boost is hardcoded Left/Right Shift. There is no facing, no hold state, and no launch action — the only way to send a marble is a body collision.

This change adds a front basket, carry/steal, and a two-stage charge shot, plus menu rebinding for the four skill keys. Movement, dunks, kickoff, table size, and win-by-two stay.

## Goals / Non-Goals

**Goals:**

- Each player has a square basket that faces movement input and can hold one marble.
- Basket overlap collects; body overlap still bounces (including dangerous knockback).
- Held marble does not score in north/south pockets; free marbles still do.
- Held marble is launched only by a two-stage charge shot (direction while mobile, power while frozen), then recoil.
- Opponent basket overlapping a held marble transfers carry. Body checks do not.
- Default P1 `J`/`K`, P2 `Alpha1`/`Alpha2`; rule setup shows and rebinds those four keys.

**Non-Goals:**

- Remapping WASD / arrow movement.
- New Input System package.
- Gamepad.
- Changing dunk, kickoff, marble count, or pocket sizes.
- Charging without a held marble, or dumping/throw without the charge sequence.
- Last-touch scoring (pocket owner still gets the point).

## Decisions

### 1. Facing is last non-zero movement input

Basket local-forward follows `ReadInput()` when the stick is non-zero. When input is released, facing stays. Kickoff spawn: P1 faces south, P2 faces north. Player rigidbody rotation stays frozen; only the basket child rotates in the XY plane.

**Why:** Matches “推哪，篮子就朝向哪” without unlocking physics rotation (which would torque on walls).

**Alternative:** Face velocity — rejected; coasting would swing the mouth after you stop steering.

### 2. Basket is a child: rim colliders + inner trigger

A square visual sits in front of the circle. Three thin rim colliders (U-shape, mouth forward) collide with marbles and walls. An inner trigger detects collect/steal. Size: mouth wide enough for one `MarbleRadius` (0.3) with a little margin; offset so the mouth is clearly ahead of the body circle.

**Why:** Body and basket are different surfaces. Occupied mouth still has rims, so a second marble bounces instead of stacking.

**Alternative:** Magnet trigger with no rims — easier catch, but then “框碰到才算收集” is a volume, not a scoop.

### 3. Carry is a flag + kinematic parent, not a second physics marble

On collect: set `MarbleMotor.Carrier`, parent to the basket well, `Rigidbody2D` kinematic, velocity zero. Skip `MarbleMotor` knockback while carried. On steal: re-parent to the other basket. On shot or round clear: unparent, dynamic, restore drag.

Scoring: `MatchDirector` ignores pocket entry when `Carrier != null`.

**Why:** A live dynamic marble in the well would rattle, double-hit, and score in the pocket the player cannot enter. Kinematic carry is the smallest honest “珠子在框里不动”.

**Alternative:** Joint — more jitter, more failure modes.

### 4. Two-stage shot is two `GetKeyDown` readings on the shoot binding

Must be carrying. Ignore shoot if empty.

1. **Direction.** Player may move. A needle oscillates around current facing. Next shoot-down locks world direction = facing at that instant + needle offset. Facing still follows input until the lock.
2. **Power.** Zero the body velocity and ignore movement/boost for this phase. A needle oscillates 0…max. Next shoot-down fires: unparent marble, give it speed along the locked direction from min…max shot speed, apply recoil impulse on the player opposite that direction (scaled by locked power).

Require key release between downs. Movement input during power **cancels** the charge and keeps the marble. Steal or round end **cancels** the charge. Emptying the basket any other way cancels too.

**Why:** User asked for two 判定 with different mobility. Oscillating needles make each press a reading, not a hold. Freeze must actually zero velocity because current movement still coasts after key-up.

**Alternative:** Lock facing with no needle, hold for power — weaker “判定”, rejected. Fail-the-shot on move during power instead of cancel — harsher; cancel keeps the ball.

### 5. Steal is collect-trigger vs carried marble

If player B’s inner trigger overlaps a marble whose `Carrier` is A, transfer to B (A’s charge cancelled). B must not already be carrying. No cooldown beyond the physics overlap (one transfer per contact is enough if we ignore stay-repeats until exit).

Body–body and body–held-marble do not steal. Empty basket vs flying marble is a normal collect.

**Why:** User replaced bump-eject with scoop-steal.

### 6. Bindings stay on old `Input.GetKey`, stored aside from match rules

`ControlBindings` (or equivalent) holds four `KeyCode`s. Defaults: P1 shoot `J`, boost `K`; P2 shoot `Alpha1`, boost `Alpha2`. Persist with `PlayerPrefs`. Movement keys stay hardcoded.

Rule setup lists P1/P2 move (read-only) and four rebind buttons. Click → listen for next `KeyCode`; Escape cancels listen without closing the panel; reject mouse, reject a key already used by any of the four skill bindings or by either player’s movement keys.

Match HUD pregame banner prints the current four skill keys plus WASD/arrows.

**Why:** No Input System in the project. Keys are machine preference, not points-to-win. P2’s `1`/`2` vs arrows is awkward on purpose — rebind is the escape hatch (numpad or nearby keys).

**Alternative:** Put shoot on Shift and drop boost — rejected; user named four distinct keys.

### 7. Shot speeds and recoil live next to `ArenaLayout`

Tuneable constants (starting points, not spec numbers): basket offset ~0.7 in front of center; shot speed from ~8 to ~22; recoil impulse scaled so a full-power east shot can threaten a west dunk if the player is already near that wall. Direction needle ±18°; both needles ~1s period.

**Why:** Feel will need Play Mode passes; keep one place to retune.

## Risks / Trade-offs

- [P2 default 1/2 is far from arrows] → Show bindings in the menu; rebind is in scope.
- [Power freeze is a steal window] → Intended. Charge next to the opponent is a risk.
- [Recoil into dunk] → Intended self-risk on east/west shots.
- [Oscillating needles feel like a minigame on a physics table] → Accept; user asked for two 判定.
- [Basket rims catch on walls/gate] → Keep basket short; ignore basket vs dunk/marble-pocket blockers the same way the player body already does, or shrink the mouth so only the circle hits walls. Prefer: basket collides with walls/marbles/players, not with pocket triggers.
- [Carried marble still tagged Marble] → Must gate scoring on `Carrier`, not tag alone.
- [Rebind listen vs Escape-to-back] → Listening state swallows Escape.

## Migration Plan

1. Add bindings store + menu rows; switch boost reads to bindings (defaults K/2).
2. Add basket child, facing, collect/carry.
3. Gate pocket scoring on carry; steal; catch vs body bounce (body already bounces).
4. Charge state machine, shot launch, recoil, cancel rules.
5. Presentation (basket, needles) and HUD banner.
6. Tests, then Play Mode scoop / steal / charge / pocket.
7. Rollback: revert this change folder’s code; bindings PlayerPrefs keys can be ignored.

## Open Questions

- None. Direction and power are oscillating needles; move during power cancels; movement keys are not remappable; number-row `1`/`2` are the P2 defaults (`Alpha1`/`Alpha2`, not keypad).
