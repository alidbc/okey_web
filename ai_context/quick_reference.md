# Quick Reference for Okey Rummy Project

## Core Structure
- **Root**: `okey_rummy-master/godot`
- **Domain (game logic)**: `Core/Scripts` – Deck, Player, Tile, RuleEngine, etc.
- **UI Layer**: `UI/Scripts`
  - `MainEngine.cs` – central game controller, handles draw/discard actions.
  - `DiscardZoneUI.cs` – UI drop target for discarding tiles.
  - `RackUI.cs` – player rack UI.
  - `AudioEngine.cs` – simple audio manager.

## Audio Handling
- `AudioEngine.cs` loads sounds in `_Ready()`:
  ```csharp
  LoadSound("tile_click", "res://Assets/Sounds/tile_click.wav");
  LoadSound("tile_draw", "res://Assets/Sounds/tile_draw.wav");
  LoadSound("turn_change", "res://Assets/Sounds/turn_change.wav");
  LoadSound("tile_discard", "res://Assets/Sounds/tile_discard.wav");
  ```
- No calls to `PlayUI`/`PlayGame` are present in the code.
- **Hook points** (where to add sound playback):
  - In `MainEngine.OnDrawFromDeckPressed` and `OnDrawFromDiscardPressed` → `AudioEngine.PlayGame("tile_draw")`.
  - In `MainEngine.OnDiscardTileDropped` (after successful discard) → `AudioEngine.PlayGame("tile_discard")`.
  - UI button clicks (e.g., `StartGameButton.Pressed`) can use `PlayUI` for UI feedback.

## Missing Assets
- `Assets/Sounds/` currently has no `.wav` files.
- Add placeholder files: `tile_click.wav`, `tile_draw.wav`, `turn_change.wav`, `tile_discard.wav` (any short sound will do).

## Quick Fix Checklist
1. **Create sound files** in `Assets/Sounds/`.
2. **Add AudioEngine calls** at the hook points above.
3. **Run the game** and verify that taking/discarding tiles now produce sound.
4. (Optional) Add a small UI test to ensure `AudioEngine` is instantiated.

This file is intended as a minimal reference to avoid long explanations in future chats.
