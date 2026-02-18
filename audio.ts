// Audio Manager
// Hosting sounds via standard CDN/Assets for demo purposes

const SOUNDS = {
  click: 'https://assets.mixkit.co/active_storage/sfx/2571/2571-preview.mp3',      // Soft Pop
  select: 'https://assets.mixkit.co/active_storage/sfx/2571/2571-preview.mp3',     // Select
  draw: 'https://assets.mixkit.co/active_storage/sfx/2570/2570-preview.mp3',       // Slide/Draw
  discard: 'https://assets.mixkit.co/active_storage/sfx/2019/2019-preview.mp3',    // Swish/Throw
  place: 'https://assets.mixkit.co/active_storage/sfx/2073/2073-preview.mp3',      // Tile Clack
  win: 'https://assets.mixkit.co/active_storage/sfx/1435/1435-preview.mp3',        // Victory
  error: 'https://assets.mixkit.co/active_storage/sfx/2572/2572-preview.mp3',      // Error
  shuffle: 'https://assets.mixkit.co/active_storage/sfx/2568/2568-preview.mp3',    // Shuffle
};

const audioCache: { [key: string]: HTMLAudioElement } = {};

export const preloadSounds = () => {
  try {
    Object.values(SOUNDS).forEach(url => {
      const audio = new Audio(url);
      audio.volume = 0.4;
      audioCache[url] = audio;
    });
  } catch (e) {
    console.warn("Audio preload failed", e);
  }
};

export const playSound = (type: keyof typeof SOUNDS) => {
  try {
    const url = SOUNDS[type];
    // Create a new instance or clone to allow overlapping sounds
    const audio = new Audio(url);
    audio.volume = 0.4;
    // slightly randomize pitch/volume for 'place' to sound natural
    if (type === 'place') {
        audio.playbackRate = 0.9 + Math.random() * 0.2;
    }
    audio.play().catch(e => {
        // Auto-play policy might block this until user interaction
        console.debug("Sound blocked or failed", e);
    });
  } catch (e) {
    console.warn("Audio play error", e);
  }
};
