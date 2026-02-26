using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace OkieRummyGodot.Core.Networking;

/// <summary>
/// Handles WebSocket connection to Supabase Realtime (Phoenix Channels).
/// </summary>
public partial class RealtimeClient : Node
{
    private WebSocketPeer _socket;
    private string _url;
    private string _apiKey;
    private string _authToken;
    private int _ref = 1;
    private double _heartbeatTimer = 0;
    private bool _isConnected = false;
    private Dictionary<string, object> _pendingChannels = new Dictionary<string, object>();

    [Signal] public delegate void ConnectedEventHandler();
    [Signal] public delegate void DisconnectedEventHandler();
    [Signal] public delegate void MessageReceivedEventHandler(string topic, string @event, string payloadJson);

    public bool IsConnected => _isConnected;

    public override void _Ready()
    {
        _socket = new WebSocketPeer();
    }

    public void Initialize(string baseUrl, string apiKey, string authToken)
    {
        // Convert http/https to ws/wss
        string wsUrl = baseUrl.Replace("http://", "ws://").Replace("https://", "wss://");
        _url = $"{wsUrl}/realtime/v1/websocket?apikey={authToken}&vsn=1.0.0";
        _apiKey = apiKey;
        _authToken = authToken;
    }

    public void Connect()
    {
        if (string.IsNullOrEmpty(_url)) return;
        Callable.From(() => {
            GD.Print($"RealtimeClient: Connecting to {_url}...");
            var err = _socket.ConnectToUrl(_url);
            if (err != Error.Ok)
            {
                GD.PrintErr($"RealtimeClient: Connection failed - {err}");
            }
        }).CallDeferred();
    }

    public void Disconnect()
    {
        Callable.From(() => {
            _socket.Close();
            _isConnected = false;
        }).CallDeferred();
    }

    public void JoinChannel(string topic, object configPayload = null)
    {
        object payload = configPayload ?? new { };
        if (!_isConnected)
        {
            if (!_pendingChannels.ContainsKey(topic))
            {
                GD.Print($"RealtimeClient: Queueing join for {topic} (not connected yet)");
                _pendingChannels.Add(topic, payload);
            }
            return;
        }

        GD.Print($"RealtimeClient: Joining channel {topic}");
        SendMessage(topic, "phx_join", payload);
    }

    public void LeaveChannel(string topic)
    {
        SendMessage(topic, "phx_leave", new { });
    }

    public override void _Process(double delta)
    {
        _socket.Poll();
        var state = _socket.GetReadyState();

        if (state == WebSocketPeer.State.Open)
        {
            if (!_isConnected)
            {
                _isConnected = true;
                GD.Print("RealtimeClient: Connection opened.");
                EmitSignal(SignalName.Connected);

                // Flush pending channels
                foreach (var kvp in _pendingChannels)
                {
                    GD.Print($"RealtimeClient: Flushing queued join for {kvp.Key}");
                    SendMessage(kvp.Key, "phx_join", kvp.Value);
                }
                _pendingChannels.Clear();
            }

            // Handle Heartbeat
            _heartbeatTimer += delta;
            if (_heartbeatTimer >= 25.0) // 25s heartbeat
            {
                _heartbeatTimer = 0;
                SendMessage("phoenix", "heartbeat", new { });
            }

            // Read packets
            while (_socket.GetAvailablePacketCount() > 0)
            {
                var packet = _socket.GetPacket();
                var json = packet.GetStringFromUtf8();
                ParseMessage(json);
            }
        }
        else if (state == WebSocketPeer.State.Closed && _isConnected)
        {
            _isConnected = false;
            GD.Print("RealtimeClient: Connection closed.");
            EmitSignal(SignalName.Disconnected);
        }
    }

    private void SendMessage(string topic, string @event, object payload)
    {
        var msg = new {
            topic = topic,
            @event = @event,
            payload = payload,
            @ref = (_ref++).ToString()
        };
        
        string json = JsonSerializer.Serialize(msg);
        Callable.From(() => {
            _socket.SendText(json);
        }).CallDeferred();
    }

    private void ParseMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            string topic = root.GetProperty("topic").GetString();
            string @event = root.GetProperty("event").GetString();
            string payload = root.GetProperty("payload").GetRawText();

            // GD.Print($"RealtimeClient: [{topic}] {@event}");
            EmitSignal(SignalName.MessageReceived, topic, @event, payload);
        }
        catch (Exception e)
        {
            GD.PrintErr($"RealtimeClient: Parse error - {e.Message}");
        }
    }
}
