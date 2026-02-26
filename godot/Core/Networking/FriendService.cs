using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace OkieRummyGodot.Core.Networking;

/// <summary>
/// Manages the friends list: send/accept/decline requests, block/unblock, list friends.
/// </summary>
public class FriendService
{
    private readonly SupabaseClient _supabase;

    public FriendService(SupabaseClient supabase)
    {
        _supabase = supabase;
    }

    /// <summary>
    /// Send a friend request to another player by their user ID.
    /// </summary>
    public async Task<bool> SendFriendRequest(string recipientId)
    {
        var result = await _supabase.Insert("friendships", new {
            requester_id = _supabase.UserId,
            recipient_id = recipientId,
            status = "pending"
        });
        return result != null;
    }

    /// <summary>
    /// Accept a pending friend request.
    /// </summary>
    public async Task<bool> AcceptFriendRequest(string friendshipId)
    {
        return await _supabase.Update("friendships",
            $"id=eq.{friendshipId}&recipient_id=eq.{_supabase.UserId}&status=eq.pending",
            new { status = "accepted", updated_at = DateTime.UtcNow.ToString("o") });
    }

    /// <summary>
    /// Decline (delete) a pending friend request.
    /// </summary>
    public async Task<bool> DeclineFriendRequest(string friendshipId)
    {
        return await _supabase.Delete("friendships",
            $"id=eq.{friendshipId}&recipient_id=eq.{_supabase.UserId}&status=eq.pending");
    }

    /// <summary>
    /// Remove an accepted friend.
    /// </summary>
    public async Task<bool> RemoveFriend(string friendshipId)
    {
        return await _supabase.Delete("friendships", $"id=eq.{friendshipId}");
    }

    /// <summary>
    /// Block a player. Creates or updates the friendship to "blocked".
    /// </summary>
    public async Task<bool> BlockPlayer(string playerId)
    {
        // Check if friendship exists
        var existing = await _supabase.Get("friendships",
            $"or=(and(requester_id.eq.{_supabase.UserId},recipient_id.eq.{playerId}),and(requester_id.eq.{playerId},recipient_id.eq.{_supabase.UserId}))");

        if (existing != null && existing.Value.GetArrayLength() > 0)
        {
            var id = existing.Value[0].GetProperty("id").GetString();
            return await _supabase.Update("friendships", $"id=eq.{id}",
                new { status = "blocked", updated_at = DateTime.UtcNow.ToString("o") });
        }
        else
        {
            var result = await _supabase.Insert("friendships", new {
                requester_id = _supabase.UserId,
                recipient_id = playerId,
                status = "blocked"
            });
            return result != null;
        }
    }

    /// <summary>
    /// Unblock a player (deletes the blocked friendship).
    /// </summary>
    public async Task<bool> UnblockPlayer(string playerId)
    {
        return await _supabase.Delete("friendships",
            $"requester_id=eq.{_supabase.UserId}&recipient_id=eq.{playerId}&status=eq.blocked");
    }

    /// <summary>
    /// Get accepted friends with their profiles and presence.
    /// </summary>
    public async Task<List<FriendInfo>> GetFriends()
    {
        var friends = new List<FriendInfo>();

        // Friends where I'm the requester
        var sent = await _supabase.Get("friendships",
            $"requester_id=eq.{_supabase.UserId}&status=eq.accepted&select=id,recipient_id,player_profiles!friendships_recipient_id_fkey(display_name,avatar_url,level,player_presence(status,last_seen,invite_response,invite_from_id))");

        // Friends where I'm the recipient
        var received = await _supabase.Get("friendships",
            $"recipient_id=eq.{_supabase.UserId}&status=eq.accepted&select=id,requester_id,player_profiles!friendships_requester_id_fkey(display_name,avatar_url,level,player_presence(status,last_seen,invite_response,invite_from_id))");

        ParseFriends(sent, friends, "recipient_id");
        ParseFriends(received, friends, "requester_id");

        return friends;
    }

    /// <summary>
    /// Get pending friend requests received by the current user.
    /// </summary>
    public async Task<List<FriendRequest>> GetPendingRequests()
    {
        var requests = new List<FriendRequest>();
        var result = await _supabase.Get("friendships",
            $"recipient_id=eq.{_supabase.UserId}&status=eq.pending&select=id,requester_id,created_at,player_profiles!friendships_requester_id_fkey(display_name,avatar_url)");

        if (result != null && result.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in result.Value.EnumerateArray())
            {
                requests.Add(new FriendRequest {
                    FriendshipId = item.GetProperty("id").GetString(),
                    RequesterId = item.GetProperty("requester_id").GetString(),
                    RequesterName = GetNestedString(item, "player_profiles", "display_name") ?? "Unknown",
                    RequesterAvatar = GetNestedString(item, "player_profiles", "avatar_url") ?? "",
                    CreatedAt = item.GetProperty("created_at").GetString()
                });
            }
        }
        return requests;
    }

    /// <summary>
    /// Search for a player by display name (for adding friends).
    /// </summary>
    public async Task<List<PlayerSearchResult>> SearchPlayers(string nameQuery)
    {
        var results = new List<PlayerSearchResult>();
        var data = await _supabase.Get("player_profiles",
            $"display_name=ilike.*{nameQuery}*&select=id,display_name,avatar_url,level&limit=10");

        if (data != null && data.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.Value.EnumerateArray())
            {
                var id = item.GetProperty("id").GetString();
                if (id == _supabase.UserId) continue;
                results.Add(new PlayerSearchResult {
                    PlayerId = id,
                    DisplayName = item.GetProperty("display_name").GetString(),
                    AvatarUrl = item.GetProperty("avatar_url").GetString(),
                    Level = item.GetProperty("level").GetInt32()
                });
            }
        }
        return results;
    }

    private void ParseFriends(JsonElement? data, List<FriendInfo> list, string idField)
    {
        if (data == null || data.Value.ValueKind != JsonValueKind.Array) return;
        foreach (var item in data.Value.EnumerateArray())
        {
            var profile = item.GetProperty("player_profiles");
            list.Add(new FriendInfo {
                FriendshipId = item.GetProperty("id").GetString(),
                PlayerId = item.GetProperty(idField).GetString(),
                DisplayName = profile.GetProperty("display_name").GetString() ?? "Unknown",
                AvatarUrl = profile.GetProperty("avatar_url").GetString() ?? "",
                Level = profile.GetProperty("level").GetInt32(),
                OnlineStatus = GetDoubleNestedString(profile, "player_presence", "status") ?? "offline",
                LastSeen = GetDoubleNestedString(profile, "player_presence", "last_seen"),
                InviteResponse = GetDoubleNestedString(profile, "player_presence", "invite_response"),
                InviteFromId = GetDoubleNestedString(profile, "player_presence", "invite_from_id")
            });
        }
    }

    private string GetDoubleNestedString(JsonElement parent, string obj, string prop)
    {
        if (parent.TryGetProperty(obj, out var nested) && nested.ValueKind == JsonValueKind.Object
            && nested.TryGetProperty(prop, out var val))
            return val.GetString();
        return null;
    }

    private string GetNestedString(JsonElement parent, string obj, string prop)
    {
        if (parent.TryGetProperty(obj, out var nested) && nested.ValueKind == JsonValueKind.Object
            && nested.TryGetProperty(prop, out var val))
            return val.GetString();
        return null;
    }

    private int GetNestedInt(JsonElement parent, string obj, string prop)
    {
        if (parent.TryGetProperty(obj, out var nested) && nested.ValueKind == JsonValueKind.Object
            && nested.TryGetProperty(prop, out var val))
            return val.GetInt32();
        return 0;
    }
}

// DTOs
public class FriendInfo
{
    public string FriendshipId { get; set; }
    public string PlayerId { get; set; }
    public string DisplayName { get; set; }
    public string AvatarUrl { get; set; }
    public int Level { get; set; }
    public string OnlineStatus { get; set; }
    public string LastSeen { get; set; }
    public string InviteResponse { get; set; }
    public string InviteFromId { get; set; }
}

public class FriendRequest
{
    public string FriendshipId { get; set; }
    public string RequesterId { get; set; }
    public string RequesterName { get; set; }
    public string RequesterAvatar { get; set; }
    public string CreatedAt { get; set; }
}

public class PlayerSearchResult
{
    public string PlayerId { get; set; }
    public string DisplayName { get; set; }
    public string AvatarUrl { get; set; }
    public int Level { get; set; }
}
