## MODIFIED Requirements

### Requirement: A round awards at most one point then resets
Only a free scoring ball entering a defended north or south goal SHALL award a point. The point SHALL belong to the opponent of that goal's owner, including an own goal, independently of last touch. The first selected valid goal in a Live tick SHALL end that round with at most one point. After the existing short scoring presentation, both players and the scoring balls SHALL reset for kickoff unless the existing win-by-two condition ends the match. Falling SHALL neither award points nor reset the world. Goal resets SHALL restore dead players, cancel pending respawns, and clear player transient gameplay state without rebuilding obstacle state or restarting wall-repair timers.

#### Scenario: South goal awards player one
- **WHEN** a free scoring ball enters player two's south goal during Live play
- **THEN** only player one gains one point and the round enters score presentation followed by kickoff or the existing match-over flow

#### Scenario: North goal awards player two
- **WHEN** a free scoring ball enters player one's north goal during Live play
- **THEN** only player two gains one point regardless of which player last touched the ball

#### Scenario: Falling does not end the round
- **WHEN** either player enters a side death region without a simultaneous valid goal
- **THEN** neither score changes and the other player, scoring balls, obstacles, and game world continue Live play

#### Scenario: Goal recovers a fallen player without rebuilding walls
- **WHEN** a ball scores while a player is awaiting respawn and a wall has a partly elapsed repair timer
- **THEN** kickoff restores both players and clears the old respawn state while preserving that wall's broken or repair-pending state and remaining repair time

### Requirement: Scoring ignores last-touch
Ball points SHALL be awarded solely to the opponent of the entered goal's owner. The system SHALL NOT attribute goals to the last robot, wall, pillar, or ball that touched the scoring ball. Falling, stunning, breaking walls, and picking up Buffs SHALL NOT award score.

#### Scenario: Own goal awards the opponent
- **WHEN** player one last hits a scoring ball and it enters player one's north goal
- **THEN** player two gains one point

#### Scenario: Environmental deflection preserves goal ownership
- **WHEN** a ball rebounds from a pillar or wall into player two's south goal
- **THEN** player one gains one point without a last-touch lookup

### Requirement: Simultaneous events resolve in a fixed order
Within one Live tick the simulation SHALL collect fall and scoring-ball goal candidates in a deterministic order. Falling SHALL NOT suppress a valid goal. At most one goal SHALL be selected, preferring the north goal before the south goal and then the lower stable scoring-ball ID within a goal. If a goal is selected, its score/reset transition SHALL supersede same-tick pickup or independent respawn processing; obstacle changes already resolved during that tick SHALL remain. Without a goal, both players' valid falls SHALL be processed independently without granting either side a point.

#### Scenario: Fall does not defeat a legal goal
- **WHEN** player two falls and a ball enters the south goal in the same tick
- **THEN** only player one gains the goal point and the round transition restores players at the subsequent kickoff without an extra fall point

#### Scenario: Both players fall together
- **WHEN** both players enter death regions in the same tick and no ball scores
- **THEN** both enter their own respawn countdown, neither gains points, and the world continues Live play

#### Scenario: Two simultaneous goals award only one point
- **WHEN** different balls enter the north and south goals in the same tick
- **THEN** only the north-goal candidate awards player two a point, consistently in baseline and replayed simulations

### Requirement: Kickoff lasts three seconds with a closed gate
Each round SHALL begin with the existing three-second, 180-tick countdown. The center gate SHALL block crossing, players SHALL be able to move within their half, and goals, falls, Buff spawning and Buff pickup SHALL not resolve as Live events during the countdown. The approved phase-control policy SHALL govern Dash availability rather than a presentation-only restriction. When the countdown reaches zero the gate SHALL open before the configured scoring balls spawn at rest and the round becomes Live. Existing broken or pending walls SHALL retain their state and SHALL continue their active-match repair timing during kickoff.

#### Scenario: Countdown movement remains inside each half
- **WHEN** a player supplies movement during kickoff
- **THEN** the player moves within the permitted half without crossing the closed gate or triggering a Live goal, fall, or pickup

#### Scenario: Gate precedes the scoring balls
- **WHEN** kickoff reaches its 180-tick boundary
- **THEN** the gate opens, the configured scoring balls spawn at valid locations, and Live play begins

#### Scenario: Goal does not extend broken-wall lifetime
- **WHEN** a wall remains broken through score presentation and kickoff
- **THEN** those active-match simulation ticks advance its existing repair countdown without replacing it with a new 600-tick countdown

### Requirement: Players respawn at own-half centers
Initial kickoff and post-goal resets SHALL place player one at the authored north-half spawn and player two at the authored south-half spawn, centered in their own half for the approved symmetric layout, with velocities cleared. Layout validation SHALL keep these kickoff spawn footprints free from static obstacles and death regions. Independent fall respawns SHALL use the same player's approved own-half safe spawn region and the separate safety policy rather than resetting the other player or forcing an occupied center position.

#### Scenario: Kickoff restores both players
- **WHEN** a goal transitions into kickoff
- **THEN** both players are alive at their own spawn positions with cleared velocities and no stale stun, active Dash, Buff, or pending-fall state

#### Scenario: Independent respawn does not move the opponent
- **WHEN** a fallen player's independent respawn becomes eligible during Live play
- **THEN** only that player is placed in a collision-free location in their own spawn region and the opponent's pose and the scoring balls remain unchanged

### Requirement: Match rules advance on a fixed tick
The match SHALL advance on the existing 60 Hz deterministic simulation tick, not Unity collision callbacks or presentation timers. Dash duration/cooldown, stun, fall respawn, Buff lifetimes/spawn clocks, contact episodes and wall repair SHALL use snapshot-restorable simulation state. Wall-repair time SHALL advance during Live, kickoff, and score-presentation ticks, but SHALL NOT advance after MatchOver or during a network failure freeze. Real-time connection heartbeats and protocol deadlines SHALL remain independent of stopped gameplay time.

#### Scenario: Fixed countdown boundaries
- **WHEN** kickoff and Dash/stun/fall/wall countdowns run without an intervening reset or terminal pause
- **THEN** their eligibility boundaries follow 180, 18/180, 36, 180, and 600 simulation ticks respectively without render-frame-dependent drift

#### Scenario: Network pause cannot repair a wall or expire a Buff
- **WHEN** a network error freezes the Arena while gameplay timers are outstanding
- **THEN** those gameplay states and timers remain frozen until the session is deliberately left and no real-time coroutine advances them

## ADDED Requirements

### Requirement: Falling creates an independent safe respawn
An alive player entering an east or west death region during Live play SHALL become inactive for a minimum of 180 simulation ticks without changing scores. An inactive player SHALL not provide a collision body, collect Buffs, move under input, or use Dash; its visual fall animation SHALL not drive simulation state. The player SHALL lose active Buffs, stun, active Dash, and residual velocities; falling SHALL NOT bypass a Dash cooldown, and an interrupted Dash SHALL enter the ordinary cooldown. At eligibility, deterministic safe candidate positions in the player's own-half spawn region SHALL be checked against full active-player, scoring-ball, static-obstacle and death-region geometry. If none are available, respawn SHALL remain pending and retry in deterministic order rather than overlap, kill, or push another object. A goal or a new match SHALL cancel an obsolete respawn request.

#### Scenario: Ordinary three-second respawn
- **WHEN** a player falls and an approved spawn position is clear at the end of 180 ticks
- **THEN** that player becomes alive there with zero velocity, without a score change or interruption to the remaining Live world

#### Scenario: Occupied spawn waits safely
- **WHEN** the respawn countdown expires but all allowed spawn footprints are occupied
- **THEN** that player remains inactive with visible waiting feedback until a safe candidate is available and no entity is forcibly displaced

#### Scenario: Falling cannot refresh Dash early
- **WHEN** a player falls during Dash or its cooldown
- **THEN** the ordinary remaining cooldown continues on simulation time during absence and falling does not grant an extra ready Dash

#### Scenario: Replay repeats the same respawn
- **WHEN** delayed input changes whether a player falls or occupies a respawn candidate
- **THEN** reconciliation restores the same alive state, countdown, chosen candidate, cooldown and scores as the corresponding baseline input tape
