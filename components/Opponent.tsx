import React from 'react';
import { TileData } from '../types';
import Tile from './Tile';
import DropSlot from './DropSlot';

interface OpponentProps {
  player: {
    id: number;
    name: string;
    avatar: string; // URL to avatar image
    level: number;
    score: number;
    isActive?: boolean;
  };
  position: 'top' | 'left' | 'right';
  lastDiscard: TileData | null;
  isDroppable?: boolean;
  dropId?: string;
}

const Opponent: React.FC<OpponentProps> = ({ player, position, lastDiscard, isDroppable, dropId }) => {
  // Positioning classes
  let containerClass = '';
  let dropSlotClass = '';

  switch (position) {
    case 'top':
      containerClass = 'top-4 left-1/2 -translate-x-1/2 flex-row items-center gap-4';
      dropSlotClass = 'relative';
      break;
    case 'left':
      containerClass = 'left-6 top-1/2 -translate-y-1/2 flex-row items-center gap-4';
      dropSlotClass = 'relative';
      break;
    case 'right':
      containerClass = 'right-6 top-1/2 -translate-y-1/2 flex-row-reverse items-center gap-4';
      dropSlotClass = 'relative';
      break;
  }

  const namePlate = (
    <div
      className={`relative flex items-center bg-gradient-to-b from-[#5c3a21] to-[#3e2613] border-2 transition-all duration-300 ${player.isActive ? 'border-yellow-400 shadow-[0_0_25px_rgba(250,204,21,0.8)] scale-105' : 'border-[#2a1a0a]'} rounded-sm p-1.5 min-w-[150px] shadow-lg`}
      data-source={`opponent-avatar-${position}`}
    >
      <img src={player.avatar} alt={player.name} className={`w-12 h-12 rounded-full border-2 ${player.isActive ? 'border-yellow-200 animate-pulse' : 'border-[#b8860b]'} shadow-md object-cover`} />
      <div className="ml-3 flex flex-col items-start justify-center text-white">
        <span className={`font-bold text-sm leading-tight ${player.isActive ? 'text-yellow-200' : 'text-amber-100'}`}>{player.name}</span>
        <span className="text-xs text-amber-400/80">Level {player.level}</span>
      </div>
      {/* Online indicator */}
      <div className="absolute top-2 right-2 w-2 h-2 rounded-full bg-green-500 shadow-[0_0_5px_rgba(34,197,94,1)]"></div>
    </div>
  );

  return (
    <div className={`absolute ${containerClass} flex z-10`}>
      {namePlate}

      <div className={dropSlotClass}>
        <DropSlot
          id={dropId || `opponent-drop-${position}`}
          label="Drop"
          isActive={isDroppable}
        >
          {lastDiscard && <Tile tile={lastDiscard} scale={0.9} className="shadow-lg" />}
        </DropSlot>
      </div>
    </div>
  );
};

export default Opponent;