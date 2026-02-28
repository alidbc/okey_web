# Okey Rummy: Detailed Rules & Scoring Guide

Okey is a popular Turkish tile-based game. This guide explains the core rules, valid hand combinations, and the point-countdown scoring system implemented in this project.

---

## 1. Setup & Components

### The Deck
- **106 Tiles Total**: 104 colored tiles + 2 Fake Okeys.
- **Colors**: Red, Blue, Black, Yellow.
- **Values**: Two sets of numbers 1 through 13 for each color.

### Dealing
- **Indicator Tile (Gösterge)**: One tile is drawn and placed face-up.
- **The Okey (Wildcard)**: The tile with the same color and one value higher than the indicator. 
    - *Example*: If the indicator is **Blue 5**, the Okey is **Blue 6**. (13 wraps to 1).
- **Starting Hands**:
    - The starting player receives **15 tiles**.
    - All other players receive **14 tiles**.

---

## 2. Valid Hand Combinations

To win, a player must organize 14 of their tiles into valid groups (Sets or Runs) or Pairs. The 15th tile is the "discard to win" tile.

### A. Sets (Groups)
A set consists of **3 or 4 tiles** of the same value but **all different colors**.
- **✅ Valid Set**: [Red 7], [Blue 7], [Black 7]
- **❌ Invalid Set**: [Red 7], [Red 7], [Blue 7] (Duplicate color)

### B. Runs (Sequences)
A run consists of **3 or more consecutive tiles** of the **same color**.
- **✅ Valid Run**: [Blue 1], [Blue 2], [Blue 3]
- **✅ Wrap-around Run**: [Red 12], [Red 13], [Red 1]
- **❌ Invalid Run**: [Red 1], [Red 2], [Blue 3] (Different colors)

### C. Pairs (Double Hand)
A player can alternatively win by collecting **7 pairs**. Each pair must consist of **two identical tiles** (same value and same color).
- **✅ Valid Pair**: [Blue 5], [Blue 5]
- **❌ Invalid Pair**: [Blue 5], [Red 5] (Different colors)

---

## 3. Special Tiles & Moves

### The Okey (Wildcard)
The Okey tile (determined by the indicator) can substitute for **any tile** in a set or run.
- *Example*: If Okey is **Black 8**, a set could be: [Red 5], [Blue 5], [Black 8*].

### Fake Okey
The two tiles printed with a special symbol (not colors 1-13) act as the "Fake Okey". They take the value and color of the *real* Okey tile.
- *Example*: If the Okey is **Red 10**, the Fake Okey tile is played exactly as a **Red 10**.

### Indicator Match (Gösterge)
If a player has the tile matching the face-up indicator in their hand at the very start of the game:
- **Action**: They can "show" the indicator on their first turn.
- **Bonus**: This penalizes all opponents by **1 point**.

---

## 4. Scoring System (Point-Countdown)

The game uses a professional point-countdown system. Each player starts with **20 points**, and the goal is to NOT reach zero.

### Deductions for Opponents
When a player wins, the other players lose points based on the win type:

| Win Type | Deduction | Explanation |
| :--- | :--- | :--- |
| **Normal Win** | **-2 pts** | Finishing with standard Sets and Runs. |
| **Okey Finish** | **-4 pts** | Finishing by discarding the Okey tile as the final tile. |
| **7 Pairs Win** | **-4 pts** | Finishing with 7 pairs instead of sets/runs. |
| **Indicator Match**| **-1 pt** | Deducted when someone shows the indicator at start. |

### Scoring Examples

#### Scenario A: Standard Win
1. Player 1 finishes with sets/runs.
2. **Result**: Player 2, 3, and 4 each lose **2 points**.

#### Scenario B: Okey Discard Win
1. Player 1 finishes by discarding the Okey tile.
2. **Result**: Player 2, 3, and 4 each lose **4 points**.

#### Scenario C: Indicator + Pair Win
1. Player 2 shows the indicator at the start (Opponents -1).
2. Later, Player 2 wins with 7 pairs (Opponents -4).
3. **Total Result**: Player 1, 3, and 4 lose a total of **5 points** (1+4).
