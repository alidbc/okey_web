import React, { useEffect, useState } from 'react';

interface SplashScreenProps {
  onFinish: () => void;
}

const SplashScreen: React.FC<SplashScreenProps> = ({ onFinish }) => {
  const [progress, setProgress] = useState(0);

  useEffect(() => {
    // Simulate loading progress
    const interval = setInterval(() => {
      setProgress(prev => {
        if (prev >= 100) {
          clearInterval(interval);
          return 100;
        }
        // Random increment between 1 and 4
        return Math.min(prev + Math.floor(Math.random() * 4) + 1, 100);
      });
    }, 50);

    // Hard finish after approx 2.5 seconds to match the feel
    const timeout = setTimeout(() => {
      onFinish();
    }, 2500);

    return () => {
      clearInterval(interval);
      clearTimeout(timeout);
    };
  }, [onFinish]);

  return (
    <div className="fixed inset-0 z-[100] bg-[#0a1a0f] font-display antialiased overflow-hidden select-none flex flex-col items-center justify-center">
        {/* Embedded Styles for this specific screen to keep it self-contained based on the provided design */}
        <style>{`
            .felt-texture-splash {
                background-color: #0a1a0f;
                background-image: radial-gradient(circle at center, #14321d 0%, #0a1a0f 100%);
                position: relative;
                overflow: hidden;
            }
            .felt-texture-splash::after {
                content: "";
                position: absolute;
                top: 0;
                left: 0;
                width: 100%;
                height: 100%;
                opacity: 0.05;
                pointer-events: none;
                background-image: url(https://lh3.googleusercontent.com/aida-public/AB6AXuCkO2mW0sBgLiBbfivHGGn4N1zs0jwGyt1TSZMIVsmpQz5JdYVWWDJhsm-OM9hdlRJr9hG8B90qo3P9sEFjcPImygV7mcSqfhjZbotYrAmKxI4iORU3n6TeERObLRPEuOhbJbxdEl_2HVGf3JW5Din_hbjN1sgYIZkJ_X9QV7fs1tUE3P4PVfA3k7ALXUjDuAlT1WfPQ2uYFGy9rzVJCa10uW3K6CpmzPu9z6ieQa7CwTP0klUwKb0nEK5lY1MEPNANHeBt9d3mZZyN);
            }
            .gold-text {
                background: linear-gradient(to bottom, #f9e1a1 0%, #d4af37 40%, #8a6d3b 100%);
                -webkit-background-clip: text;
                -webkit-text-fill-color: transparent;
                filter: drop-shadow(0 4px 4px rgba(0, 0, 0, 0.5));
            }
            .sparkle {
                position: absolute;
                width: 2px;
                height: 2px;
                background: #d4af37;
                border-radius: 50%;
                opacity: 0.3;
            }
            .stone-shadow {
                box-shadow: 
                  inset 0 1px 0 rgba(255,255,255,0.2),
                  inset 0 -1px 0 rgba(0,0,0,0.4),
                  0 10px 20px rgba(0,0,0,0.5);
            }
        `}</style>

        <div className="absolute inset-0 felt-texture-splash"></div>

        {/* Sparkles */}
        <div className="sparkle top-[20%] left-[15%]"></div>
        <div className="sparkle top-[40%] left-[80%]"></div>
        <div className="sparkle top-[70%] left-[25%]"></div>
        <div className="sparkle top-[10%] left-[60%]"></div>
        <div className="sparkle top-[85%] left-[75%]"></div>

        {/* Hero Section */}
        <div className="flex flex-col items-center justify-center space-y-8 z-10">
            {/* Central Okey Stone */}
            <div className="relative group animate-bounce-slow">
                {/* Main Stone Image Container */}
                <div className="w-64 h-80 bg-[#fdfdfd] rounded-[2rem] flex flex-col items-center justify-center border-b-8 border-r-8 border-gray-300 stone-shadow relative overflow-hidden shadow-2xl transform hover:scale-105 transition-transform duration-500">
                    {/* Subtle lighting glint */}
                    <div className="absolute inset-0 bg-gradient-to-tr from-transparent via-white/20 to-transparent"></div>
                    {/* Stone Content (10 of Hearts) */}
                    <div className="flex flex-col items-center">
                        <span className="text-[#ea2a33] text-9xl font-bold leading-none tracking-tighter font-sans">10</span>
                        <div className="mt-2 text-[#ea2a33]">
                            <span className="material-symbols-outlined !text-7xl fill-current" style={{ fontVariationSettings: "'FILL' 1" }}>favorite</span>
                        </div>
                    </div>
                </div>
                {/* Rim Light Glow */}
                <div className="absolute -inset-4 bg-white/5 blur-3xl rounded-full -z-10 animate-pulse"></div>
            </div>

            {/* Title Section */}
            <div className="text-center space-y-2">
                <h1 className="text-5xl md:text-7xl lg:text-8xl font-black tracking-[0.15em] gold-text uppercase font-display">
                    OKEY MASTER
                </h1>
                <div className="h-1 w-32 bg-gradient-to-r from-transparent via-[#d4af37] to-transparent mx-auto opacity-50"></div>
            </div>
        </div>

        {/* Bottom Loading Section */}
        <div className="absolute bottom-12 w-full max-w-md px-8 flex flex-col items-center gap-4 z-20">
            {/* Progress Info */}
            <div className="flex justify-between w-full items-end">
                <span className="text-white/60 text-xs font-medium tracking-widest uppercase">Loading high-fidelity assets...</span>
                <span className="text-[#d4af37] text-sm font-bold tabular-nums">{progress}%</span>
            </div>
            {/* Progress Bar */}
            <div className="w-full h-1.5 bg-black/40 rounded-full overflow-hidden border border-white/5">
                <div 
                    className="h-full bg-gradient-to-r from-[#8a6d3b] via-[#d4af37] to-[#f9e1a1] rounded-full shadow-[0_0_10px_rgba(212,175,55,0.5)] transition-all duration-75 ease-out" 
                    style={{ width: `${progress}%` }}
                ></div>
            </div>
            {/* Footer Meta */}
            <div className="flex items-center gap-2 mt-4 opacity-30">
                <span className="material-symbols-outlined text-white text-xs">verified_user</span>
                <p className="text-white text-[10px] uppercase tracking-[0.2em]">Secure Premium Server Connection</p>
            </div>
        </div>

        {/* Ambient Vignette Overlays */}
        <div className="pointer-events-none absolute inset-0 bg-gradient-to-t from-black/60 via-transparent to-black/40 z-10"></div>
        <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(circle_at_center,transparent_0%,rgba(0,0,0,0.4)_100%)] z-10"></div>
    </div>
  );
};

export default SplashScreen;