import React from 'react';

interface PlayerScore {
  playerName: string;
  score: number;
  isWinner: boolean;
}

interface VictoryScreenProps {
  score: number;
  onPlayAgain: () => void;
  playerScores?: PlayerScore[] | null;
  isDeckEmpty?: boolean;
}

const VictoryScreen: React.FC<VictoryScreenProps> = ({ score, onPlayAgain, playerScores, isDeckEmpty = false }) => {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/80 backdrop-blur-sm animate-fade-in">
      <div className="flex flex-col items-center justify-center p-8 bg-gradient-to-b from-[#1a2e26] to-[#0a1a15] rounded-3xl border-4 border-[#b8860b] shadow-[0_0_50px_rgba(255,215,0,0.3)] max-w-md w-full relative overflow-hidden">

        {/* Confetti (Simple CSS dots) */}
        <div className="absolute top-0 left-0 w-full h-full pointer-events-none overflow-hidden">
          {/* We can use CSS animations for confetti but for simplicity static decor */}
          <div className="absolute top-10 left-10 w-2 h-2 bg-red-500 rounded-full"></div>
          <div className="absolute top-20 right-20 w-3 h-3 bg-blue-500 transform rotate-45"></div>
          <div className="absolute bottom-32 left-1/2 w-2 h-2 bg-yellow-400 rounded-full"></div>
        </div>

        <h1 className="text-6xl font-black text-transparent bg-clip-text bg-gradient-to-b from-[#ffd700] to-[#b8860b] drop-shadow-sm mb-4 tracking-wide">
          {isDeckEmpty ? 'GAME OVER' : 'VICTORY'}
        </h1>

        {isDeckEmpty && (
          <p className="text-gray-400 text-sm uppercase tracking-widest mb-4">Deck is Empty</p>
        )}

        {/* Show leaderboard if scores are provided */}
        {playerScores && playerScores.length > 0 ? (
          <div className="w-full mb-6">
            <h2 className="text-white text-xl font-bold mb-4 text-center">Final Scores</h2>
            <div className="space-y-2">
              {playerScores.map((player, index) => (
                <div
                  key={player.playerName}
                  className={`flex items-center justify-between p-3 rounded-lg ${player.isWinner
                      ? 'bg-gradient-to-r from-yellow-600/30 to-yellow-700/30 border-2 border-yellow-500'
                      : 'bg-white/5'
                    }`}
                >
                  <div className="flex items-center gap-3">
                    <span className="text-2xl font-bold text-gray-400 w-6">
                      {index + 1}
                    </span>
                    {player.isWinner && <span className="text-2xl">👑</span>}
                    <span className={`font-bold ${player.isWinner ? 'text-yellow-400' : 'text-white'}`}>
                      {player.playerName}
                    </span>
                  </div>
                  <span className={`text-xl font-bold ${player.isWinner ? 'text-yellow-400' : 'text-gray-300'}`}>
                    {player.score} pts
                  </span>
                </div>
              ))}
            </div>
          </div>
        ) : (
          <>
            {/* Trophy Icon */}
            <div className="relative mb-8">
              <div className="absolute inset-0 bg-yellow-500 blur-3xl opacity-20 rounded-full"></div>
              <svg className="w-40 h-40 text-[#ffd700] drop-shadow-lg" viewBox="0 0 24 24" fill="currentColor">
                <path d="M19 5h-2V3H7v2H5c-1.1 0-2 .9-2 2v1c0 2.55 1.92 4.63 4.39 4.94.63 1.5 1.98 2.63 3.61 2.96V19H7v2h10v-2h-4v-3.1c1.63-.33 2.98-1.46 3.61-2.96C19.08 12.63 21 10.55 21 8V7c0-1.1-.9-2-2-2zM7 8V7h2v3.82C8.36 10.4 7.5 9.3 7 8zm12 0c-.5 1.3-1.36 2.4-2 2.82V7h2v1z" />
              </svg>
            </div>

            <div className="flex flex-col items-center mb-8">
              <div className="relative">
                <img src="https://picsum.photos/seed/player/100" alt="Player" className="w-20 h-20 rounded-full border-4 border-[#ffd700] shadow-lg mb-2" />
                <div className="absolute -top-6 left-1/2 -translate-x-1/2 text-4xl">👑</div>
              </div>
              <span className="text-white text-xl font-bold">Player</span>
              <span className="text-yellow-400 text-2xl font-black mt-1">+{score} Coins</span>
              <span className="text-gray-400 text-sm uppercase tracking-widest mt-1">Level Up!</span>
            </div>
          </>
        )}

        <div className="flex gap-4 w-full">
          <button
            onClick={onPlayAgain}
            className="flex-1 bg-gradient-to-b from-green-500 to-green-700 hover:from-green-400 hover:to-green-600 text-white font-bold py-4 rounded-full shadow-[0_4px_0_rgb(21,128,61)] active:shadow-none active:translate-y-1 transition-all uppercase tracking-wide text-lg"
          >
            Play Again
          </button>
          <button className="flex-1 bg-gradient-to-b from-gray-500 to-gray-700 hover:from-gray-400 hover:to-gray-600 text-white font-bold py-4 rounded-full shadow-[0_4px_0_rgb(75,85,99)] active:shadow-none active:translate-y-1 transition-all uppercase tracking-wide text-lg">
            Main Menu
          </button>
        </div>

      </div>
    </div>
  );
};

export default VictoryScreen;
