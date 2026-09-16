## MODIFIED Requirements

### Requirement: A round awards at most one point then resets
The match SHALL treat the first valid scoring event of a live round as the end of that round. After a short freeze that shows who scored, the table SHALL reset and a new kickoff SHALL begin unless the match is over.

#### Scenario: Marble in player two's defended pocket scores for player one
- **WHEN** the round is live AND a free marble enters player two's south pocket
- **THEN** player one gains 1 point AND the round ends

#### Scenario: Marble in player one's defended pocket scores for player two
- **WHEN** the round is live AND a free marble enters player one's north pocket
- **THEN** player two gains 1 point AND the round ends

#### Scenario: Dunk awards the opponent one point
- **WHEN** the round is live AND player one enters an east or west pocket
- **THEN** player two gains 1 point AND the round ends AND player one's score does not decrease

#### Scenario: Self-dunk still awards the opponent
- **WHEN** the round is live AND a player enters an east or west pocket with no other player contacting them that frame
- **THEN** the other player gains 1 point AND the round ends

### Requirement: Scoring ignores last-touch
Marble points SHALL be awarded to the opponent of the owner of the pocket the marble entered. Dunk points SHALL be awarded to the player who did not fall in. The system SHALL NOT use last-touch attribution.

#### Scenario: Own goal awards the opponent
- **WHEN** player one last hit a free marble AND that marble then enters player one's north pocket
- **THEN** player two gains 1 point

#### Scenario: Opponent goal does not depend on last touch
- **WHEN** either player last hit a free marble AND that marble then enters player two's south pocket
- **THEN** player one gains 1 point
