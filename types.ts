export enum TileColor {
  RED = 'red',
  BLUE = 'blue',
  BLACK = 'black',
  YELLOW = 'yellow', // In Okey, sometimes orange/yellow
  JOKER = 'joker'
}

export interface TileData {
  id: string;
  value: number; // 1-13
  color: TileColor;
  
  // Specific Okey Properties
  isFakeOkey: boolean; // The physical tile with the clover/symbol
  isWildcard?: boolean; // If true, this specific tile is the "Okey" (Joker) for this round
  
  // For the Fake Okey tile, it takes on the identity of the tile that became the wildcard
  virtualValue?: number; 
  virtualColor?: TileColor;
}

export interface Player {
  id: string;
  name: string;
  level: number;
  avatar: string;
  isHuman: boolean;
  score: number;
  isActive: boolean;
  tilesCount: number;
}

export enum GameStatus {
  MENU,
  PLAYING,
  VICTORY
}

export enum TurnPhase {
  WAITING, // Opponents playing
  DRAW,    // Player needs to draw
  DISCARD  // Player needs to discard
}