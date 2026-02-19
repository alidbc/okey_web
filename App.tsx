import React, { useState, useEffect, useRef } from 'react';
import { GameStatus, TileData, Player, TileColor, TurnPhase } from './types';
import { createDeck, MOCK_PLAYERS, generateId } from './constants';
import Tile from './components/Tile';
import PlayerRack from './components/PlayerRack';
import Opponent from './components/Opponent';
import BoardCenter from './components/BoardCenter';
import VictoryScreen from './components/VictoryScreen';
import MainMenu from './components/MainMenu';
import SplashScreen from './components/SplashScreen';
import { playSound, preloadSounds } from './audio';
import {
    DndContext,
    DragOverlay,
    useSensor,
    useSensors,
    PointerSensor,
    TouchSensor,
    DragStartEvent,
    DragMoveEvent,
    DragEndEvent,
    closestCenter,
    MeasuringStrategy
} from '@dnd-kit/core';

// --- Validation Logic ---

function validateHandGroups(rack: (TileData | null)[]): { valid: boolean; reason?: string } {
    const clusters: TileData[][] = [];
    let currentCluster: TileData[] = [];
    for (let i = 0; i < rack.length; i++) {
        if (rack[i]) {
            currentCluster.push(rack[i]!);
        } else if (currentCluster.length > 0) {
            clusters.push(currentCluster);
            currentCluster = [];
        }
    }
    if (currentCluster.length > 0) clusters.push(currentCluster);
    const totalTiles = clusters.reduce((acc, c) => acc + c.length, 0);
    if (totalTiles !== 14) {
        return { valid: false, reason: `You need exactly 14 tiles to finish (currently used: ${totalTiles}).` };
    }
    for (const cluster of clusters) {
        if (cluster.length < 3) return { valid: false, reason: "All groups must have at least 3 tiles." };
        if (!isRun(cluster) && !isSet(cluster)) return { valid: false, reason: "Found a group that is neither a valid Set nor a Run." };
    }
    return { valid: true };
}

function isSet(tiles: TileData[]): boolean {
    if (tiles.length > 4) return false;
    const nonWild = tiles.filter(t => !t.isWildcard);
    if (nonWild.length === 0) return true;
    const baseValue = nonWild[0].value;
    if (nonWild.some(t => t.value !== baseValue)) return false;
    const colors = new Set(nonWild.map(t => t.color));
    if (colors.size !== nonWild.length) return false;
    return true;
}

function isRun(tiles: TileData[]): boolean {
    const nonWild = tiles.filter(t => !t.isWildcard);
    if (nonWild.length === 0) return true;
    const baseColor = nonWild[0].color;
    if (nonWild.some(t => t.color !== baseColor)) return false;
    const checkSequence = (treatOneAsFourteen: boolean) => {
        const firstIdx = tiles.findIndex(t => !t.isWildcard);
        let firstVal = tiles[firstIdx].value;
        if (treatOneAsFourteen && firstVal === 1) firstVal = 14;
        for (let i = firstIdx + 1; i < tiles.length; i++) {
            if (!tiles[i].isWildcard) {
                let currentVal = tiles[i].value;
                if (treatOneAsFourteen && currentVal === 1) currentVal = 14;
                let diff = i - firstIdx;
                if (currentVal !== firstVal + diff) return false;
            }
        }
        const startVal = firstVal - firstIdx;
        const endVal = startVal + tiles.length - 1;
        if (treatOneAsFourteen) {
            if (startVal < 1 || endVal > 14) return false;
        } else {
            if (startVal < 1 || endVal > 13) return false;
        }
        return true;
    };
    if (checkSequence(false)) return true;
    const hasOne = nonWild.some(t => t.value === 1);
    if (hasOne && checkSequence(true)) return true;
    return false;
}

// --- Main App ---

const App = () => {
    const [showSplash, setShowSplash] = useState(true);
    const [status, setStatus] = useState<GameStatus>(GameStatus.MENU);
    const [turnPhase, setTurnPhase] = useState<TurnPhase>(TurnPhase.WAITING);
    const [deck, setDeck] = useState<TileData[]>([]);
    const [playerTiles, setPlayerTiles] = useState<(TileData | null)[]>([]);
    const [discardPile, setDiscardPile] = useState<TileData[]>([]);
    const [playerDiscardPile, setPlayerDiscardPile] = useState<TileData[]>([]);
    const [rightDiscardPile, setRightDiscardPile] = useState<TileData[]>([]);
    const [topDiscardPile, setTopDiscardPile] = useState<TileData[]>([]);
    const [indicatorTile, setIndicatorTile] = useState<TileData | null>(null);
    const [selectedTileId, setSelectedTileId] = useState<string | null>(null);
    const [errorMsg, setErrorMsg] = useState<string | null>(null);
    const [animatingTile, setAnimatingTile] = useState<{ tile: TileData, style: React.CSSProperties } | null>(null);
    const [finalScores, setFinalScores] = useState<{ playerName: string; score: number; isWinner: boolean }[] | null>(null);
    const [opponents, setOpponents] = useState<Player[]>(MOCK_PLAYERS);
    const botsPlayingRef = useRef(false);

    const [windowSize, setWindowSize] = useState({
        width: typeof window !== 'undefined' ? window.innerWidth : 1200,
        height: typeof window !== 'undefined' ? window.innerHeight : 800
    });

    useEffect(() => {
        const handleResize = () => setWindowSize({ width: window.innerWidth, height: window.innerHeight });
        window.addEventListener('resize', handleResize);
        return () => window.removeEventListener('resize', handleResize);
    }, []);

    const TARGET_WIDTH = 1280;
    const TARGET_HEIGHT = 720;
    const scale = Math.min(windowSize.width / TARGET_WIDTH, windowSize.height / TARGET_HEIGHT);

    useEffect(() => { preloadSounds(); }, []);

    const handleStartGame = () => { playSound('click'); startNewGame(); setTimeout(() => setStatus(GameStatus.PLAYING), 100); };

    const startNewGame = () => {
        playSound('shuffle');
        let newDeck = createDeck();
        const indicatorIndex = Math.floor(Math.random() * newDeck.length);
        const indicator = newDeck[indicatorIndex];
        newDeck.splice(indicatorIndex, 1);
        setIndicatorTile(indicator);

        newDeck = newDeck.map(tile => {
            const t = { ...tile, isWildcard: false };
            if (t.isFakeOkey) t.isWildcard = true;
            return t;
        });

        const initialPlayerTiles = new Array(26).fill(null);
        for (let i = 0; i < 15; i++) if (newDeck.length > 0) initialPlayerTiles[i] = newDeck.pop() || null;
        for (let i = 0; i < 3; i++) for (let j = 0; j < 14; j++) newDeck.pop();

        setPlayerTiles(initialPlayerTiles);
        setDeck(newDeck);
        setDiscardPile([]);
        setPlayerDiscardPile([]);
        setRightDiscardPile([]);
        setTopDiscardPile([]);
        setTurnPhase(TurnPhase.DISCARD);
    };

    const handleTileClick = (tile: TileData) => { playSound('click'); setSelectedTileId(selectedTileId === tile.id ? null : tile.id); setErrorMsg(null); };

    const handleEmptySlotClick = (index: number) => {
        if (selectedTileId) {
            const currentTileIndex = playerTiles.findIndex(t => t?.id === selectedTileId);
            if (currentTileIndex !== -1) {
                playSound('place');
                const newTiles = [...playerTiles];
                newTiles[index] = newTiles[currentTileIndex];
                newTiles[currentTileIndex] = null;
                setPlayerTiles(newTiles);
                setSelectedTileId(null);
            }
        }
    };

    const handleMoveTile = (fromIndex: number, toIndex: number) => {
        if (fromIndex === toIndex) return;
        playSound('place');
        const newTiles = [...playerTiles];
        const tileToMove = newTiles[fromIndex];
        newTiles[fromIndex] = newTiles[toIndex];
        newTiles[toIndex] = tileToMove;
        setPlayerTiles(newTiles);
    };

    const handleQuickSort = () => {
        playSound('shuffle');
        const validTiles = playerTiles.filter((t): t is TileData => t !== null);
        validTiles.sort((a, b) => {
            if (a.isWildcard && !b.isWildcard) return -1;
            if (!a.isWildcard && b.isWildcard) return 1;
            if (a.color !== b.color) return a.color.localeCompare(b.color);
            return a.value - b.value;
        });
        const newTiles = new Array(26).fill(null);
        validTiles.forEach((t, i) => newTiles[i] = t);
        setPlayerTiles(newTiles);
    };

    const drawFromDeck = () => {
        if (turnPhase !== TurnPhase.DRAW || deck.length === 0) return;
        const newDeck = [...deck];
        const tile = newDeck.pop()!;
        const emptyIndex = playerTiles.findIndex(t => t === null);
        setDeck(newDeck); setTurnPhase(TurnPhase.WAITING);
        animateMove('[data-source="draw-deck"]', `[data-target="rack-slot-${emptyIndex}"]`, tile, () => {
            const newTiles = [...playerTiles]; newTiles[emptyIndex] = tile; setPlayerTiles(newTiles); setTurnPhase(TurnPhase.DISCARD);
        });
    };

    const drawFromDiscard = () => {
        if (turnPhase !== TurnPhase.DRAW || discardPile.length === 0) return;
        const newDiscard = [...discardPile];
        const tile = newDiscard.pop()!;
        const emptyIndex = playerTiles.findIndex(t => t === null);
        setDiscardPile(newDiscard); setTurnPhase(TurnPhase.WAITING);
        animateMove('[data-target="discard-pile"]', `[data-target="rack-slot-${emptyIndex}"]`, tile, () => {
            const newTiles = [...playerTiles]; newTiles[emptyIndex] = tile; setPlayerTiles(newTiles); setTurnPhase(TurnPhase.DISCARD);
        });
    };

    const animateMove = (sourceSelector: string, targetSelector: string, tile: TileData, onComplete: () => void) => {
        const sourceNode = document.querySelector(sourceSelector);
        const targetNode = document.querySelector(targetSelector);
        if (sourceNode && targetNode) {
            const sRect = sourceNode.getBoundingClientRect();
            const tRect = targetNode.getBoundingClientRect();

            const BASE_WIDTH = 65;
            const ASSET_RATIO = 313 / 218;
            const BASE_HEIGHT = BASE_WIDTH * ASSET_RATIO;

            // Use the global game scale as a reference for "natural" tile size
            const standardWidth = BASE_WIDTH * scale;

            // Cap source/target dimensions at the standard game scale to prevent exaggerated tile sizes
            // (e.g. when drawing from a 130px deck stack or moving to a large avatar)
            const sWidth = sRect.width > standardWidth ? standardWidth : sRect.width;
            const tWidth = tRect.width > standardWidth ? standardWidth : tRect.width;

            // Calculate scale relative to baseline width (65px)
            const sScale = sWidth / BASE_WIDTH;
            const tScale = tWidth / BASE_WIDTH;

            // Calculate centered positions
            const sTop = sRect.top + (sRect.height - (BASE_HEIGHT * sScale)) / 2;
            const sLeft = sRect.left + (sRect.width - (BASE_WIDTH * sScale)) / 2;
            const tTop = tRect.top + (tRect.height - (BASE_HEIGHT * tScale)) / 2;
            const tLeft = tRect.left + (tRect.width - (BASE_WIDTH * tScale)) / 2;

            const initialStyle: React.CSSProperties = {
                position: 'fixed',
                top: sTop,
                left: sLeft,
                width: BASE_WIDTH,
                height: BASE_HEIGHT,
                transform: `scale(${sScale})`,
                transformOrigin: '0 0',
                zIndex: 100,
                pointerEvents: 'none',
                transition: 'all 0.5s cubic-bezier(0.2, 0.8, 0.2, 1)',
                opacity: 1
            };

            playSound('draw');
            setAnimatingTile({ tile, style: initialStyle });
            setTimeout(() => {
                setAnimatingTile(prev => prev ? {
                    ...prev,
                    style: {
                        ...prev.style,
                        top: tTop,
                        left: tLeft,
                        transform: `scale(${tScale})`,
                    }
                } : null);
            }, 50);
            setTimeout(() => { setAnimatingTile(null); playSound('place'); onComplete(); }, 550);
        } else { onComplete(); }
    };

    const discardSelectedTile = () => {
        if (turnPhase !== TurnPhase.DISCARD || !selectedTileId) return;
        const index = playerTiles.findIndex(t => t?.id === selectedTileId);
        const tileToDiscard = playerTiles[index]!;
        const newTiles = [...playerTiles];
        newTiles[index] = null;
        setPlayerTiles(newTiles);
        setSelectedTileId(null);
        setTurnPhase(TurnPhase.WAITING);
        animateMove(`[data-tile-id="${tileToDiscard.id}"]`, `[data-target="opponent-drop-right"]`, tileToDiscard, () => {
            setPlayerDiscardPile(prev => [...prev, tileToDiscard]);
            simulateBotTurns();
        });
    };

    const simulateBotTurns = async () => {
        if (botsPlayingRef.current) return;
        botsPlayingRef.current = true;
        const setBotActive = (idx: number) => setOpponents(prev => prev.map((p, i) => ({ ...p, isActive: i === idx })));

        // Right Bot (Marcus)
        setBotActive(2); await new Promise(r => setTimeout(r, 800));
        const fromDiscardR = (playerDiscardPile.length > 0 && Math.random() < 0.3);
        let tileR: TileData;
        if (fromDiscardR) {
            tileR = playerDiscardPile[playerDiscardPile.length - 1];
            setPlayerDiscardPile(prev => prev.slice(0, -1));
        } else {
            tileR = deck[deck.length - 1];
            setDeck(prev => prev.slice(0, -1));
        }
        const sourceSelectorR = fromDiscardR ? '[data-target="discard-zone"]' : '[data-source="draw-deck"]';
        await new Promise<void>(res => animateMove(sourceSelectorR, '[data-source="opponent-avatar-right"]', tileR, () => res()));
        await new Promise(r => setTimeout(r, 400));
        await new Promise<void>(res => animateMove('[data-source="opponent-avatar-right"]', '[data-target="opponent-drop-top"]', tileR, () => {
            setRightDiscardPile(prev => [...prev, tileR]);
            res();
        }));

        // Top Bot (Victor)
        setBotActive(0); await new Promise(r => setTimeout(r, 800));
        const fromDiscardT = (rightDiscardPile.length > 0 && Math.random() < 0.3);
        let tileT: TileData;
        if (fromDiscardT) {
            tileT = rightDiscardPile[rightDiscardPile.length - 1];
            setRightDiscardPile(prev => prev.slice(0, -1));
        } else {
            tileT = deck[deck.length - 1];
            setDeck(prev => prev.slice(0, -1));
        }
        const sourceSelectorT = fromDiscardT ? '[data-target="opponent-drop-top"]' : '[data-source="draw-deck"]';
        await new Promise<void>(res => animateMove(sourceSelectorT, '[data-source="opponent-avatar-top"]', tileT, () => res()));
        await new Promise(r => setTimeout(r, 400));
        await new Promise<void>(res => animateMove('[data-source="opponent-avatar-top"]', '[data-target="opponent-drop-left"]', tileT, () => {
            setTopDiscardPile(prev => [...prev, tileT]);
            res();
        }));

        // Left Bot (Elena)
        setBotActive(1); await new Promise(r => setTimeout(r, 800));
        const fromDiscardL = (topDiscardPile.length > 0 && Math.random() < 0.3);
        let tileL: TileData;
        if (fromDiscardL) {
            tileL = topDiscardPile[topDiscardPile.length - 1];
            setTopDiscardPile(prev => prev.slice(0, -1));
        } else {
            tileL = deck[deck.length - 1];
            setDeck(prev => prev.slice(0, -1));
        }
        const sourceSelectorL = fromDiscardL ? '[data-target="opponent-drop-left"]' : '[data-source="draw-deck"]';
        await new Promise<void>(res => animateMove(sourceSelectorL, '[data-source="opponent-avatar-left"]', tileL, () => res()));
        await new Promise(r => setTimeout(r, 400));
        await new Promise<void>(res => animateMove('[data-source="opponent-avatar-left"]', '[data-target="discard-pile"]', tileL, () => {
            setDiscardPile(prev => [...prev, tileL]);
            res();
        }));

        setOpponents(prev => prev.map(p => ({ ...p, isActive: false })));
        botsPlayingRef.current = false;
        setTurnPhase(TurnPhase.DRAW);
    };

    const handleFinishTurn = () => {
        if (turnPhase !== TurnPhase.DISCARD || !selectedTileId) return;
        const tempRack = [...playerTiles];
        const idx = tempRack.findIndex(t => t?.id === selectedTileId);
        tempRack[idx] = null;
        const val = validateHandGroups(tempRack);
        if (val.valid) { playSound('win'); setStatus(GameStatus.VICTORY); }
        else { playSound('error'); setErrorMsg(val.reason || "Invalid Hand"); setTimeout(() => setErrorMsg(null), 3000); }
    };

    const getInstructionText = () => {
        if (status === GameStatus.VICTORY || showSplash) return "";
        if (turnPhase === TurnPhase.DRAW) return "Your Turn: Draw a tile";
        if (turnPhase === TurnPhase.DISCARD) return "Your Turn: Discard a tile to the RIGHT";
        return "Opponents are playing...";
    };

    // --- DnD Logic ---
    const [activeDragTile, setActiveDragTile] = useState<TileData | null>(null);
    const [debugLog, setDebugLog] = useState<string>('Ready');

    const sensors = useSensors(
        useSensor(PointerSensor, { activationConstraint: { distance: 5 } }),
        useSensor(TouchSensor, { activationConstraint: { delay: 150, tolerance: 10 } })
    );

    const handleDragStart = (e: DragStartEvent) => {
        const tile = e.active.data.current?.tile;
        if (tile) { setActiveDragTile(tile); setSelectedTileId(tile.id); setDebugLog(`Start: ${tile.id}`); }
    };

    const handleDragMove = (e: DragMoveEvent) => {
        setDebugLog(`Move: dx=${Math.round(e.delta.x)} dy=${Math.round(e.delta.y)}`);
    };

    const handleDragEnd = (e: DragEndEvent) => {
        const { active, over } = e;
        setActiveDragTile(null);
        if (!over) return;
        const fromIdx = active.data.current?.index as number;
        if (over.id.toString().startsWith('slot-')) {
            const toIdx = over.data.current?.index as number;
            if (fromIdx !== undefined && toIdx !== undefined && fromIdx !== toIdx) handleMoveTile(fromIdx, toIdx);
        } else if (over.id === 'discard-zone') {
            const currentTiles = [...playerTiles];
            const tileToDiscard = currentTiles[fromIdx];
            if (tileToDiscard && turnPhase === TurnPhase.DISCARD) {
                currentTiles[fromIdx] = null; setPlayerTiles(currentTiles); setSelectedTileId(null); setTurnPhase(TurnPhase.WAITING);
                animateMove(`[data-tile-id="${tileToDiscard.id}"]`, `[data-target="opponent-drop-right"]`, tileToDiscard, () => {
                    setPlayerDiscardPile(p => [...p, tileToDiscard]); simulateBotTurns();
                });
            }
        }
    };

    return (
        <DndContext
            sensors={sensors}
            collisionDetection={closestCenter}
            onDragStart={handleDragStart}
            onDragMove={handleDragMove}
            onDragEnd={handleDragEnd}
            measuring={{ draggable: { strategy: MeasuringStrategy.Always } }}
        >
            <div className={`relative w-full h-screen overflow-hidden ${status !== GameStatus.MENU ? 'felt-texture' : ''}`}>
                <div className="absolute inset-0 flex items-center justify-center pointer-events-none">
                    <div
                        className="relative flex flex-col items-center justify-center pointer-events-auto"
                        style={{
                            width: TARGET_WIDTH,
                            height: TARGET_HEIGHT,
                            transform: `scale(${scale})`,
                            transformOrigin: 'center center',
                            flexShrink: 0
                        }}
                    >
                        <div className="absolute top-0 left-0 z-50 bg-black/80 text-green-400 font-mono text-xs p-2 pointer-events-none">
                            DEBUG: {debugLog}
                        </div>

                        {showSplash ? <SplashScreen onFinish={() => setShowSplash(false)} /> : (
                            <>
                                {status === GameStatus.MENU && <MainMenu onPlay={handleStartGame} />}
                                {status !== GameStatus.MENU && (
                                    <>
                                        <div className="absolute top-4 left-4 z-30">
                                            <button onClick={() => setStatus(GameStatus.MENU)} className="bg-black/40 text-white p-3 rounded-full border border-white/10 shadow-lg"><svg className="w-6 h-6" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M10.5 19.5L3 12m0 0l7.5-7.5M3 12h18" /></svg></button>
                                        </div>
                                        <Opponent player={opponents[0]} position="top" lastDiscard={rightDiscardPile[rightDiscardPile.length - 1]} />
                                        <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-[85%] flex flex-col items-center gap-12 z-20 w-full px-16">
                                            <div className="w-full flex items-center justify-between">
                                                <Opponent player={opponents[1]} position="left-inline" lastDiscard={topDiscardPile[topDiscardPile.length - 1]} />
                                                <BoardCenter deckCount={deck.length} discardPile={discardPile} onDrawFromDeck={drawFromDeck} onDrawFromDiscard={drawFromDiscard} indicatorTile={indicatorTile} canDraw={turnPhase === TurnPhase.DRAW} isDiscardActive={turnPhase === TurnPhase.DISCARD} />
                                                <Opponent player={opponents[2]} position="right-inline" lastDiscard={playerDiscardPile[playerDiscardPile.length - 1]} isDroppable={turnPhase === TurnPhase.DISCARD} dropId="discard-zone" />
                                            </div>
                                            <div className="flex flex-col items-center gap-4">
                                                <div className="bg-black/50 text-amber-100 px-6 py-2 rounded-full border border-white/10 shadow-xl text-sm font-medium animate-pulse">{getInstructionText()}</div>
                                                {errorMsg && <div className="bg-red-600/90 text-white px-6 py-2 rounded-full shadow-xl text-sm font-bold animate-bounce">{errorMsg}</div>}
                                            </div>
                                        </div>
                                        <div className="mt-auto w-full flex flex-col items-center pb-4">
                                            <div className="w-full max-w-[800px] flex items-end justify-end px-6 mb-2 relative z-30">
                                                <div className="absolute left-1/2 -translate-x-1/2 bottom-0 flex gap-2">
                                                    {turnPhase === TurnPhase.DISCARD && selectedTileId && (
                                                        <>
                                                            <button onClick={discardSelectedTile} className="bg-red-600 hover:bg-red-500 text-white font-bold py-3 px-6 rounded-t-xl shadow-lg border-red-400 animate-bounce">DISCARD</button>
                                                            <button onClick={handleFinishTurn} className="bg-green-600 hover:bg-green-500 text-white font-bold py-3 px-6 rounded-t-xl shadow-lg border-green-400 animate-pulse">FINISH GAME</button>
                                                        </>
                                                    )}
                                                </div>
                                                <button onClick={handleQuickSort} className="h-12 w-12 rounded-full bg-blue-600 hover:bg-blue-500 shadow-lg flex items-center justify-center text-white"><svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M3 4h13M3 8h9m-9 4h6m4 0l4-4m0 0l4 4m-4-4v12" /></svg></button>
                                            </div>
                                            <PlayerRack tiles={playerTiles as TileData[]} selectedTileId={selectedTileId} onTileClick={handleTileClick} onEmptySlotClick={handleEmptySlotClick} onTileMove={handleMoveTile} />
                                        </div>
                                    </>
                                )}
                            </>
                        )}
                    </div>
                </div>

                <DragOverlay zIndex={1000} dropAnimation={null}>
                    {activeDragTile && (
                        <div style={{
                            transform: `scale(${scale})`,
                            transformOrigin: '0 0',
                            pointerEvents: 'none',
                        }}>
                            <Tile tile={activeDragTile} selected={true} scale={1} fluid={false} className="shadow-2xl scale-110 cursor-grabbing" />
                        </div>
                    )}
                </DragOverlay>

                {animatingTile && (
                    <div style={animatingTile.style} className="z-[100]">
                        <Tile tile={animatingTile.tile} fluid={false} className="" />
                    </div>
                )}
            </div>
            {status === GameStatus.VICTORY && (
                <VictoryScreen score={500} onPlayAgain={() => setStatus(GameStatus.MENU)} playerScores={finalScores} isDeckEmpty={finalScores !== null} />
            )}
        </DndContext>
    );
};

export default App;