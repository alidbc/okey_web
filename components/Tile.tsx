import React from 'react';
import { TileData, TileColor } from '../types';

interface TileProps {
  tile: TileData;
  onClick?: (e: React.MouseEvent) => void;
  selected?: boolean;
  scale?: number;
  className?: string;
  faceDown?: boolean;
  /** If true, tile fills its parent container width (used in rack slots). */
  fluid?: boolean;
}

const Tile: React.FC<TileProps> = ({ tile, onClick, selected, scale = 1, className = '', faceDown = false, fluid = false }) => {

  const getColor = (color: TileColor) => {
    switch (color) {
      case TileColor.RED: return '#d32f2f';
      case TileColor.BLUE: return '#1976d2';
      case TileColor.BLACK: return '#212121';
      case TileColor.YELLOW: return '#fbc02d';
      case TileColor.JOKER: return '#388e3c';
      default: return '#212121';
    }
  };

  // Tile.png is visually portrait — ~3:4 ratio
  const baseStyle: React.CSSProperties = {
    transform: selected ? `translateY(-15%) scale(${scale})` : `scale(${scale})`,
    transition: 'transform 0.15s cubic-bezier(0.34, 1.56, 0.64, 1)',
    position: 'relative',
    display: 'inline-flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    cursor: 'pointer',
    userSelect: 'none',
    flexShrink: 0,
    backgroundImage: 'url(/Tile.png)',
    backgroundSize: '100% 100%',
    backgroundRepeat: 'no-repeat',
    backgroundPosition: 'center',
    // Size: fluid fills parent width with natural aspect ratio; fixed uses explicit px
    ...(fluid ? {
      width: '100%',
      aspectRatio: '218 / 313',
    } : {
      width: '65px',
      aspectRatio: '218 / 313',
    }),
  };

  if (faceDown) {
    return (
      <div
        className={`relative rounded-md shadow-lg border border-amber-900/50 ${className}`}
        style={{
          ...(fluid ? { width: '100%', aspectRatio: '218 / 313' } : { width: '65px', aspectRatio: '218 / 313' }),
          transform: `scale(${scale})`,
          background: 'linear-gradient(45deg, #5D4037 25%, #4E342E 25%, #4E342E 50%, #5D4037 50%, #5D4037 75%, #4E342E 75%, #4E342E 100%)',
          backgroundSize: '12px 12px',
          boxShadow: '2px 2px 4px rgba(0,0,0,0.5)',
          position: 'relative',
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'center',
          cursor: 'pointer',
          userSelect: 'none',
          flexShrink: 0,
        }}
        onClick={onClick}
      >
        <div className="absolute inset-0 rounded-md bg-black/10 shadow-inner"></div>
        <div className="absolute inset-0 flex items-center justify-center">
          <div className="w-8 h-8 rounded-full border-2 border-amber-800/30 flex items-center justify-center">
            <span className="text-amber-100/50 font-serif font-bold text-[10px]">OKEY</span>
          </div>
        </div>
      </div>
    );
  }

  const color = getColor(tile.color);

  return (
    <div
      onClick={onClick}
      style={baseStyle}
      className={`${selected ? 'z-50' : 'hover:-translate-y-1 z-10'} ${className}`}
    >
      <div style={{ position: 'relative', zIndex: 10, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', width: '100%', height: '100%' }}>
        {tile.isFakeOkey ? (
          <svg viewBox="0 0 24 24" fill={color} style={{ width: '55%', height: '55%' }}>
            <path d="M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M12,4C9.5,4 7.29,5.14 5.9,7L7.47,8.57C8.42,7.6 9.64,7 11,7C12.55,7 13.92,7.77 14.77,9H17.9C16.85,6.07 14.06,4 12,4M12,20C14.5,20 16.71,18.86 18.1,17L16.53,15.43C15.58,16.4 14.36,17 13,17C11.45,17 10.08,16.23 9.23,15H6.1C7.15,17.93 9.94,20 12,20M7,12A1,1 0 0,0 6,13A1,1 0 0,0 7,14A1,1 0 0,0 8,13A1,1 0 0,0 7,12M17,12A1,1 0 0,0 16,13A1,1 0 0,0 17,14A1,1 0 0,0 18,13A1,1 0 0,0 17,12Z" />
          </svg>
        ) : (
          <>
            <span style={{
              fontSize: 'clamp(14px, 4vw, 30px)',
              fontWeight: 900,
              lineHeight: 1,
              color,
              fontFamily: 'Roboto, sans-serif',
              letterSpacing: '-1px',
              textShadow: `
                0px 1px 1px rgba(0,0,0,0.45),
                0px -1px 1px rgba(255,255,255,0.6)
              `,
            }}>
              {tile.value}
            </span>
            <svg viewBox="0 0 24 24" fill={color} style={{
              width: '32%',
              height: 'auto',
              marginTop: '2px',
              filter: 'drop-shadow(0px 1px 0.5px rgba(0,0,0,0.45)) drop-shadow(0px -1px 0.5px rgba(255,255,255,0.6))',
            }}>
              <path d="M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z" />
            </svg>
          </>
        )}
      </div>
    </div>
  );
};

export default Tile;