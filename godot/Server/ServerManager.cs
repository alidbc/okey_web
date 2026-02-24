using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using OkieRummyGodot.Core.Domain;
using OkieRummyGodot.Core.Application;

namespace OkieRummyGodot.Server
{
    public partial class ServerManager : Node
    {
        private ENetMultiplayerPeer _peer = new ENetMultiplayerPeer();
        private Dictionary<string, MatchManager> _activeRooms = new Dictionary<string, MatchManager>();

        [Export] public int Port = 8080;
        [Export] public int MaxClients = 32;

        public override void _Ready()
        {
            bool noServer = false;
            foreach (var arg in OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()))
            {
                if (arg == "--no-server") noServer = true;
                if (arg == "--test-marathon-host") noServer = true;
                if (arg == "--test-marathon-join") noServer = true;
            }

            if (!noServer && (DisplayServer.GetName() == "headless" || OS.HasFeature("server")))
            {
                StartServer();
            }
            else
            {
                GD.Print("ServerManager: Waiting (Manual start required or --no-server passed).");
            }
        }

        public bool StartServer()
        {
            if (Multiplayer.MultiplayerPeer != null && !(Multiplayer.MultiplayerPeer is OfflineMultiplayerPeer))
            {
                if (Multiplayer.MultiplayerPeer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Disconnected)
                {
                    GD.Print($"ServerManager: Server already active with peer: {Multiplayer.MultiplayerPeer.GetType().Name}");
                    return true;
                }
            }

            var error = _peer.CreateServer(Port, MaxClients);
            if (error != Error.Ok)
            {
                GD.PrintErr($"ServerManager: Error creating server: {error}");
                return false;
            }

            Multiplayer.MultiplayerPeer = _peer;
            GD.Print($"ServerManager: Server started on port {Port}");
            Multiplayer.PeerConnected += OnPeerConnected;
            Multiplayer.PeerDisconnected += OnPeerDisconnected;

            return true;
        }

        private void OnPeerConnected(long id)
        {
            GD.Print($"ServerManager: Peer connected: {id}");
        }

        private void OnPeerDisconnected(long id)
        {
            GD.Print($"ServerManager: Peer disconnected: {id}");
        }
    }
}
