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
    MouseSensor,
    TouchSensor,
    DragStartEvent,
    DragEndEvent,
    closestCenter
} from '@dnd-kit/core';

// --- Validation Logic (Unchanged) ---

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
        if (!isSet(cluster) && !isRun(cluster)) return { valid: false, reason: "Found a group that is neither a valid Set nor a Run." };
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
            if (startVal < 1) return false;
            if (endVal > 14) return false;
        } else {
            if (startVal < 1) return false;
            if (endVal > 13) return false;
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

    // -- Discard Piles (The circular flow) --
    // 1. Left Opponent -> Player (Input Pile)
    const [discardPile, setDiscardPile] = useState<TileData[]>([]);
    // 2. Player -> Right Opponent
    const [playerDiscardPile, setPlayerDiscardPile] = useState<TileData[]>([]);
    // 3. Right Opponent -> Top Opponent
    const [rightDiscardPile, setRightDiscardPile] = useState<TileData[]>([]);
    // 4. Top Opponent -> Left Opponent
    const [topDiscardPile, setTopDiscardPile] = useState<TileData[]>([]);

    const [indicatorTile, setIndicatorTile] = useState<TileData | null>(null);

    // Selection state
    const [selectedTileId, setSelectedTileId] = useState<string | null>(null);
    const [errorMsg, setErrorMsg] = useState<string | null>(null);

    // Animation State
    const [animatingTile, setAnimatingTile] = useState<{ tile: TileData, style: React.CSSProperties } | null>(null);

    // Final scores when game ends due to empty deck
    const [finalScores, setFinalScores] = useState<{ playerName: string; score: number; isWinner: boolean }[] | null>(null);

    const [opponents, setOpponents] = useState<Player[]>(MOCK_PLAYERS);

    const botsPlayingRef = useRef(false);

    // Scaling Logic
    const [windowSize, setWindowSize] = useState({
        width: typeof window !== 'undefined' ? window.innerWidth : 1200,
        height: typeof window !== 'undefined' ? window.innerHeight : 800
    });

    useEffect(() => {
        const handleResize = () => {
            setWindowSize({ width: window.innerWidth, height: window.innerHeight });
        };
        window.addEventListener('resize', handleResize);
        return () => window.removeEventListener('resize', handleResize);
    }, []);

    // We design for a 1280x720 canvas
    const TARGET_WIDTH = 1280;
    const TARGET_HEIGHT = 720;
    const scale = Math.min(windowSize.width / TARGET_WIDTH, windowSize.height / TARGET_HEIGHT);

    // Initialize Game
    useEffect(() => {
        preloadSounds();
        // Do not start game automatically anymore
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    const handleStartGame = () => {
        playSound('click');
        startNewGame();
        // Delay slightly to allow sound to play and UI to transition
        setTimeout(() => {
            setStatus(GameStatus.PLAYING);
        }, 100);
    };

    const startNewGame = () => {
        playSound('shuffle');
        let newDeck = createDeck();
        const indicatorIndex = Math.floor(Math.random() * newDeck.length);
        const indicator = newDeck[indicatorIndex];
        newDeck.splice(indicatorIndex, 1);
        setIndicatorTile(indicator);

        newDeck = newDeck.map(tile => {
            const t = { ...tile, isWildcard: false, virtualValue: undefined, virtualColor: undefined };
            if (t.isFakeOkey) t.isWildcard = true;
            return t;
        });

        const playerHandSize = 15;
        const initialPlayerTiles = new Array(26).fill(null);
        for (let i = 0; i < playerHandSize; i++) {
            if (newDeck.length > 0) initialPlayerTiles[i] = newDeck.pop() || null;
        }

        // Remove tiles for bots (conceptually)
        for (let i = 0; i < 3; i++) {
            for (let j = 0; j < 14; j++) newDeck.pop();
        }

        setPlayerTiles(initialPlayerTiles);
        setDeck(newDeck);

        // Reset all piles
        setDiscardPile([]);
        setPlayerDiscardPile([]);
        setRightDiscardPile([]);
        setTopDiscardPile([]);

        // Reset opponents active state
        setOpponents(prev => prev.map(p => ({ ...p, isActive: false })));

        setTurnPhase(TurnPhase.DISCARD);
    };

    const handleTileClick = (tile: TileData) => {
        playSound('click');
        if (selectedTileId === tile.id) setSelectedTileId(null);
        else setSelectedTileId(tile.id);
        setErrorMsg(null);
    };

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
        const targetTile = newTiles[toIndex];
        newTiles[toIndex] = tileToMove;
        newTiles[fromIndex] = targetTile;
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

    // Calculate total points in a hand (for deck empty scenario)
    const calculateHandValue = (tiles: (TileData | null)[]): number => {
        return tiles.reduce((total, tile) => {
            if (!tile) return total;
            // Wildcards are worth 0 points
            if (tile.isWildcard) return total;
            // Regular tiles worth their face value (1-13)
            return total + tile.value;
        }, 0);
    };

    const drawFromDeck = () => {
        if (turnPhase !== TurnPhase.DRAW) return;

        // Check if deck is empty - trigger game end
        if (deck.length === 0) {
            // Calculate player's score
            const playerScore = calculateHandValue(playerTiles);

            // Generate random scores for bots (20-80 range)
            const botScores = [
                { playerName: 'Elena', score: Math.floor(Math.random() * 61) + 20 },
                { playerName: 'Marcus', score: Math.floor(Math.random() * 61) + 20 },
                { playerName: 'Victor', score: Math.floor(Math.random() * 61) + 20 },
            ];

            // Add player score
            const allScores = [
                { playerName: 'You', score: playerScore },
                ...botScores
            ];

            // Find winner (lowest score)
            const lowestScore = Math.min(...allScores.map(s => s.score));
            const scoresWithWinner = allScores.map(s => ({
                ...s,
                isWinner: s.score === lowestScore
            }));

            // Sort by score (lowest first)
            scoresWithWinner.sort((a, b) => a.score - b.score);

            setFinalScores(scoresWithWinner);
            setStatus(GameStatus.VICTORY);
            playSound('win');
            return;
        }

        const newDeck = [...deck];
        const tile = newDeck.pop();
        if (!tile) return;

        // Find the first empty slot in the rack
        const emptyIndex = playerTiles.findIndex(t => t === null);
        if (emptyIndex === -1) return; // No empty slots

        // Update deck immediately
        setDeck(newDeck);
        setTurnPhase(TurnPhase.WAITING);

        // Animate tile from deck to rack
        const sourceSelector = '[data-source="draw-deck"]';
        const targetSelector = `[data-target="rack-slot-${emptyIndex}"]`;

        animateMove(sourceSelector, targetSelector, tile, () => {
            addTileToRack(tile);
            setTurnPhase(TurnPhase.DISCARD);
        });
    };

    const drawFromDiscard = () => {
        if (turnPhase !== TurnPhase.DRAW) return;
        if (discardPile.length === 0) return;

        const newDiscardPile = [...discardPile];
        const tile = newDiscardPile.pop();
        if (!tile) return;

        // Find the first empty slot in the rack
        const emptyIndex = playerTiles.findIndex(t => t === null);
        if (emptyIndex === -1) return; // No empty slots

        // Update discard pile immediately
        setDiscardPile(newDiscardPile);
        setTurnPhase(TurnPhase.WAITING);

        // Animate tile from discard pile to rack
        const sourceSelector = '[data-target="discard-pile"]';
        const targetSelector = `[data-target="rack-slot-${emptyIndex}"]`;

        animateMove(sourceSelector, targetSelector, tile, () => {
            addTileToRack(tile);
            setTurnPhase(TurnPhase.DISCARD);
        });
    };

    const addTileToRack = (tile: TileData) => {
        const emptyIndex = playerTiles.findIndex(t => t === null);
        if (emptyIndex !== -1) {
            const newTiles = [...playerTiles];
            newTiles[emptyIndex] = tile;
            setPlayerTiles(newTiles);
        }
    };

    // Helper to run an animation
    const animateMove = (
        sourceSelector: string,
        targetSelector: string,
        tile: TileData,
        onComplete: () => void
    ) => {
        const sourceNode = document.querySelector(sourceSelector);
        const targetNode = document.querySelector(targetSelector);

        if (sourceNode && targetNode) {
            const startRect = sourceNode.getBoundingClientRect();
            const targetRect = targetNode.getBoundingClientRect();

            const initialStyle: React.CSSProperties = {
                position: 'fixed',
                // Center the tile (55x80) relative to the source element center
                top: startRect.top + (startRect.height - 80) / 2,
                left: startRect.left + (startRect.width - 55) / 2,
                width: 55, // Match standard tile width
                height: 80, // Match standard tile height
                zIndex: 100,
                pointerEvents: 'none',
                transition: 'all 0.5s cubic-bezier(0.2, 0.8, 0.2, 1)',
                opacity: 1
            };

            playSound('draw'); // Whoosh sound for movement
            setAnimatingTile({ tile, style: initialStyle });

            // Start move next frame
            setTimeout(() => {
                setAnimatingTile(prev => {
                    if (!prev) return null;
                    return {
                        ...prev,
                        style: {
                            ...prev.style,
                            // Center the tile relative to the target element center
                            top: targetRect.top + (targetRect.height - 80) / 2,
                            left: targetRect.left + (targetRect.width - 55) / 2,
                            transform: 'rotate(0deg) scale(1.0)'
                        }
                    };
                });
            }, 50);

            // Finish
            setTimeout(() => {
                setAnimatingTile(null);
                playSound('place');
                onComplete();
            }, 550);
        } else {
            // Fallback if DOM not found
            onComplete();
        }
    };

    const discardSelectedTile = () => {
        if (turnPhase !== TurnPhase.DISCARD) return;
        if (!selectedTileId) return;

        const index = playerTiles.findIndex(t => t?.id === selectedTileId);
        if (index === -1) return;
        const tileToDiscard = playerTiles[index];
        if (!tileToDiscard) return;

        // Special handling for Player Discard (Source is data-tile-id, not a fixed selector)
        const sourceSelector = `[data-tile-id="${tileToDiscard.id}"]`;
        const targetSelector = `[data-target="opponent-drop-right"]`;

        // 1. Remove from Rack immediately
        const newTiles = [...playerTiles];
        newTiles[index] = null;
        setPlayerTiles(newTiles);
        setSelectedTileId(null);
        setTurnPhase(TurnPhase.WAITING);

        // 2. Animate
        animateMove(sourceSelector, targetSelector, tileToDiscard, () => {
            setPlayerDiscardPile(prev => [...prev, tileToDiscard]);
            simulateBotTurns();
        });
    };

    const setBotActive = (botIndex: number) => {
        setOpponents(prev => prev.map((p, i) => ({
            ...p,
            isActive: i === botIndex
        })));
    };

    const clearAllActive = () => {
        setOpponents(prev => prev.map(p => ({ ...p, isActive: false })));
    };

    const simulateBotTurns = async () => {
        if (botsPlayingRef.current) return;
        botsPlayingRef.current = true;

        let currentDeck = [...deck];
        let currentPlayerDiscard = [...playerDiscardPile];
        let currentRightDiscard = [...rightDiscardPile];
        let currentTopDiscard = [...topDiscardPile];

        const drawFromDeckForBot = () => {
            if (currentDeck.length > 0) {
                return currentDeck.pop()!;
            } else {
                // Fallback if deck is empty
                return {
                    id: generateId(),
                    value: Math.floor(Math.random() * 13) + 1,
                    color: TileColor.RED,
                    isFakeOkey: false,
                    isWildcard: false
                } as TileData;
            }
        };

        // --- 1. Right Bot (Marcus) Plays ---
        setBotActive(2); // Marcus is index 2
        // Marcus can take from player's discard pile or draw from deck
        await new Promise(r => setTimeout(r, 800)); // Thinking time

        let tileRightDrew: TileData;
        let rightTookFromDiscard = false;

        // 30% chance to take from discard pile if available
        if (currentPlayerDiscard.length > 0 && Math.random() < 0.3) {
            tileRightDrew = currentPlayerDiscard.pop()!;
            rightTookFromDiscard = true;
            setPlayerDiscardPile([...currentPlayerDiscard]);

            // Animate from player's discard pile to right bot
            await new Promise<void>(resolve => {
                animateMove(
                    '[data-target="opponent-drop-right"]',
                    '[data-source="opponent-avatar-right"]',
                    tileRightDrew,
                    () => resolve()
                );
            });
        } else {
            // Draw from deck
            tileRightDrew = drawFromDeckForBot();
            setDeck([...currentDeck]);

            // Animate from deck to right bot
            await new Promise<void>(resolve => {
                animateMove(
                    '[data-source="draw-deck"]',
                    '[data-source="opponent-avatar-right"]',
                    tileRightDrew,
                    () => resolve()
                );
            });
        }

        // Right bot discards
        await new Promise(r => setTimeout(r, 400));
        const tileRightDiscard = tileRightDrew; // For simplicity, discard what was drawn
        await new Promise<void>(resolve => {
            animateMove(
                '[data-source="opponent-avatar-right"]',
                '[data-target="opponent-drop-top"]',
                tileRightDiscard,
                () => {
                    currentRightDiscard.push(tileRightDiscard);
                    setRightDiscardPile([...currentRightDiscard]);
                    resolve();
                }
            );
        });

        // --- 2. Top Bot (Victor) Plays ---
        setBotActive(0); // Victor is index 0
        // Victor can take from right bot's discard pile or draw from deck
        await new Promise(r => setTimeout(r, 800));

        let tileTopDrew: TileData;
        let topTookFromDiscard = false;

        if (currentRightDiscard.length > 0 && Math.random() < 0.3) {
            tileTopDrew = currentRightDiscard.pop()!;
            topTookFromDiscard = true;
            setRightDiscardPile([...currentRightDiscard]);

            await new Promise<void>(resolve => {
                animateMove(
                    '[data-target="opponent-drop-top"]',
                    '[data-source="opponent-avatar-top"]',
                    tileTopDrew,
                    () => resolve()
                );
            });
        } else {
            tileTopDrew = drawFromDeckForBot();
            setDeck([...currentDeck]);

            await new Promise<void>(resolve => {
                animateMove(
                    '[data-source="draw-deck"]',
                    '[data-source="opponent-avatar-top"]',
                    tileTopDrew,
                    () => resolve()
                );
            });
        }

        // Top bot discards
        await new Promise(r => setTimeout(r, 400));
        const tileTopDiscard = tileTopDrew;
        await new Promise<void>(resolve => {
            animateMove(
                '[data-source="opponent-avatar-top"]',
                '[data-target="opponent-drop-left"]',
                tileTopDiscard,
                () => {
                    currentTopDiscard.push(tileTopDiscard);
                    setTopDiscardPile([...currentTopDiscard]);
                    resolve();
                }
            );
        });

        // --- 3. Left Bot (Elena) Plays ---
        setBotActive(1); // Elena is index 1
        // Elena can take from top bot's discard pile or draw from deck
        await new Promise(r => setTimeout(r, 800));

        let tileLeftDrew: TileData;
        let leftTookFromDiscard = false;

        if (currentTopDiscard.length > 0 && Math.random() < 0.3) {
            tileLeftDrew = currentTopDiscard.pop()!;
            leftTookFromDiscard = true;
            setTopDiscardPile([...currentTopDiscard]);

            await new Promise<void>(resolve => {
                animateMove(
                    '[data-target="opponent-drop-left"]',
                    '[data-source="opponent-avatar-left"]',
                    tileLeftDrew,
                    () => resolve()
                );
            });
        } else {
            tileLeftDrew = drawFromDeckForBot();
            setDeck([...currentDeck]);

            await new Promise<void>(resolve => {
                animateMove(
                    '[data-source="draw-deck"]',
                    '[data-source="opponent-avatar-left"]',
                    tileLeftDrew,
                    () => resolve()
                );
            });
        }

        // Left bot discards to player's input pile
        await new Promise(r => setTimeout(r, 400));
        const tileLeftDiscard = tileLeftDrew;
        await new Promise<void>(resolve => {
            animateMove(
                '[data-source="opponent-avatar-left"]',
                '[data-target="discard-pile"]',
                tileLeftDiscard,
                () => {
                    setDiscardPile(prev => [...prev, tileLeftDiscard]);
                    resolve();
                }
            );
        });

        // Update deck state
        setDeck(currentDeck);

        clearAllActive(); // Reset bots
        botsPlayingRef.current = false;
        setTurnPhase(TurnPhase.DRAW);
        playSound('error'); // Notification that it's your turn
    };

    const handleFinishTurn = () => {
        if (turnPhase !== TurnPhase.DISCARD) return;
        if (!selectedTileId) {
            setErrorMsg("Select a tile to discard for the win.");
            playSound('error');
            return;
        }
        const tempRack = [...playerTiles];
        const index = tempRack.findIndex(t => t?.id === selectedTileId);
        if (index !== -1) tempRack[index] = null;
        const validation = validateHandGroups(tempRack);
        if (validation.valid) {
            playSound('win');
            setStatus(GameStatus.VICTORY);
        }
        else {
            playSound('error');
            setErrorMsg(validation.reason || "Invalid Hand");
            setTimeout(() => setErrorMsg(null), 3000);
        }
    };

    const triggerVictory = () => {
        playSound('win');
        setStatus(GameStatus.VICTORY);
    };

    const handleReturnToMenu = () => {
        setStatus(GameStatus.MENU);
    };

    const getInstructionText = () => {
        if (status === GameStatus.VICTORY) return "";
        switch (turnPhase) {
            case TurnPhase.DRAW: return "Your Turn: Draw a tile from the Deck or the Pile on your LEFT";
            case TurnPhase.DISCARD: return "Your Turn: Discard a tile to the RIGHT";
            case TurnPhase.WAITING: return "Opponents are playing...";
        }
    };

    // --- DnD Logic ---
    const [activeDragTile, setActiveDragTile] = useState<TileData | null>(null);
    const [debugLog, setDebugLog] = useState<string>('Ready'); // Debug state

    const sensors = useSensors(
        useSensor(MouseSensor, {
            activationConstraint: {
                distance: 8,
            },
        }),
        useSensor(TouchSensor, {
            activationConstraint: {
                delay: 150,
                tolerance: 5,
            },
        })
    );

    const handleDragStart = (event: DragStartEvent) => {
        const tile = event.active.data.current?.tile as TileData;
        if (tile) {
            setActiveDragTile(tile);
            // Also select it to show its details/actions if needed, or just visual feedback
            setSelectedTileId(tile.id);
            setDebugLog(`Drag Start: ${tile.id}`);
        }
    };

    const handleDragEnd = (event: DragEndEvent) => {
        const { active, over } = event;
        setActiveDragTile(null);

        setDebugLog(`Drag End: Active=${active.id} Over=${over?.id || 'null'}`);

        if (!over) return;

        const activeId = active.id.toString();
        // If we dragged a rack tile
        if (activeId.startsWith('tile-')) {
            const fromIndex = active.data.current?.index as number;

            // 1. Drop on Rack Slot (Reorder)
            if (over.id.toString().startsWith('slot-')) {
                const toIndex = over.data.current?.index as number;
                if (fromIndex !== undefined && toIndex !== undefined && fromIndex !== toIndex) {
                    handleMoveTile(fromIndex, toIndex);
                }
            }
            // 2. Drop on Discard Zone (Right Opponent)
            else if (over.id === 'discard-zone') {
                // We need to discard the specific tile that was dragged
                // The 'selectedTileId' might be set on drag start, but let's be safe and use 'fromIndex'
                const tileToDiscard = playerTiles[fromIndex];
                if (tileToDiscard) {
                    // We can reuse discardSelectedTile logic but we need to ensure the state is right 
                    // or just call a specific discard function
                    // Let's manually trigger the discard logic for this specific tile index
                    discardRef.current(fromIndex);
                }
            }
            // 3. Drop on Finish Zone (Indicator Tile)
            else if (over.id === 'finish-zone') {
                // Finish turn logic
                handleFinishTurn();
            }
        }
    };

    // Helper ref to allow calling discard logic from within Dnd handler without stale closures if we defined it simply
    // Actually, since we are in the component, we can just define a helper that takes an index
    const discardRef = useRef<(index: number) => void>(() => { });

    // Update the ref whenever relevant state changes
    useEffect(() => {
        discardRef.current = (index: number) => {
            if (turnPhase !== TurnPhase.DISCARD) return;

            const tileToDiscard = playerTiles[index];
            if (!tileToDiscard) return;

            // Special handling for Player Discard
            const sourceSelector = `[data-tile-id="${tileToDiscard.id}"]`;
            const targetSelector = `[data-target="opponent-drop-right"]`;

            // 1. Remove from Rack
            const newTiles = [...playerTiles];
            newTiles[index] = null;
            setPlayerTiles(newTiles);
            setSelectedTileId(null);
            setTurnPhase(TurnPhase.WAITING);

            // 2. Animate
            animateMove(sourceSelector, targetSelector, tileToDiscard, () => {
                setPlayerDiscardPile(prev => [...prev, tileToDiscard]);
                simulateBotTurns();
            });
        };
    }, [playerTiles, turnPhase, playerDiscardPile, deck]); // Dependencies for the discard logic


    return (
        <DndContext
            sensors={sensors}
            collisionDetection={closestCenter}
            onDragStart={handleDragStart}
            onDragEnd={handleDragEnd}
        >
            <div className={`relative w-full h-screen overflow-hidden flex flex-col items-center justify-center ${status !== GameStatus.MENU ? 'felt-texture' : ''}`}>
                <div
                    className="relative flex flex-col items-center justify-center pointer-events-auto"
                    style={{
                        width: `${TARGET_WIDTH}px`,
                        height: `${TARGET_HEIGHT}px`,
                        transform: `scale(${scale})`,
                        transformOrigin: 'center center',
                        flexShrink: 0
                    }}
                >

                    {/* DEBUG OVERLAY */}
                    <div className="absolute top-0 left-0 z-50 bg-black/80 text-green-400 font-mono text-xs p-2 pointer-events-none">
                        DEBUG: {debugLog}
                    </div>

                    {showSplash ? (
                        <SplashScreen onFinish={() => setShowSplash(false)} />
                    ) : (
                        <>
                            {/* --- MENU STATE --- */}
                            {status === GameStatus.MENU && (
                                <MainMenu onPlay={handleStartGame} />
                            )}

                            {/* --- GAME STATE --- */}
                            {status !== GameStatus.MENU && (
                                <>
                                    {/* Flying Tile Layer */}
                                    {animatingTile && (
                                        <div style={animatingTile.style}>
                                            <Tile tile={animatingTile.tile} className="" />
                                        </div>
                                    )}

                                    {/* Top UI Bar */}
                                    <div className="absolute top-4 left-4 z-30">
                                        <button onClick={handleReturnToMenu} className="bg-black/40 hover:bg-black/60 text-white p-3 rounded-full backdrop-blur-sm border border-white/10 shadow-lg transition-colors">
                                            <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor" className="w-6 h-6">
                                                <path strokeLinecap="round" strokeLinejoin="round" d="M10.5 19.5L3 12m0 0l7.5-7.5M3 12h18" />
                                            </svg>
                                        </button>
                                    </div>

                                    {/* Debug/Menu Buttons */}
                                    <div className="absolute top-4 right-4 z-30 flex gap-2">
                                        <button onClick={triggerVictory} className="bg-amber-600/40 hover:bg-amber-600/60 text-amber-200 px-3 py-2 rounded-full backdrop-blur-sm border border-amber-500/30 shadow-lg text-[10px] font-bold uppercase">Force Win</button>
                                        <button onClick={startNewGame} className="bg-black/40 hover:bg-black/60 text-white px-4 py-2 rounded-full backdrop-blur-sm border border-white/10 shadow-lg text-xs font-bold uppercase">Restart</button>
                                    </div>

                                    {/* Opponents */}
                                    <Opponent
                                        player={opponents[0]} // Victor (Top)
                                        position="top"
                                        lastDiscard={rightDiscardPile.length > 0 ? rightDiscardPile[rightDiscardPile.length - 1] : null}
                                    />
                                    <Opponent
                                        player={opponents[1]} // Elena (Left)
                                        position="left"
                                        lastDiscard={topDiscardPile.length > 0 ? topDiscardPile[topDiscardPile.length - 1] : null}
                                    />
                                    <Opponent
                                        player={opponents[2]} // Marcus (Right) (Discard Target)
                                        position="right"
                                        lastDiscard={playerDiscardPile.length > 0 ? playerDiscardPile[playerDiscardPile.length - 1] : null}
                                        isDroppable={turnPhase === TurnPhase.DISCARD} // New Prop for Drop Zone
                                        dropId="discard-zone"
                                    />

                                    {/* Instruction Toast */}
                                    <div className="absolute top-24 left-1/2 -translate-x-1/2 z-20 pointer-events-none flex flex-col items-center gap-2">
                                        <div className="bg-black/50 text-amber-100 px-6 py-2 rounded-full backdrop-blur border border-white/10 shadow-xl text-sm font-medium animate-pulse">
                                            {getInstructionText()}
                                        </div>
                                        {errorMsg && (
                                            <div className="bg-red-600/90 text-white px-6 py-2 rounded-full shadow-xl text-sm font-bold animate-bounce">
                                                {errorMsg}
                                            </div>
                                        )}
                                    </div>

                                    {/* Board Center Area (Stacked Deck/Indicator above Nameplate) */}
                                    <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-[70%] flex flex-col items-center gap-12 z-20">
                                        <BoardCenter
                                            deckCount={deck.length}
                                            discardPile={discardPile}
                                            onDrawFromDeck={drawFromDeck}
                                            onDrawFromDiscard={drawFromDiscard}
                                            indicatorTile={indicatorTile}
                                            canDraw={turnPhase === TurnPhase.DRAW}
                                            isDiscardActive={turnPhase === TurnPhase.DISCARD}
                                        />

                                        <div className="flex items-center gap-6">
                                            {/* Discard Pile Target (Now on Player's LEFT) */}
                                            <div
                                                className={`relative group w-[65px] h-[90px] border-2 border-dashed border-white/20 rounded-sm flex items-center justify-center transition-all
                                                ${turnPhase === TurnPhase.DRAW && discardPile.length > 0 ? 'ring-2 ring-yellow-400 hover:bg-white/5 cursor-pointer shadow-lg shadow-yellow-400/20' : ''}
                                            `}
                                                onClick={turnPhase === TurnPhase.DRAW && discardPile.length > 0 ? drawFromDiscard : undefined}
                                                data-target="discard-pile"
                                            >
                                                {discardPile.length > 0 ? (
                                                    <Tile tile={discardPile[discardPile.length - 1]} scale={1} className="shadow-lg" />
                                                ) : (
                                                    <span className="text-white/20 text-[10px] font-bold uppercase tracking-wider">Pile</span>
                                                )}
                                            </div>

                                            {/* Local Player Info (Nameplate) */}
                                            <div className={`flex items-center gap-3 bg-black/40 backdrop-blur-md px-4 py-2 rounded-sm border-2 transition-all duration-300 ${turnPhase !== TurnPhase.WAITING ? 'border-yellow-400/80 shadow-[0_0_20px_rgba(250,204,21,0.3)] bg-yellow-900/20' : 'border-white/10'}`}>
                                                <div className="relative">
                                                    <img
                                                        src="https://lh3.googleusercontent.com/aida-public/AB6AXuCnuQ6Gm1OskQt8MRl2gEDwaRFzBwAjmpqQN7Ic_logX72YA36_NJBZLZxXElyUfT7tJFjW5wBabUai2XOeEysbU6sAJ-Ac_mHFynMKBdXUb78qp2oJfIdGaG75fIWyd4TYzaRUs2FmgME3Elw06O8GypU2FOOcMdCJrXUPL_qzqQmXbXmofk9SJrkO5tATYFx_1vx5-_wMaXPAw_8RvURFvdKLxzm65sf-CbvblnJN6Qr27aQIg3s_NIHynZ_uPmslIw8LELIh6Exv"
                                                        alt="OkeyPro_99"
                                                        className={`w-12 h-12 rounded-full border-2 ${turnPhase !== TurnPhase.WAITING ? 'border-yellow-200' : 'border-white/20'}`}
                                                    />
                                                    <div className="absolute -top-1 -right-1 w-3 h-3 rounded-full bg-green-500 border-2 border-black"></div>
                                                </div>
                                                <div className="flex flex-col">
                                                    <span className={`text-sm font-bold ${turnPhase !== TurnPhase.WAITING ? 'text-yellow-100' : 'text-white/80'}`}>OkeyPro_99</span>
                                                    <div className="text-[10px] text-white/50 uppercase tracking-wider">Level 42</div>
                                                </div>
                                            </div>
                                        </div>
                                    </div>

                                    {/* Main Player Area (Bottom Rack) */}
                                    <div className="mt-auto w-full flex flex-col items-center pb-4">

                                        {/* Actions Bar above Rack */}
                                        <div className="w-full max-w-[800px] flex items-end justify-end px-6 mb-2 relative z-30">

                                            {/* Middle Action: Discard / Finish */}
                                            <div className="absolute left-1/2 -translate-x-1/2 bottom-0 flex gap-2">
                                                {turnPhase === TurnPhase.DISCARD && selectedTileId && (
                                                    <>
                                                        <button onClick={discardSelectedTile} className="bg-red-600 hover:bg-red-500 text-white font-bold py-3 px-6 rounded-t-xl shadow-[0_-4px_10px_rgba(0,0,0,0.3)] border-t border-l border-r border-red-400 animate-bounce">
                                                            DISCARD
                                                        </button>
                                                        <button onClick={handleFinishTurn} className="bg-green-600 hover:bg-green-500 text-white font-bold py-3 px-6 rounded-t-xl shadow-[0_-4px_10px_rgba(0,0,0,0.3)] border-t border-l border-r border-green-400 animate-pulse">
                                                            FINISH GAME
                                                        </button>
                                                    </>
                                                )}
                                            </div>

                                            <button onClick={handleQuickSort} className="h-12 w-12 rounded-full bg-blue-600 hover:bg-blue-500 shadow-[0_4px_0_rgb(30,58,138)] border border-blue-400 flex items-center justify-center text-white active:translate-y-1 active:shadow-none transition-all">
                                                <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M3 4h13M3 8h9m-9 4h6m4 0l4-4m0 0l4 4m-4-4v12" /></svg>
                                            </button>
                                        </div>

                                        {/* Rack */}
                                        <PlayerRack
                                            tiles={playerTiles as TileData[]}
                                            selectedTileId={selectedTileId}
                                            onTileClick={handleTileClick}
                                            onEmptySlotClick={handleEmptySlotClick}
                                            onTileMove={handleMoveTile}
                                        />
                                    </div>

                                    {status === GameStatus.VICTORY && (
                                        <VictoryScreen
                                            score={500}
                                            onPlayAgain={handleReturnToMenu}
                                            playerScores={finalScores}
                                            isDeckEmpty={finalScores !== null}
                                        />
                                    )}
                                </>
                            )}
                        </>
                    )}

                    <DragOverlay>
                        {activeDragTile && (
                            <div style={{ width: '55px', height: '80px', pointerEvents: 'none' }}>
                                <Tile tile={activeDragTile} selected={true} scale={1} fluid={true} className="shadow-2xl scale-110 cursor-grabbing" />
                            </div>
                        )}
                    </DragOverlay>

                </div>
            </div>
        </DndContext>
    );
};

export default App;