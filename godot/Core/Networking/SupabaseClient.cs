using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace OkieRummyGodot.Core.Networking;

/// <summary>
/// Lightweight Supabase client for Godot.
/// Handles Auth (JWT), REST API calls, and session persistence.
/// </summary>
public class SupabaseClient
{
    private readonly string _baseUrl;
    private readonly string _anonKey;
    private readonly System.Net.Http.HttpClient _http;
    private string _accessToken;
    private string _refreshToken;
    private string _userId;

    private const string TOKEN_SAVE_PATH = "user://supabase_session.json";

    public string UserId => _userId;
    public string AccessToken => _accessToken;
    public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken);

    public event Action<string> OnAuthStateChanged; // "signed_in", "signed_out", "token_refreshed"

    public SupabaseClient(string baseUrl, string anonKey)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _anonKey = anonKey;
        _http = new System.Net.Http.HttpClient();
        _http.DefaultRequestHeaders.Add("apikey", _anonKey);
    }

    // ================================================================
    // AUTH
    // ================================================================

    /// <summary>
    /// Sign in as a guest using the device's unique ID.
    /// Uses Supabase's anonymous sign-in with device_id metadata.
    /// </summary>
    public async Task<bool> SignInAsGuest()
    {
        string deviceId = OS.GetUniqueId();
        if (string.IsNullOrEmpty(deviceId))
            deviceId = "dev_" + Guid.NewGuid().ToString("N")[..12];

        // First try to restore an existing session
        if (TryLoadSession())
        {
            GD.Print("SupabaseClient: Restored session from disk");
            OnAuthStateChanged?.Invoke("signed_in");
            return true;
        }

        // Sign up anonymously with device_id in metadata
        var body = new {
            email = $"{deviceId}@guest.okey.local",
            password = deviceId,
            data = new { device_id = deviceId, display_name = $"Player_{deviceId[..6]}" }
        };

        var response = await PostAuth("/auth/v1/signup", body);
        if (response == null)
        {
            // Account may already exist, try sign-in
            var signInBody = new {
                email = $"{deviceId}@guest.okey.local",
                password = deviceId
            };
            response = await PostAuth("/auth/v1/token?grant_type=password", signInBody);
        }

        if (response != null)
        {
            ParseAuthResponse(response);
            SaveSession();
            GD.Print($"SupabaseClient: Signed in as guest. UserId: {_userId}");
            OnAuthStateChanged?.Invoke("signed_in");
            return true;
        }

        GD.PrintErr("SupabaseClient: Guest sign-in failed");
        return false;
    }

    /// <summary>
    /// Sign in with IDP (Google, Facebook, Discord, Twitch).
    /// Returns the OAuth URL to open in a browser.
    /// </summary>
    public string GetOAuthUrl(string provider)
    {
        return $"{_baseUrl}/auth/v1/authorize?provider={provider}&redirect_to={_baseUrl}";
    }

    /// <summary>
    /// Exchange an OAuth code/token for a session.
    /// Called after the user completes the OAuth flow.
    /// </summary>
    public async Task<bool> ExchangeOAuthCode(string code)
    {
        var body = new { auth_code = code };
        var response = await PostAuth("/auth/v1/token?grant_type=authorization_code", body);
        if (response != null)
        {
            ParseAuthResponse(response);
            SaveSession();
            OnAuthStateChanged?.Invoke("signed_in");
            return true;
        }
        return false;
    }

    /// <summary>
    /// Refresh the session token.
    /// </summary>
    public async Task<bool> RefreshSession()
    {
        if (string.IsNullOrEmpty(_refreshToken)) return false;

        var body = new { refresh_token = _refreshToken };
        var response = await PostAuth("/auth/v1/token?grant_type=refresh_token", body);
        if (response != null)
        {
            ParseAuthResponse(response);
            SaveSession();
            OnAuthStateChanged?.Invoke("token_refreshed");
            return true;
        }
        return false;
    }

    /// <summary>
    /// Sign out and clear session.
    /// </summary>
    public void SignOut()
    {
        _accessToken = null;
        _refreshToken = null;
        _userId = null;
        ClearSession();
        OnAuthStateChanged?.Invoke("signed_out");
    }

    // ================================================================
    // REST API (PostgREST)
    // ================================================================

    /// <summary>
    /// GET from a table. Returns JSON response.
    /// Example: await Get("player_profiles", "id=eq." + userId);
    /// </summary>
    public async Task<JsonElement?> Get(string table, string query = "")
    {
        string url = $"{_baseUrl}/rest/v1/{table}?{query}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        SetAuthHeaders(request);
        
        try
        {
            var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
                return JsonSerializer.Deserialize<JsonElement>(json);
            GD.PrintErr($"SupabaseClient GET {table}: {response.StatusCode} - {json}");
        }
        catch (Exception ex) { GD.PrintErr($"SupabaseClient GET error: {ex.Message}"); }
        return null;
    }

    /// <summary>
    /// POST (insert) into a table.
    /// </summary>
    public async Task<JsonElement?> Insert(string table, object data)
    {
        string url = $"{_baseUrl}/rest/v1/{table}";
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        SetAuthHeaders(request);
        request.Headers.Add("Prefer", "return=representation");
        request.Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");

        try
        {
            var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
                return JsonSerializer.Deserialize<JsonElement>(json);
            GD.PrintErr($"SupabaseClient INSERT {table}: {response.StatusCode} - {json}");
        }
        catch (Exception ex) { GD.PrintErr($"SupabaseClient INSERT error: {ex.Message}"); }
        return null;
    }

    /// <summary>
    /// PATCH (update) a table row.
    /// Example: await Update("player_profiles", "id=eq." + userId, new { display_name = "NewName" });
    /// </summary>
    public async Task<bool> Update(string table, string query, object data)
    {
        string url = $"{_baseUrl}/rest/v1/{table}?{query}";
        var request = new HttpRequestMessage(HttpMethod.Patch, url);
        SetAuthHeaders(request);
        request.Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");

        try
        {
            var response = await _http.SendAsync(request);
            if (response.IsSuccessStatusCode) return true;
            var json = await response.Content.ReadAsStringAsync();
            GD.PrintErr($"SupabaseClient UPDATE {table}: {response.StatusCode} - {json}");
        }
        catch (Exception ex) { GD.PrintErr($"SupabaseClient UPDATE error: {ex.Message}"); }
        return false;
    }

    /// <summary>
    /// DELETE from a table.
    /// </summary>
    public async Task<bool> Delete(string table, string query)
    {
        string url = $"{_baseUrl}/rest/v1/{table}?{query}";
        var request = new HttpRequestMessage(HttpMethod.Delete, url);
        SetAuthHeaders(request);

        try
        {
            var response = await _http.SendAsync(request);
            if (response.IsSuccessStatusCode) return true;
            var json = await response.Content.ReadAsStringAsync();
            GD.PrintErr($"SupabaseClient DELETE {table}: {response.StatusCode} - {json}");
        }
        catch (Exception ex) { GD.PrintErr($"SupabaseClient DELETE error: {ex.Message}"); }
        return false;
    }

    /// <summary>
    /// Call a Postgres RPC function.
    /// Example: await Rpc("heartbeat", new { p_status = "online" });
    /// </summary>
    public async Task<JsonElement?> Rpc(string functionName, object parameters = null)
    {
        string url = $"{_baseUrl}/rest/v1/rpc/{functionName}";
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        SetAuthHeaders(request);
        request.Content = new StringContent(
            JsonSerializer.Serialize(parameters ?? new {}),
            Encoding.UTF8, "application/json"
        );

        try
        {
            var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                if (string.IsNullOrWhiteSpace(json)) return null;
                return JsonSerializer.Deserialize<JsonElement>(json);
            }
            GD.PrintErr($"SupabaseClient RPC {functionName}: {response.StatusCode} - {json}");
        }
        catch (Exception ex) { GD.PrintErr($"SupabaseClient RPC error: {ex.Message}"); }
        return null;
    }

    // ================================================================
    // HELPERS
    // ================================================================

    private void SetAuthHeaders(HttpRequestMessage request)
    {
        request.Headers.Add("apikey", _anonKey);
        if (!string.IsNullOrEmpty(_accessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
    }

    private async Task<JsonElement?> PostAuth(string path, object body)
    {
        try
        {
            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{_baseUrl}{path}", content);
            var json = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
                return JsonSerializer.Deserialize<JsonElement>(json);
            GD.Print($"SupabaseClient Auth {path}: {response.StatusCode}");
        }
        catch (Exception ex) { GD.PrintErr($"SupabaseClient Auth error: {ex.Message}"); }
        return null;
    }

    private void ParseAuthResponse(JsonElement? response)
    {
        if (response == null) return;
        var r = response.Value;
        _accessToken = r.TryGetProperty("access_token", out var at) ? at.GetString() : null;
        _refreshToken = r.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        if (r.TryGetProperty("user", out var user) && user.TryGetProperty("id", out var uid))
            _userId = uid.GetString();
    }

    private void SaveSession()
    {
        try
        {
            var session = JsonSerializer.Serialize(new {
                access_token = _accessToken,
                refresh_token = _refreshToken,
                user_id = _userId
            });
            using var file = FileAccess.Open(TOKEN_SAVE_PATH, FileAccess.ModeFlags.Write);
            file?.StoreString(session);
        }
        catch (Exception ex) { GD.PrintErr($"SupabaseClient: Failed to save session: {ex.Message}"); }
    }

    private bool TryLoadSession()
    {
        try
        {
            if (!FileAccess.FileExists(TOKEN_SAVE_PATH)) return false;
            using var file = FileAccess.Open(TOKEN_SAVE_PATH, FileAccess.ModeFlags.Read);
            var json = file?.GetAsText();
            if (string.IsNullOrEmpty(json)) return false;

            var session = JsonSerializer.Deserialize<JsonElement>(json);
            _accessToken = session.TryGetProperty("access_token", out var at) ? at.GetString() : null;
            _refreshToken = session.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
            _userId = session.TryGetProperty("user_id", out var uid) ? uid.GetString() : null;
            return !string.IsNullOrEmpty(_accessToken);
        }
        catch { return false; }
    }

    private void ClearSession()
    {
        try
        {
            if (FileAccess.FileExists(TOKEN_SAVE_PATH))
                DirAccess.Open("user://").Remove("supabase_session.json");
        }
        catch { }
    }
}
