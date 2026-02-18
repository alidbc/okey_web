import { TileColor, TileData } from './types';

// Helper to generate a unique ID
export const generateId = () => Math.random().toString(36).substr(2, 9);

export const TOTAL_TILES = 106;

// Function to create a full deck
export const createDeck = (): TileData[] => {
  const deck: TileData[] = [];
  const colors = [TileColor.RED, TileColor.BLACK, TileColor.BLUE, TileColor.YELLOW];
  
  // 2 sets of tiles for each color 1-13
  colors.forEach(color => {
    for (let set = 0; set < 2; set++) {
      for (let i = 1; i <= 13; i++) {
        deck.push({
          id: generateId(),
          value: i,
          color: color,
          isFakeOkey: false
        });
      }
    }
  });

  // 2 Fake Okeys (Jokers in the visual sense of the clover tile)
  deck.push({ id: generateId(), value: 0, color: TileColor.JOKER, isFakeOkey: true });
  deck.push({ id: generateId(), value: 0, color: TileColor.JOKER, isFakeOkey: true });

  return shuffle(deck);
};

const shuffle = (array: any[]) => {
  let currentIndex = array.length,  randomIndex;
  while (currentIndex !== 0) {
    randomIndex = Math.floor(Math.random() * currentIndex);
    currentIndex--;
    [array[currentIndex], array[randomIndex]] = [array[randomIndex], array[currentIndex]];
  }
  return array;
};

export const MOCK_PLAYERS = [
  { id: 'p2', name: 'Victor', level: 36, avatar: 'https://picsum.photos/seed/victor/100', isHuman: false, score: 250, isActive: false, tilesCount: 14 },
  { id: 'p3', name: 'Elena', level: 41, avatar: 'https://picsum.photos/seed/elena/100', isHuman: false, score: 320, isActive: false, tilesCount: 14 },
  { id: 'p4', name: 'Marcus', level: 22, avatar: 'https://picsum.photos/seed/marcus/100', isHuman: false, score: 180, isActive: false, tilesCount: 14 },
];