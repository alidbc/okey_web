import React from 'react';

interface MainMenuProps {
  onPlay: () => void;
}

const MainMenu: React.FC<MainMenuProps> = ({ onPlay }) => {
  return (
    <div className="absolute inset-0 z-50 bg-background-light dark:bg-background-dark text-white flex flex-col items-center justify-center p-0 m-0 select-none font-display">
      {/* Main Background (Hyper-realistic Casino Scene) */}
      <div className="fixed inset-0 bg-casino z-0" data-alt="Blurred luxury casino table with okey tiles"></div>
      
      {/* UI Overlay Layer */}
      <div className="relative z-10 w-full h-full flex flex-col justify-between p-6 lg:p-10 landscape:flex-row landscape:flex-wrap">
        
        {/* Top Left: Profile Glass Plate */}
        <div className="flex items-start justify-start w-full lg:w-1/2">
          <div className="glass-panel flex items-center gap-4 p-2 pr-6 rounded-full border-l-4 border-primary/50">
            <div className="relative">
              <div className="size-14 rounded-full border-2 border-primary/40 p-0.5 overflow-hidden">
                <img 
                    className="size-full rounded-full object-cover" 
                    alt="Modern male avatar profile picture" 
                    src="https://lh3.googleusercontent.com/aida-public/AB6AXuCnuQ6Gm1OskQt8MRl2gEDwaRFzBwAjmpqQN7Ic_logX72YA36_NJBZLZxXElyUfT7tJFjW5wBabUai2XOeEysbU6sAJ-Ac_mHFynMKBdXUb78qp2oJfIdGaG75fIWyd4TYzaRUs2FmgME3Elw06O8GypU2FOOcMdCJrXUPL_qzqQmXbXmofk9SJrkO5tATYFx_1vx5-_wMaXPAw_8RvURFvdKLxzm65sf-CbvblnJN6Qr27aQIg3s_NIHynZ_uPmslIw8LELIh6Exv"
                />
              </div>
              <div className="absolute -bottom-1 -right-1 bg-primary text-[10px] font-bold px-2 py-0.5 rounded-full border border-background-dark">
                42
              </div>
            </div>
            <div className="flex flex-col">
              <span className="text-sm font-bold tracking-tight text-white/90">OkeyPro_99</span>
              <div className="w-32 h-1.5 bg-white/10 rounded-full mt-1 overflow-hidden">
                <div className="h-full bg-primary w-[75%] rounded-full shadow-[0_0_8px_#ea2a33]"></div>
              </div>
              <span className="text-[10px] font-medium text-white/50 mt-1 uppercase tracking-widest">Premium Member</span>
            </div>
          </div>
        </div>

        {/* Top Right: Currency Boxes */}
        <div className="flex items-start justify-end gap-3 w-full lg:w-1/2 mt-4 lg:mt-0">
          <div className="glass-panel flex items-center gap-3 px-4 py-2 rounded-full border border-accent-gold/20">
            <span className="material-symbols-outlined text-accent-gold text-xl">monetization_on</span>
            <span className="text-sm font-bold tracking-wide">1,250,000</span>
            <button className="size-6 bg-accent-gold/20 rounded-full flex items-center justify-center hover:bg-accent-gold/30 transition-colors">
              <span className="material-symbols-outlined text-accent-gold text-xs font-bold">add</span>
            </button>
          </div>
          <div className="glass-panel flex items-center gap-3 px-4 py-2 rounded-full border border-accent-blue/20">
            <span className="material-symbols-outlined text-accent-blue text-xl">diamond</span>
            <span className="text-sm font-bold tracking-wide">450</span>
            <button className="size-6 bg-accent-blue/20 rounded-full flex items-center justify-center hover:bg-accent-blue/30 transition-colors">
              <span className="material-symbols-outlined text-accent-blue text-xs font-bold">add</span>
            </button>
          </div>
        </div>

        {/* Center: Primary Action Buttons */}
        <div className="absolute inset-0 flex items-center justify-center pointer-events-none">
          <div className="flex flex-col gap-4 w-full max-w-[320px] pointer-events-auto">
            {/* PLAY NOW */}
            <button 
                onClick={onPlay}
                className="group relative flex items-center justify-center h-16 rounded-full bg-gradient-to-r from-primary via-[#ff4d4d] to-primary premium-glow btn-shadow overflow-hidden transition-all active:scale-95"
            >
              <div className="absolute inset-0 bg-white/10 group-hover:bg-white/20 transition-colors"></div>
              <span className="relative text-xl font-black tracking-widest text-white italic">PLAY NOW</span>
            </button>

            {/* PLAY WITH FRIENDS */}
            <button className="group relative flex items-center justify-center h-14 rounded-full bg-gradient-to-r from-accent-blue via-[#60a5fa] to-accent-blue btn-shadow overflow-hidden transition-all active:scale-95">
              <div className="absolute inset-0 bg-white/10 group-hover:bg-white/20 transition-colors"></div>
              <div className="flex items-center gap-2 relative">
                <span className="material-symbols-outlined text-white">group</span>
                <span className="text-sm font-bold tracking-widest text-white uppercase">Play With Friends</span>
              </div>
            </button>

            {/* TOURNAMENTS */}
            <button className="group relative flex items-center justify-center h-14 rounded-full bg-gradient-to-r from-accent-purple via-[#c084fc] to-accent-purple btn-shadow overflow-hidden transition-all active:scale-95">
              <div className="absolute inset-0 bg-white/10 group-hover:bg-white/20 transition-colors"></div>
              <div className="flex items-center gap-2 relative">
                <span className="material-symbols-outlined text-white">emoji_events</span>
                <span className="text-sm font-bold tracking-widest text-white uppercase">Tournaments</span>
              </div>
              {/* Notification Badge */}
              <div className="absolute top-2 right-4 flex size-2 bg-primary rounded-full animate-pulse shadow-[0_0_8px_#ea2a33]"></div>
            </button>
          </div>
        </div>

        {/* Bottom: Secondary Navigation Dock */}
        <div className="w-full flex justify-center items-end mt-auto">
          <div className="glass-panel flex items-center gap-6 px-8 py-3 rounded-full border-b border-white/5">
            {/* Settings */}
            <a className="flex flex-col items-center gap-1 group" href="#">
              <div className="size-12 rounded-full flex items-center justify-center bg-white/5 group-hover:bg-white/10 transition-all group-active:scale-90">
                <span className="material-symbols-outlined text-white/70 group-hover:text-white transition-colors">settings</span>
              </div>
            </a>
            {/* Shop */}
            <a className="flex flex-col items-center gap-1 group relative" href="#">
              <div className="size-12 rounded-full flex items-center justify-center bg-white/5 group-hover:bg-white/10 transition-all group-active:scale-90">
                <span className="material-symbols-outlined text-white/70 group-hover:text-white transition-colors">shopping_cart</span>
              </div>
              <div className="absolute -top-1 -right-1 bg-primary text-[8px] font-bold h-4 min-w-4 flex items-center justify-center px-1 rounded-full border border-background-dark">NEW</div>
            </a>
            {/* Leaderboard */}
            <a className="flex flex-col items-center gap-1 group" href="#">
              <div className="size-12 rounded-full flex items-center justify-center bg-white/5 group-hover:bg-white/10 transition-all group-active:scale-90 border-t border-white/10">
                <span className="material-symbols-outlined text-white/70 group-hover:text-white transition-colors">leaderboard</span>
              </div>
            </a>
            {/* Mail */}
            <a className="flex flex-col items-center gap-1 group relative" href="#">
              <div className="size-12 rounded-full flex items-center justify-center bg-white/5 group-hover:bg-white/10 transition-all group-active:scale-90">
                <span className="material-symbols-outlined text-white/70 group-hover:text-white transition-colors">mail</span>
              </div>
              <div className="absolute top-0 right-0 size-3 bg-primary rounded-full border-2 border-background-dark"></div>
            </a>
          </div>
        </div>
      </div>

      {/* Decorative Elements */}
      <div className="fixed top-0 left-0 w-full h-32 bg-gradient-to-b from-black/40 to-transparent pointer-events-none z-1"></div>
      <div className="fixed bottom-0 left-0 w-full h-32 bg-gradient-to-t from-black/40 to-transparent pointer-events-none z-1"></div>
    </div>
  );
};

export default MainMenu;