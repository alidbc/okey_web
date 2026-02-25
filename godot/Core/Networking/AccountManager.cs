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

    public SupabaseClient Supabase { get; private set; }
    public string DisplayName { get; private set; }
    public string AvatarUrl { get; private set; }
    public string AccountType { get; private set; }

    // Configure these before calling Initialize()
    private const string SUPABASE_URL = "http://localhost:8000";
    private const string SUPABASE_ANON_KEY = "your-anon-key-here";

    public override void _Ready()
    {
        Supabase = new SupabaseClient(SUPABASE_URL, SUPABASE_ANON_KEY);
        Supabase.OnAuthStateChanged += OnAuthStateChanged;
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
        if (!Supabase.IsAuthenticated) return;

        var result = await Supabase.Get("player_profiles", $"id=eq.{Supabase.UserId}&select=*");
        if (result != null && result.Value.ValueKind == JsonValueKind.Array)
        {
            var arr = result.Value;
            if (arr.GetArrayLength() > 0)
            {
                var profile = arr[0];
                DisplayName = profile.GetProperty("display_name").GetString();
                AvatarUrl = profile.GetProperty("avatar_url").GetString();
                AccountType = profile.GetProperty("account_type").GetString();
                int level = profile.GetProperty("level").GetInt32();
                int gamesPlayed = profile.GetProperty("games_played").GetInt32();
                int gamesWon = profile.GetProperty("games_won").GetInt32();

                GD.Print($"AccountManager: Profile loaded - {DisplayName} (Lv.{level})");
                EmitSignal(nameof(ProfileLoaded), DisplayName, AvatarUrl, level, gamesPlayed, gamesWon);
            }
        }
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
        Supabase.SignOut();
        DisplayName = null;
        AvatarUrl = null;
    }

    private void OnAuthStateChanged(string state)
    {
        switch (state)
        {
            case "signed_in":
                GD.Print($"AccountManager: Auth state -> signed_in ({Supabase.UserId})");
                _ = LoadProfile();
                EmitSignal(nameof(SignedIn), Supabase.UserId, DisplayName ?? "");
                break;
            case "signed_out":
                GD.Print("AccountManager: Auth state -> signed_out");
                EmitSignal(nameof(SignedOut));
                break;
        }
    }
}
