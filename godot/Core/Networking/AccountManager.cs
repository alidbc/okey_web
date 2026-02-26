using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace OkieRummyGodot.Core.Networking;

/// <summary>
/// Manages the player's account lifecycle: sign-in, profile, settings.
/// Autonode — add as a child of the root or autoload.
/// </summary>
public partial class AccountManager : Node
{
    [Signal] public delegate void SignedInEventHandler(string userId, string displayName);
    [Signal] public delegate void SignedOutEventHandler();
    [Signal] public delegate void ProfileLoadedEventHandler(string displayName, string avatarUrl, int level, int gamesPlayed, int gamesWon);
    [Signal] public delegate void RejoinGameAvailableEventHandler(string roomCode);

    public SupabaseClient Supabase { get; private set; }
    public RealtimeClient Realtime { get; private set; }
    public string DisplayName { get; private set; }
    public string AvatarUrl { get; private set; }
    public string AccountType { get; private set; }
    public List<string> EnabledProviders { get; private set; } = new List<string>();

    // Loaded from res://supabase_config.json at runtime
    private string _supabaseUrl;
    private string _supabaseAnonKey;

    public override void _Ready()
    {
        LoadConfig();
        Supabase = new SupabaseClient(_supabaseUrl, _supabaseAnonKey);

        // --- Handle Multi-Profile Isolation ---
        var args = OS.GetCmdlineArgs();
        foreach (var arg in args)
        {
            if (arg.StartsWith("--profile="))
            {
                string profileName = arg.Replace("--profile=", "");
                Supabase.SetSessionProfile(profileName);
                break;
            }
        }
        
        Realtime = new RealtimeClient();
        Realtime.Name = "RealtimeClient";
        AddChild(Realtime);

        Supabase.OnAuthStateChanged += (state) => {
            GD.Print($"AccountManager: Internal Supabase Auth state -> {state}");
            OnAuthStateChanged(state);
        };
        
        // Load persistent session if it exists
        if (Supabase.TryLoadSession())
        {
            GD.Print("AccountManager: Found existing session on disk. Ready for auto-login.");
            DisplayName = Supabase.UserMetadataName;
            AvatarUrl = Supabase.UserMetadataAvatar;
        }
    }

    private void LoadConfig()
    {
        _supabaseUrl = "http://localhost:8000";
        _supabaseAnonKey = "";

        var path = "res://supabase_config.json";
        if (FileAccess.FileExists(path))
        {
            using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            var json = file.GetAsText();
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("supabase_url", out var url))
                _supabaseUrl = url.GetString();
            if (root.TryGetProperty("anon_key", out var key))
                _supabaseAnonKey = key.GetString();
            if (root.TryGetProperty("enabled_providers", out var providers))
            {
                EnabledProviders.Clear();
                foreach (var p in providers.EnumerateArray())
                    EnabledProviders.Add(p.GetString());
            }
            GD.Print($"AccountManager: Config loaded — {_supabaseUrl}, providers: {string.Join(",", EnabledProviders)}");
        }
        else
        {
            GD.PrintErr("AccountManager: supabase_config.json not found, using defaults");
        }
    }

    /// <summary>
    /// Auto sign-in as guest. Call from LobbyUI._Ready() or a LoginScreen.
    /// </summary>
    public async Task<bool> SignInAsGuest()
    {
        GD.Print("AccountManager: Signing in as guest...");
        return await Supabase.SignInAsGuest();
    }

    /// <summary>
    /// Get the OAuth URL for a provider. Open in browser for the user.
    /// </summary>
    public string GetOAuthUrl(string provider) => Supabase.GetOAuthUrl(provider);

    /// <summary>
    /// Load the current user's profile from Supabase.
    /// </summary>
    public async Task LoadProfile()
    {
        if (Supabase == null || !Supabase.IsAuthenticated) return;

        // Fallback: emit signal with current metadata first so UI updates instantly
        EmitProfileLoadedSignal(1, 0, 0);

        var result = await Supabase.Get("player_profiles", $"id=eq.{Supabase.UserId}&select=*");
        if (result != null && result.Value.ValueKind == JsonValueKind.Array)
        {
            var arr = result.Value;
            if (arr.GetArrayLength() > 0)
            {
                var profile = arr[0];
                string dbName = profile.TryGetProperty("display_name", out var dn) ? dn.GetString() : null;
                string dbAvatar = profile.TryGetProperty("avatar_url", out var au) ? au.GetString() : null;

                if (!string.IsNullOrEmpty(dbName)) DisplayName = dbName;
                if (!string.IsNullOrEmpty(dbAvatar)) AvatarUrl = dbAvatar;

                AccountType = profile.TryGetProperty("account_type", out var at) ? at.GetString() : "standard";
                int level = profile.TryGetProperty("level", out var l) ? l.GetInt32() : 1;
                int gamesPlayed = profile.TryGetProperty("games_played", out var gp) ? gp.GetInt32() : 0;
                int gamesWon = profile.TryGetProperty("games_won", out var gw) ? gw.GetInt32() : 0;

                GD.Print($"AccountManager: Profile loaded from DB - {DisplayName} (Lv.{level})");
                EmitProfileLoadedSignal(level, gamesPlayed, gamesWon);
            }
        }
    }

    /// <summary>
    /// Check if the player has an active game session to rejoin.
    /// </summary>
    public async Task CheckActiveGame()
    {
        if (Supabase == null || !Supabase.IsAuthenticated) return;

        var result = await Supabase.Get("player_presence", $"player_id=eq.{Supabase.UserId}&select=current_room_code");
        if (result != null && result.Value.ValueKind == JsonValueKind.Array && result.Value.GetArrayLength() > 0)
        {
            var presence = result.Value[0];
            if (presence.TryGetProperty("current_room_code", out var code) && code.ValueKind != JsonValueKind.Null)
            {
                string roomCode = code.GetString();
                if (!string.IsNullOrEmpty(roomCode))
                {
                    GD.Print($"AccountManager: Found active game session to rejoin: {roomCode}");
                    Callable.From(() => EmitSignal(SignalName.RejoinGameAvailable, roomCode)).CallDeferred();
                }
            }
        }
    }

    private void EmitProfileLoadedSignal(int level, int gamesPlayed, int gamesWon)
    {
        string name = DisplayName ?? "";
        string avatar = AvatarUrl ?? "";
        Callable.From(() => EmitSignal(SignalName.ProfileLoaded, name, avatar, level, gamesPlayed, gamesWon)).CallDeferred();
    }

    /// <summary>
    /// Update the player's display name.
    /// </summary>
    public async Task<bool> UpdateDisplayName(string newName)
    {
        if (!Supabase.IsAuthenticated) return false;
        bool ok = await Supabase.Update("player_profiles", $"id=eq.{Supabase.UserId}", new { display_name = newName });
        if (ok) DisplayName = newName;
        return ok;
    }

    /// <summary>
    /// Record a game result (increment stats).
    /// </summary>
    public async Task RecordGameResult(bool won)
    {
        if (!Supabase.IsAuthenticated) return;
        // Use RPC to atomically increment
        await Supabase.Rpc("heartbeat", new { p_status = "online" }); // Also update presence
        // Simple update — in production, use an RPC for atomic increment
        var profile = await Supabase.Get("player_profiles", $"id=eq.{Supabase.UserId}&select=games_played,games_won");
        if (profile != null && profile.Value.GetArrayLength() > 0)
        {
            var p = profile.Value[0];
            int played = p.GetProperty("games_played").GetInt32() + 1;
            int wins = p.GetProperty("games_won").GetInt32() + (won ? 1 : 0);
            await Supabase.Update("player_profiles", $"id=eq.{Supabase.UserId}", new { games_played = played, games_won = wins });
        }
    }

    /// <summary>
    /// Validate a display name client-side before sending to server.
    /// Server enforces the same rules via SQL trigger.
    /// </summary>
    public static (bool valid, string error) ValidateDisplayName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return (false, "Name cannot be empty");
        if (name.Length < 2 || name.Length > 24)
            return (false, "Name must be 2-24 characters");
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_\- ]+$"))
            return (false, "Only letters, numbers, spaces, hyphens, and underscores allowed");

        string lower = name.ToLower();
        string[] blocked = { "fuck", "shit", "bitch", "dick", "cock", "cunt", "nigger", "nigga",
            "faggot", "retard", "whore", "slut", "nazi", "hitler", "rape", "terrorist" };
        foreach (var word in blocked)
            if (lower.Contains(word))
                return (false, "Name contains inappropriate language");

        return (true, null);
    }

    /// <summary>
    /// Delete the user's account and all associated data (GDPR compliance).
    /// </summary>
    public async Task<bool> DeleteAccount()
    {
        if (!Supabase.IsAuthenticated) return false;

        GD.Print("AccountManager: Deleting account...");
        var result = await Supabase.Rpc("delete_my_account");
        SignOut();
        GD.Print("AccountManager: Account deleted");
        return true;
    }

    public void SignOut()
    {
        GD.Print("AccountManager: SignOut requested");
        Supabase.SignOut();
        DisplayName = null;
        AvatarUrl = null;
        GetTree().ChangeSceneToFile("res://UI/Scenes/Login.tscn");
    }

    private void OnAuthStateChanged(string state)
    {
        if (state == "signed_in" || state == "token_refreshed")
        {
            GD.Print($"AccountManager: Auth state -> {state} ({Supabase.UserId})");
            
            // Set initial defaults from metadata if available
            GD.Print($"AccountManager: Capturing metadata... Name: {Supabase.UserMetadataName}, Avatar: {Supabase.UserMetadataAvatar}");
            if (string.IsNullOrEmpty(DisplayName))
                DisplayName = Supabase.UserMetadataName;
            if (string.IsNullOrEmpty(AvatarUrl))
                AvatarUrl = Supabase.UserMetadataAvatar;

            GD.Print($"AccountManager: Result -> DisplayName: {DisplayName}, AvatarUrl: {AvatarUrl}");

            _ = LoadProfile();
            _ = CheckActiveGame();
            
            // Connect Realtime safely on the Godot main thread
            Callable.From(() =>
            {
                Realtime.Initialize(_supabaseUrl, _supabaseAnonKey, Supabase.AccessToken);
                Realtime.Connect();
                
                Realtime.JoinChannel("realtime:public:friendships", new {
                    config = new {
                        postgres_changes = new[] {
                            new { @event = "*", schema = "public", table = "friendships" }
                        }
                    }
                });
                
                Realtime.JoinChannel("realtime:public:player_presence", new {
                    config = new {
                        postgres_changes = new[] {
                            new { @event = "*", schema = "public", table = "player_presence" }
                        }
                    }
                });
            }).CallDeferred();

            // Fix signal emission for Godot 4 C# (Thread safe)
            string uid = Supabase.UserId;
            string dname = DisplayName ?? "";
            Callable.From(() => EmitSignal(SignalName.SignedIn, uid, dname)).CallDeferred();
        }
        else if (state == "signed_out")
        {
            GD.Print("AccountManager: Auth state -> signed_out");
            Realtime.Disconnect();
            DisplayName = null;
            AvatarUrl = null;
            Callable.From(() => EmitSignal(SignalName.SignedOut)).CallDeferred();
        }
    }
}
