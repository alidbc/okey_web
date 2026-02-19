import React from 'react';
import { TileData } from '../types';
import Tile from './Tile';
import DropSlot from './DropSlot';

interface BoardCenterProps {
    deckCount: number;
    discardPile: TileData[];
    onDrawFromDeck: () => void;
    onDrawFromDiscard: () => void;
    indicatorTile: TileData | null;
    canDraw: boolean;
    isDiscardActive: boolean;
}

const BoardCenter: React.FC<BoardCenterProps> = ({
    deckCount,
    discardPile,
    onDrawFromDeck,
    onDrawFromDiscard,
    indicatorTile,
    canDraw,
    isDiscardActive
}) => {
    return (
        <div className="flex gap-12 items-center z-20">

            {/* Discard Pile (Takeable by Player) */}
            <div className="flex items-center">
                <div
                    className={`relative w-[65px] h-[90px] border-2 border-dashed border-white/20 rounded-sm flex items-center justify-center transition-all bg-black/20
                        ${canDraw && discardPile.length > 0 ? 'ring-2 ring-yellow-400 cursor-pointer hover:bg-white/10 animate-pulse' : ''}
                    `}
                    onClick={(canDraw && discardPile.length > 0) ? onDrawFromDiscard : undefined}
                    data-target="discard-pile"
                >
                    {discardPile.length > 0 ? (
                        <Tile tile={discardPile[discardPile.length - 1]} scale={1} className="shadow-lg" />
                    ) : (
                        <div className="flex flex-col items-center justify-center gap-1 opacity-20">
                            <span className="text-[9px] uppercase font-bold text-center px-1">Discard</span>
                        </div>
                    )}
                </div>
            </div>

            {/* Deck Area */}
            <div className="flex items-center">
                {/* Deck Card */}
                <div
                    className={`relative w-[65px] h-[90px] bg-[#2a1a0a] rounded-sm shadow-xl cursor-pointer transition-transform hover:-translate-y-1 border border-white/10 flex items-center justify-center
                        ${canDraw ? 'ring-2 ring-yellow-400 animate-pulse' : ''}
                    `}
                    onClick={canDraw ? onDrawFromDeck : undefined}
                    data-source="draw-deck"
                >
                    {/* Card Back Pattern */}
                    <div className="absolute inset-1 rounded-sm border border-white/5 opacity-50"></div>
                    <div className="absolute inset-0 flex items-center justify-center opacity-30">
                        <div className="w-8 h-8 rounded-full border-2 border-amber-600/50"></div>
                    </div>

                    <span className="text-amber-200 font-serif font-bold opacity-90 text-xs tracking-widest uppercase">OKEY</span>

                    {/* Count Badge */}
                    <div className="absolute -top-3 -right-3 bg-red-600 text-white text-xs font-bold w-6 h-6 rounded-full flex items-center justify-center shadow-md border border-red-400">
                        {deckCount}
                    </div>
                </div>
            </div>

            {/* Center: Indicator Tile (Now the Finish Target) */}
            <div className="flex flex-col items-center gap-2">
                {indicatorTile && (
                    <DropSlot
                        id="finish-zone"
                        label="Indicator"
                        isActive={isDiscardActive}
                        className="w-auto h-auto border-none p-1"
                    >
                        <div className="relative group">
                            <Tile tile={indicatorTile} scale={1} className="opacity-90 brightness-75 border-2 border-amber-500/50" />
                        </div>
                    </DropSlot>
                )}
            </div>

        </div>
    );
};

export default BoardCenter;