## ADDED Requirements

### Requirement: Local map visibility follows a configurable press edge

Local split-screen SHALL use one shortcut, defaulting to M, to toggle both existing minimaps on each new press. Holding the key SHALL NOT repeatedly toggle. Menus capturing input, left sessions and LAN SHALL NOT act on this shortcut. The toggle SHALL change presentation only without modifying movement, Dash, simulation, camera framing or other HUD elements.

#### Scenario: Two presses hide and restore both maps
- **WHEN** a local match receives two separate map-key presses
- **THEN** both minimap subtrees hide after the first and reappear after the second while other HUD and simulation remain intact

#### Scenario: Refresh preserves hiding but a new match resets it
- **WHEN** the maps are hidden and the same session refreshes, scores or respawns
- **THEN** they remain hidden until another press; binding a new director or match epoch defaults them to visible without writing visibility preferences

#### Scenario: LAN does not inherit local hiding
- **WHEN** the HUD leaves local mode or receives this shortcut in LAN
- **THEN** the temporary local visibility override is restored and LAN visibility is not toggled

### Requirement: Settings expose a persistent map binding without conflicts

Settings SHALL show the current map key and support rebinding, Escape cancellation and restoring default M. The key SHALL use an independent persistent preference, separate from nonpersistent map visibility. Movement keys, Escape, invalid/mouse keys and either Dash key SHALL be rejected for map assignment. Dash assignment SHALL reject the current map key. M SHALL become available to Dash if the map is assigned elsewhere. Loading SHALL not write preferences or discard unaffected valid controls.

#### Scenario: Custom key survives reload
- **WHEN** a user assigns an available map key and reloads bindings
- **THEN** the setting shows and uses the new key, M no longer toggles maps and no Dash shares the map key

#### Scenario: Conflicting assignment or cancellation preserves configuration
- **WHEN** the user attempts a forbidden key or cancels listening with Escape
- **THEN** the previous valid key configuration remains intact

#### Scenario: Old M Dash settings recover only the conflicting player
- **WHEN** default or recovered map key M conflicts with an old Dash binding
- **THEN** the conflicting seat uses its available default Dash key, or the other available default if occupied, while unaffected bindings remain and loading does not save preferences

### Requirement: Map controls are authored through incremental integration

The map settings control and references SHALL be saved in MainMenu by explicit incremental integration, preserving unrelated objects, styles, unknown children, Arena and Build Settings. Runtime binding SHALL not substitute menu regeneration for the authored control. Repeated installation SHALL preserve existing customization.

#### Scenario: Map settings survive saved-scene reload
- **WHEN** the control is saved, reloaded and installed again
- **THEN** the references and edited style remain valid without duplicate controls or unrelated scene changes
