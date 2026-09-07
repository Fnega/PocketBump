## ADDED Requirements

### Requirement: A round awards at most one point then resets
The match SHALL treat the first valid scoring event of a live round as the end of that round. After a short freeze that shows who scored, the table SHALL reset and a new kickoff SHALL begin unless the match is over.

#### Scenario: Marble in opponent pocket scores and ends the round
- **WHEN** the round is live AND a marble enters player two's south pocket
- **THEN** player two gains 1 point AND the round ends

#### Scenario: Marble in own pocket is an own-goal for the opponent
- **WHEN** the round is live AND a marble enters player one's north pocket
- **THEN** player one gains 1 point AND the round ends

#### Scenario: Dunk awards the opponent one point
- **WHEN** the round is live AND player one enters an east or west pocket
- **THEN** player two gains 1 point AND the round ends AND player one's score does not decrease

#### Scenario: Self-dunk still awards the opponent
- **WHEN** the round is live AND a player enters an east or west pocket with no other player contacting them that frame
- **THEN** the other player gains 1 point AND the round ends

### Requirement: Scoring ignores last-touch
Marble points SHALL be awarded to the owner of the pocket the marble entered. Dunk points SHALL be awarded to the player who did not fall in. The system SHALL NOT use last-touch attribution.

#### Scenario: Banked marble still scores for the pocket owner
- **WHEN** player one last hit a marble AND that marble then enters player one's north pocket
- **THEN** player one gains 1 point (own goal into their defended pocket)

### Requirement: Simultaneous events resolve in a fixed order
If multiple scoring conditions become true in the same physics frame, the system SHALL award exactly one point using this order: player-one dunk, player-two dunk, north marble pocket, south marble pocket.

#### Scenario: Dunk beats marble in the same frame
- **WHEN** player two falls into a dunk pocket AND a marble enters the south pocket in the same frame
- **THEN** only player one gains 1 point AND the marble goal is ignored

### Requirement: Kickoff lasts three seconds with a closed gate
Each round SHALL start with a 3-second countdown. During the countdown the center gate SHALL be closed, players SHALL be allowed to move in their own half, and scoring SHALL NOT occur. When the countdown reaches zero the gate SHALL open and then marbles SHALL spawn, after which the round SHALL be live.

#### Scenario: Countdown movement without crossing
- **WHEN** kickoff countdown is running AND a player applies movement input
- **THEN** the player can move inside their half AND cannot cross the center gate

#### Scenario: Live starts after gate then marbles
- **WHEN** kickoff countdown reaches zero
- **THEN** the gate opens AND marbles spawn afterward AND the round becomes live

### Requirement: Players respawn at own-half centers
On each kickoff, player one SHALL spawn at the geometric center of the north half and player two SHALL spawn at the geometric center of the south half.

#### Scenario: Reset restores spawn positions
- **WHEN** a round resets into kickoff
- **THEN** both players are placed at their half centers with velocities cleared

### Requirement: Match is won by reaching the win score with a two-point lead
A player SHALL win the match when their score is greater than or equal to the configured points-to-win value AND they lead the other player by at least 2 points. Until that condition holds, rounds SHALL continue (deuce).

#### Scenario: Straight win
- **WHEN** points-to-win is 8 AND the score becomes 8–6
- **THEN** the player with 8 wins the match

#### Scenario: Must win by two
- **WHEN** points-to-win is 8 AND the score becomes 8–7
- **THEN** the match continues

#### Scenario: Deuce resolution
- **WHEN** points-to-win is 8 AND the score becomes 9–7
- **THEN** the player with 9 wins the match

### Requirement: Win score and marble count are configurable
Before a match starts, the system SHALL allow configuring points-to-win and marbles-per-round. Defaults SHALL be 8 and 1. Points-to-win SHALL be an integer from 1 through 21. Marble count SHALL be an integer from 1 through 8. Changing these values SHALL NOT be allowed after kickoff of the first round.

#### Scenario: Defaults
- **WHEN** the player starts a match without changing settings
- **THEN** points-to-win is 8 AND marble count is 1

#### Scenario: Custom settings apply to the whole match
- **WHEN** the player sets points-to-win to 5 AND marble count to 4 AND then starts the match
- **THEN** every round of that match spawns 4 marbles AND the match uses 5 as the win threshold with a two-point lead rule
