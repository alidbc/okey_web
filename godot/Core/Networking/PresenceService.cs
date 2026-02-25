using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace OkieRummyGodot.Core.Networking;

/// <summary>
/// Manages player online presence via heartbeat to Supabase.
/// Call Start() after sign-in and Stop() on sign-out or quit.
/// </summary>
public class PresenceService
{
    private readonly SupabaseClient _supabase;
    private CancellationTokenSource _cts;
    private string _currentStatus = "online";
    private string _currentRoomCode;

    public string CurrentStatus => _currentStatus;

    public PresenceService(SupabaseClient supabase)
    {
        _supabase = supabase;
    }

    /// <summary>
    /// Start sending heartbeats every 30 seconds.
    /// </summary>
    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        _ = HeartbeatLoop(_cts.Token);
    }

    /// <summary>
    /// Stop heartbeats and mark as offline.
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        _ = SetOffline();
    }

    /// <summary>
    /// Update status (call when joining/leaving a game).
    /// </summary>
    public void SetStatus(string status, string roomCode = null)
    {
        _currentStatus = status;
        _currentRoomCode = roomCode;
        _ = SendHeartbeat();
    }

    /// <summary>
    /// Get a friend's privacy settings to check if we can see their status.
    /// </summary>
    public async Task<PrivacySettingsData> GetPrivacySettings()
    {
        if (!_supabase.IsAuthenticated) return new PrivacySettingsData();

        var result = await _supabase.Get("privacy_settings", $"player_id=eq.{_supabase.UserId}&select=*");
        if (result != null && result.Value.ValueKind == JsonValueKind.Array && result.Value.GetArrayLength() > 0)
        {
            var s = result.Value[0];
            return new PrivacySettingsData {
                OnlineStatusVisibility = GetStr(s, "online_status_visibility", "friends"),
                AllowFriendRequests = GetStr(s, "allow_friend_requests", "everyone"),
                ShowLastSeen = GetBool(s, "show_last_seen", true),
                ProfileVisibility = GetStr(s, "profile_visibility", "public"),
                ShowInLeaderboards = GetBool(s, "show_in_leaderboards", true)
            };
        }
        return new PrivacySettingsData();
    }

    /// <summary>
    /// Update privacy settings.
    /// </summary>
    public async Task<bool> UpdatePrivacySettings(PrivacySettingsData settings)
    {
        return await _supabase.Update("privacy_settings", $"player_id=eq.{_supabase.UserId}", new {
            online_status_visibility = settings.OnlineStatusVisibility,
            allow_friend_requests = settings.AllowFriendRequests,
            show_last_seen = settings.ShowLastSeen,
            profile_visibility = settings.ProfileVisibility,
            show_in_leaderboards = settings.ShowInLeaderboards,
            updated_at = DateTime.UtcNow.ToString("o")
        });
    }

    private async Task HeartbeatLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await SendHeartbeat();
            try { await Task.Delay(30_000, ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task SendHeartbeat()
    {
        if (!_supabase.IsAuthenticated) return;
        await _supabase.Rpc("heartbeat", new {
            p_status = _currentStatus,
            p_room_code = _currentRoomCode
        });
    }

    private async Task SetOffline()
    {
        if (!_supabase.IsAuthenticated) return;
        await _supabase.Rpc("heartbeat", new { p_status = "offline" });
    }

    private static string GetStr(JsonElement el, string prop, string def)
    {
        return el.TryGetProperty(prop, out var v) ? v.GetString() ?? def : def;
    }

    private static bool GetBool(JsonElement el, string prop, bool def)
    {
        return el.TryGetProperty(prop, out var v) ? v.GetBoolean() : def;
    }
}

public class PrivacySettingsData
{
    public string OnlineStatusVisibility { get; set; } = "friends";
    public string AllowFriendRequests { get; set; } = "everyone";
    public bool ShowLastSeen { get; set; } = true;
    public string ProfileVisibility { get; set; } = "public";
    public bool ShowInLeaderboards { get; set; } = true;
}
