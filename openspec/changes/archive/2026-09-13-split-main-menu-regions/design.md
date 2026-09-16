## Context

`MainMenuController.BuildUi` currently creates Start, HostLan, JoinLan, Exit, and Error under one `MainPanel`. The user wants three independent regions on the same home screen, not three pages. Rule setup and join-address input already have separate panels. `EnsureLanUi` also repairs old authored scenes by adding LAN buttons directly to MainPanel, so moving objects in the scene alone would not be sufficient.

## Goals / Non-Goals

**Goals:**

- Present three clearly bounded, separately titled Start, Server, and Exit regions together on the home screen.
- Group Start alone, Host LAN and Join LAN together, and Exit alone.
- Keep generated UI, legacy hierarchy repair, and authored MainMenu consistent and idempotent.
- Preserve all existing action callbacks, rule values, binding controls, and back navigation.

**Non-Goals:**

- Separate home pages, a server browser, dedicated servers, matchmaking, or an exit confirmation dialog.
- Changing LAN protocol, gameplay, rule setup contents, or input bindings.

## Decisions

### Keep MainPanel as the home visibility root

Use three child containers so existing ShowRules/ShowJoin home visibility toggles still hide or restore the entire home UI atomically:

```text
MainMenuCanvas
  MainPanel
    StartRegion  -> heading + Start
    ServerRegion -> heading + HostLan + JoinLan
    ExitRegion   -> heading + Exit
    Error        -> reserved shared feedback area
  RuleSetupPanel
  JoinPanel
```

Alternative: three independent pages with a page-state controller. Rejected because the user explicitly requested three regions, and new navigation would be unnecessary.

### Use a vertical stack of separated cards

Stack Start, Server, and Exit cards with consistent margins and gaps; allow the Server card more height for two actions. Keep all cards below the title and above reserved error feedback. Each card has its own background/boundary and readable heading in the existing neon/TMP style. Layout must fit the current Canvas reference resolution and scale without clipping at 16:9 and 4:3 aspect ratios.

Alternative: three equal horizontal columns. A vertical stack better accommodates the taller server content and the existing centered menu structure while avoiding narrow card text on 4:3 displays.

### Share hierarchy construction and repair

Replace flat LAN repair with an idempotent region-ensuring routine used by both runtime and authored UI paths. Reparent existing named buttons into their intended containers, preserving their identities and restoring intended layout; create only missing objects. Resolve references recursively or at the new explicit paths, and retain stable button names for existing binding/navigation tests. Repeated initialization must not duplicate buttons, cards, or listeners.

Alternative: scene-only edits or retaining the old EnsureLanUi behavior. Rejected because runtime fallback/repair would restore the mixed layout.

### Preserve navigation and quit behavior

Start still opens local rule setup. Host LAN still opens host rule setup. Join LAN still opens address entry. Back restores the complete three-region home screen. Exit continues to request a direct clean quit. Shared error feedback remains outside the action cards so long connection errors do not overlap buttons.

## Risks / Trade-offs

- [Risk] Existing authored references and flat hierarchy paths become stale. → Resolve/reparent legacy objects and test both authored and generated hierarchy paths.
- [Risk] Automatic repair recreates old flat buttons or duplicates callbacks. → Use one idempotent layout routine and existing listener-clearing binding behavior; test repeated setup.
- [Risk] Cards, title, or error feedback overlap at different aspect ratios. → Reserve feedback space and visually verify 16:9 and 4:3 layouts.
- [Risk] The existing scene baker may regenerate unrelated Arena content with user changes. → Use the existing MainMenu baking path in a menu-only operation and inspect the scene diff before accepting it.
