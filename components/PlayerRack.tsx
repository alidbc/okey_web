import React, { useState } from 'react';
import {
    DndContext,
    DragOverlay,
    useDraggable,
    useDroppable,
    MouseSensor,
    TouchSensor,
    useSensor,
    useSensors,
    DragEndEvent,
    DragStartEvent
} from '@dnd-kit/core';
import { CSS } from '@dnd-kit/utilities';
import Tile from './Tile';
import { TileData } from '../types';

interface PlayerRackProps {
    tiles: TileData[];
    selectedTileId: string | null;
    onTileClick: (tile: TileData) => void;
    onEmptySlotClick: (index: number) => void;
    onTileMove?: (fromIndex: number, toIndex: number) => void;
}

// --- Internal Dnd Components ---

interface DraggableTileProps {
    tile: TileData;
    index: number;
    isSelected: boolean;
    onClick: (e: React.MouseEvent) => void;
    children: React.ReactNode;
}

const DraggableTile: React.FC<DraggableTileProps> = ({
    tile,
    index,
    isSelected,
    onClick,
    children
}) => {
    const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({
        id: `tile-${tile.id}`,
        data: { index, tile }
    });

    const style: React.CSSProperties = {
        transform: CSS.Translate.toString(transform),
        zIndex: isDragging ? 100 : (isSelected ? 50 : 40),
        opacity: isDragging ? 0 : 1, // Hide the original element while dragging
        touchAction: 'none', // Prevent scrolling while dragging
        position: 'relative',
        width: '100%',
        marginBottom: 0,
        transition: isDragging ? 'none' : 'transform 0.15s ease-out',
    };

    return (
        <div
            ref={setNodeRef}
            style={style}
            {...listeners}
            {...attributes}
            onClick={onClick}
            className={`
                w-full origin-bottom
                ${isSelected ? '-translate-y-4 scale-110' : 'hover:-translate-y-2'}
            `}
            data-tile-id={tile.id}
        >
            {children}
        </div>
    );
};

interface DroppableSlotProps {
    index: number;
    children: React.ReactNode;
    onClick: () => void;
}

const DroppableSlot: React.FC<DroppableSlotProps> = ({
    index,
    children,
    onClick
}) => {
    const { setNodeRef, isOver } = useDroppable({
        id: `slot-${index}`,
        data: { index }
    });

    return (
        <div
            ref={setNodeRef}
            className={`relative flex-1 min-w-0 h-full flex items-end justify-center transition-colors ${isOver ? 'bg-white/20 rounded-lg' : ''}`}
            onClick={onClick}
            data-target={`rack-slot-${index}`}
        >
            {children}
        </div>
    );
};


const PlayerRack: React.FC<PlayerRackProps> = ({ tiles, selectedTileId, onTileClick, onEmptySlotClick, onTileMove }) => {
    const ROW_SIZE = 13;
    const LOCAL_RACK = "/rack.png";
    const FALLBACK_RACK_TEXTURE = "https://images.unsplash.com/photo-1546527868-ccb7ee7dfa6a?q=80&w=2070&auto=format&fit=crop";

    const [imgSrc, setImgSrc] = useState(LOCAL_RACK);
    const [isFallback, setIsFallback] = useState(false);
    const [activeDragTile, setActiveDragTile] = useState<TileData | null>(null);

    const sensors = useSensors(
        useSensor(MouseSensor, {
            activationConstraint: {
                distance: 8, // Require 8px movement before drag starts (allows clicks)
            },
        }),
        useSensor(TouchSensor, {
            activationConstraint: {
                delay: 150, // Short delay to distinguish tap from drag
                tolerance: 5,
            },
        })
    );

    const handleError = () => {
        if (!isFallback) {
            console.warn(`Failed to load ${LOCAL_RACK}, switching to hotlinked fallback.`);
            setImgSrc(FALLBACK_RACK_TEXTURE);
            setIsFallback(true);
        }
    };

    const rowConfig = [
        // Top Shelf
        { id: 0, style: { top: '4%', paddingLeft: '8%', paddingRight: '8%' } },
        // Bottom Shelf
        { id: 1, style: { bottom: '9.5%', paddingLeft: '7.5%', paddingRight: '7.5%' } }
    ];

    return (
        <div className="relative w-full max-w-[960px] mx-auto mt-auto mb-2 z-10 select-none flex justify-center">

            <div className="relative w-full aspect-[3.3/1]">

                {/* Rack Background Layer */}
                <div className="absolute inset-0 z-0 drop-shadow-2xl overflow-hidden rounded-lg">
                    <img
                        src={imgSrc}
                        alt="Player Rack"
                        className={`
                absolute inset-0 w-full h-full pointer-events-none transition-opacity duration-500
                ${isFallback ? 'object-cover' : 'object-contain'}
            `}
                        style={isFallback ? {
                            clipPath: 'polygon(1.5% 0%, 98.5% 0%, 100% 100%, 0% 100%)',
                            filter: 'brightness(0.7) sepia(0.3)'
                        } : {}}
                        onError={handleError}
                    />

                    {isFallback && (
                        <div className="absolute inset-0 pointer-events-none">
                            <div className="absolute inset-0 bg-gradient-to-b from-black/20 via-transparent to-black/60 mix-blend-multiply"></div>
                            <div className="absolute top-[20%] left-[2%] right-[2%] h-[70px] bg-black/30 rounded shadow-[inset_0_2px_5px_rgba(0,0,0,0.8)] border-b border-white/5"></div>
                            <div className="absolute bottom-[13%] left-[1.5%] right-[1.5%] h-[70px] bg-black/30 rounded shadow-[inset_0_2px_5px_rgba(0,0,0,0.8)] border-b border-white/5"></div>
                        </div>
                    )}
                </div>

                {/* Rack Content (Interactive Layer) */}
                <div className="absolute inset-0 z-10 flex flex-col justify-between py-1">
                    {rowConfig.map((row) => (
                        <div
                            key={row.id}
                            className="absolute left-0 right-0 h-[42%] flex items-end justify-center overflow-hidden gap-[3px]"
                            style={row.style}
                        >
                            {Array.from({ length: ROW_SIZE }).map((_, colIndex) => {
                                const linearIndex = row.id * ROW_SIZE + colIndex;
                                const tile = tiles[linearIndex];

                                return (
                                    <DroppableSlot
                                        key={linearIndex}
                                        index={linearIndex}
                                        onClick={() => !tile && onEmptySlotClick(linearIndex)}
                                    >
                                        {!tile && (
                                            <div className="absolute bottom-1 w-[80%] h-[80%] rounded-md hover:bg-white/10 transition-colors border border-white/0 hover:border-white/10"></div>
                                        )}

                                        {tile && (
                                            <DraggableTile
                                                tile={tile}
                                                index={linearIndex}
                                                isSelected={selectedTileId === tile.id}
                                                onClick={(e) => { e.stopPropagation(); onTileClick(tile); }}
                                            >
                                                <Tile
                                                    tile={tile}
                                                    selected={selectedTileId === tile.id}
                                                    scale={1}
                                                    fluid={true}
                                                    className=""
                                                />
                                            </DraggableTile>
                                        )}
                                    </DroppableSlot>
                                );
                            })}
                        </div>
                    ))}
                </div>
            </div>
        </div>
    );
};

export default PlayerRack;