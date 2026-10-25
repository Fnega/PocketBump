## ADDED Requirements

### Requirement: Handfeel candidates preserve the released baseline and require final acceptance

The system SHALL retain the released Production V1 rules, content identity, layout identity and legacy simulation behavior as a comparison baseline. Following approved 2026-10-25 feedback, the current handfeel candidate SHALL use a distinct immutable content identity, with a six-tick initial recoil stage at the existing decay rate of 8 and a tail decay multiplier of 6. Its geometry and unrelated gameplay rules SHALL remain unchanged. The earlier multiplier-3 candidate SHALL remain reproducible as a test comparison with its distinct identity. The candidate SHALL NOT become the release-default production profile until the user accepts the actual comparison experience; automated test success SHALL NOT substitute for this acceptance.

#### Scenario: Candidate identity does not silently replace V1
- **WHEN** V1 and the candidate are created with the same score target and ball count
- **THEN** their geometry and layout hashes match, their content identities differ, and V1 retains its existing content hash and behavior

#### Scenario: Tests pass before a user has tried the candidate
- **WHEN** automated simulation and presentation tests pass but final user handfeel acceptance has not been recorded
- **THEN** the candidate remains separately selectable for development validation and the release-default production profile remains unchanged

### Requirement: Ordinary ball assistance scales with pre-contact active approach

For an eligible new, closing, non-Dash player-ball contact, the additional ordinary ball velocity SHALL be the existing NormalShotImpulse multiplied by a weight and the existing green multiplier. With n pointing from player to ball, the weight SHALL be clamp01(min(max(0, ControlVelocity dot n), max(0, TotalPlayerVelocity dot n), max(0, (TotalPlayerVelocity - BallVelocity) dot n)) / NormalSpeed). Every operand SHALL be captured before this contact's base collision response changes velocity. Only this additional ordinary velocity SHALL be scaled; the base solver, contact normal, separation rules and existing speed cap SHALL remain unchanged.

#### Scenario: Gentle direct touches receive proportionally smaller assistance
- **WHEN** an unbuffed non-Dash player with matching control and total velocities of 2, 6 and 12 approaches a stationary ball head-on under the current normal speed of 12
- **THEN** the respective additional velocities are 4 times 2/12, 4 times 6/12 and 4, applied along the contact normal after the unchanged base response

#### Scenario: A passive incoming ball does not manufacture an ordinary shot
- **WHEN** a closing ball hits a stationary player, a purely externally driven player with zero control projection, or a player whose actual total velocity is away from the ball
- **THEN** the ordinary assistance weight is zero while the applicable base collision and dangerous-ball response still execute

#### Scenario: Opposing control and recoil cannot fake forward approach
- **WHEN** the player's control projection is +12, external projection is -15 and the incoming ball projection is -10 along n
- **THEN** the player's actual projection is -3 and the ordinary assistance is zero despite the positive relative closing speed

#### Scenario: Chasing and grazing use actual available approach
- **WHEN** a player chases a slower ball in the same direction or touches it obliquely
- **THEN** the assistance is limited by all three pre-contact normal projections and cannot exceed the original ordinary additional velocity

### Requirement: Existing heavy impacts and contact contracts remain intact

Valid Dash ball contacts SHALL retain the existing fixed DashShotImpulse of 12 before the unchanged green multiplier and ball speed cap; the ordinary approach weighting SHALL NOT be applied to Dash. Ordinary normal-speed head-on contacts with a stationary ball and no opposing external motion SHALL retain the existing baseline. Green SHALL retain its 1.5 multiplier on eligible additional gameplay velocity rather than on total resulting ball velocity. Player-player first impulses, dangerous-ball threshold and first response, masses, restitution, static responses, ball drag, and the once-per-contact assistance latch SHALL remain unchanged. The system SHALL NOT introduce collection, automatic aiming, a shooting action, or repeated assistance during sustained contact.

#### Scenario: Full-speed and Dash references are preserved
- **WHEN** the same isolated V1 and candidate fixtures perform normal full-speed or valid Dash contact with a stationary ball
- **THEN** their immediate ball velocity responses match for those fixtures, including the unchanged green handling and existing final speed cap

#### Scenario: Saturated green Dash respects the old cap
- **WHEN** a green-enhanced Dash would exceed the existing maximum ball speed
- **THEN** the same cap applies and no assertion requires the capped final velocity to equal 1.5 times the unbuffed total velocity

#### Scenario: Continuous contact cannot inject assistance each tick
- **WHEN** a player remains in the same player-ball contact episode across multiple solver calls or ticks
- **THEN** the existing latch prevents repeated additional shot velocity until the existing separation condition ends that episode

### Requirement: Player contact assistance is reduced without rescaling base physics

The current candidate SHALL set NormalPlayerImpulse to 4.5 and DashPlayerImpulse to 10.5, reducing only the previous 6 and 14 additional player-contact amounts by 25%. Base collision impulses, masses, restitution, caps, green multiplication, purple immunity, contact latches and player control SHALL remain unchanged. Normal/Dash ball assistance, dangerous-ball impact and static responses SHALL NOT be weakened by this tuning. The released V1 and reconstructed older candidates SHALL retain their old values and identities. This additional-impulse reduction SHALL NOT be described as a 25% reduction of total knockback.

#### Scenario: Unbuffed ordinary head-on players receive the approved smaller addition
- **WHEN** two normal players approach at speed 12 each with zero initial external velocity and no cap clipping
- **THEN** each receives external speed 27.3 instead of 28.8, while the original basic response and control velocity are preserved

#### Scenario: Dash head-on reduction leaves ball response intact
- **WHEN** two unbuffed players Dash toward each other at speed 24 each without cap clipping
- **THEN** each receives external speed 56.1 instead of 59.6, and separate player-ball fixtures retain their previous candidate responses

#### Scenario: Existing immunity and contact episodes still apply
- **WHEN** mirrored contacts include green, purple or an already latched pair
- **THEN** the same green multiplier and purple immunity apply to the revised additions and sustained contact does not repeatedly apply them

### Requirement: Dynamic recoil uses six original-decay updates before the faster tail

The decay mechanism SHALL NOT itself rescale accepted dynamic collision impulses; player-contact additions follow the separately approved reduction above. It SHALL represent the remaining initial-decay stage in authoritative player state. A qualifying impact at tick T SHALL set six remaining updates. In subsequent normal external-motion updates T+1 through T+6, ExternalVelocity SHALL move toward zero at 8/60 per update before decrementing that counter; beginning with T+7 it SHALL use 48/60 until zero or a qualifying new impact. This shared tail SHALL apply to both player-player and dangerous-ball knockback as approved. These bounds assume no intervening paused phase or new impact. The decay SHALL NOT change ControlVelocity, normal braking, input eligibility, collision coefficients or stun duration, and SHALL NOT cross through zero. Player-player contact SHALL NOT introduce stun or an input lock. V1 SHALL retain its original constant decay.

#### Scenario: The sixth and seventh recoil updates have distinct rates
- **WHEN** a candidate player receives a qualifying impact leaving external speed 12 and then advances seven uninterrupted external-motion updates without another collision
- **THEN** the first six reductions match V1's 8/60 reductions and the seventh uses 48/60, while the original impact velocity is unchanged

#### Scenario: Faster feedback candidate preserves player control
- **WHEN** otherwise identical multiplier-3 and multiplier-6 comparisons start from the same accepted external velocity and reverse input
- **THEN** their first six external updates match, both accept unchanged control input, and the faster tail removes external motion sooner without stun or changing V1 identity

#### Scenario: Faster tail is measured without promising universal recovery time
- **WHEN** V1 and candidate players begin with identical dynamic external speed 12 in an unobstructed comparison fixture
- **THEN** the candidate's tail reaches zero sooner after its initial six updates; in general unobstructed equal-start comparisons it reaches zero no later than V1, without requiring all players to recover direction within a fixed 0.5 seconds

#### Scenario: A small impulse can finish during the unchanged initial stage
- **WHEN** a qualifying impact leaves external speed 0.1 before the next 8/60 initial-stage reduction
- **THEN** that update clamps external velocity to zero and clears the stage, without reversing velocity or requiring an artificial seventh update or a strictly earlier stop than V1

### Requirement: Only newly accepted dynamic impacts restart the recoil stage

The initial stage SHALL start or reset at most once for a new unlatched closing dynamic contact whose complete response actually changes that player's ExternalVelocity to a nonzero value. Multiple velocity updates within the same contact SHALL be considered together. Requested but ignored or fully clamped increments SHALL NOT reset the stage. Already latched contacts SHALL continue their normal base constraints but SHALL NOT extend the initial stage. Static wall, pillar, gate and boundary responses SHALL NOT start or reset it. Zero final external velocity SHALL clear it.

#### Scenario: Persistent contact cannot keep extending the slow stage
- **WHEN** a latched pair produces further base-solver responses over consecutive updates without a genuine new contact episode
- **THEN** the recoil stage is not reset and its scheduled tail can begin

#### Scenario: A genuine second hit starts a fresh stage
- **WHEN** another dynamic body, or a previously separated pair, creates a new closing contact with an actual accepted nonzero external-velocity change
- **THEN** that recipient gets a fresh six-update initial stage once for the new contact

#### Scenario: Armor and saturated increments do not fabricate a hit stage
- **WHEN** purple immunity rejects an incoming impulse or a same-direction increment is completely discarded by the external-speed cap
- **THEN** no initial-stage reset is attributed to that discarded increment

### Requirement: Recoil stage lifecycle follows existing external-motion advancement

The recoil stage SHALL advance only where existing external motion advances, including the existing external-motion update during stun, and SHALL NOT advance with wall-clock time. Falling, respawning, successful teleportation, player repositioning for kickoff and a new match SHALL clear it with the associated velocity reset. A failed teleport SHALL preserve it. Goal-display phases that do not advance external motion SHALL not consume it, and their eventual kickoff reset SHALL clear it. No respawn, buff, scoring, wall-repair or pause rule SHALL be changed to support this stage.

#### Scenario: Successful and failed teleports retain their distinct behavior
- **WHEN** a recoiling player successfully teleports, or instead consumes blue with no safe destination
- **THEN** success clears external velocity and the recoil stage, while failure preserves both under the existing failure policy

#### Scenario: Pauses do not consume recovery time
- **WHEN** local pause, terminal network pause, recoverable input pacing or match completion prevents external-motion updates
- **THEN** real time alone does not consume the recoil stage or change its external velocity

#### Scenario: Stun length does not shrink with recoil
- **WHEN** a player with dynamic external velocity is stunned by the existing pillar rule
- **THEN** the unchanged 36-tick stun and input restriction apply while external velocity follows the approved candidate decay wherever the existing simulation updates it
