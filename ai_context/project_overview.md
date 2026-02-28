# Okey Rummy Project Overview

- **Root**: `okey_rummy-master/godot`
- **Core domain**: `Core/Scripts` (Deck, Player, RuleEngine, Tile, etc.)
- **UI layer**: `UI/Scripts` (MainEngine, BoardCenterUI, DiscardZoneUI, RackUI, OpponentUI, etc.)
- **Audio**: `UI/Scripts/AudioEngine.cs` loads sounds from `res://Assets/Sounds/`. Currently missing `tile_discard.wav` and other discard/take sounds.
- **Networking**: Autoloaded `NetworkManager`, `AccountManager` in `project.godot`.
- **Gameplay flow**:
  1. Player draws a tile (`OnDrawFromDeckPressed` / `OnDrawFromDiscardPressed`).
  2. Player discards a tile (`OnDiscardTileDropped`).
  3. UI updates via `MainEngine` and animations.
- **Documentation**: Detailed Okey rules and scoring logic are available in `ai_context/okey_rules_guide.md`.

This file provides a concise AI‑readable context for the project structure and current audio‑related issues.
