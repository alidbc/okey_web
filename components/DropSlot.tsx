import React from 'react';
import { useDroppable } from '@dnd-kit/core';

interface DropSlotProps {
    id: string;
    label: string;
    isActive?: boolean;
    className?: string;
    children?: React.ReactNode;
}

const DropSlot: React.FC<DropSlotProps> = ({ id, label, isActive = false, className = '', children }) => {
    const { setNodeRef, isOver } = useDroppable({
        id,
        disabled: !isActive
    });

    const isTargeting = isActive && isOver;
    const activeStyle = isTargeting ? 'ring-4 ring-yellow-400/50 scale-105 border-yellow-400/50 bg-yellow-400/5' : '';

    return (
        <div
            ref={isActive ? setNodeRef : null}
            className={`
                relative w-16 h-24 rounded-sm border-2 border-dashed border-white/20 
                flex items-center justify-center transition-all duration-300
                ${activeStyle}
                ${className}
            `}
            data-target={id}
        >
            {children ? (
                children
            ) : (
                <div className="flex flex-col items-center gap-1 opacity-20 group-hover:opacity-40 transition-opacity">
                    <span className="text-[10px] font-bold uppercase tracking-widest text-white leading-none">{label}</span>
                </div>
            )}

            {/* Hover/Active Indicator */}
            {isActive && !children && (
                <div className={`absolute inset-0 rounded-lg bg-yellow-400/0 transition-colors ${isOver ? 'bg-yellow-400/10' : ''}`}></div>
            )}
        </div>
    );
};

export default DropSlot;
