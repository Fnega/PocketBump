## 1. Mouth

- [x] 1.1 Set `BasketWidth` to 1.11; leave length, rim, and offset unchanged
- [x] 1.2 Confirm well and rim rebuild from those constants so inner opening is ~0.95

## 2. Charge readings

- [x] 2.1 Parent direction and power needles under the basket; grow them forward of the well
- [x] 2.2 Size: direction width 0.28 length 1.8; power width 0.36 length `1.0 + power * 1.2`; lift so they clear the meshes
- [x] 2.3 Direction stage: identity-colored direction needle only. Power stage: keep locked direction visible and show the high-contrast power needle
- [x] 2.4 Hide both needles on fire or cancel

## 3. Pulses

- [x] 3.1 Pulse identity color at the new carrier's basket on collect and steal
- [x] 3.2 Pulse at the launch point on fire; no pulse on body bounce or cancel

## 4. Verify

- [x] 4.1 Bake authored Arena
- [x] 4.2 Play: front-on scoop collects instead of rim bounce; needles read outside the body in both charge stages; collect and fire flash
- [x] 4.3 Run existing EditMode and PlayMode PocketBump tests
