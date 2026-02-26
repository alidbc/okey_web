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

    private string _sessionSavePath = "user://supabase_session.json";

    public string UserId => _userId;
    public string AccessToken => _accessToken;
    public string UserMetadataName { get; private set; }
    public string UserMetadataAvatar { get; private set; }
    public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken);

    public event Action<string> OnAuthStateChanged; // "signed_in", "signed_out", "token_refreshed"

    public SupabaseClient(string baseUrl, string anonKey)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _anonKey = anonKey;
        _http = new System.Net.Http.HttpClient();
        _http.DefaultRequestHeaders.Add("apikey", _anonKey);
    }
    public void SetSessionProfile(string profileName)
    {
        if (string.IsNullOrEmpty(profileName))
        {
            _sessionSavePath = "user://supabase_session.json";
        }
        else
        {
            _sessionSavePath = $"user://supabase_session_{profileName}.json";
            GD.Print($"SupabaseClient: Session profile set to '{profileName}'. Path: {_sessionSavePath}");
        }
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

    public string GetOAuthUrl(string provider)
    {
        // Use a local port that we can listen on
        string localRedirect = "http://localhost:4321/";
        string encodedRedirect = Uri.EscapeDataString(localRedirect);
        return $"{_baseUrl}/auth/v1/authorize?provider={provider}&redirect_to={encodedRedirect}";
    }

    /// <summary>
    /// Starts a temporary local listener to capture the OAuth token/code from the browser redirect.
    /// </summary>
    public async Task<bool> ListenForOAuthCallback()
    {
        using var listener = new System.Net.HttpListener();
        listener.Prefixes.Add("http://localhost:4321/");
        listener.Prefixes.Add("http://127.0.0.1:4321/");
        
        try 
        {
            listener.Start();
            GD.Print("SupabaseClient: Listening for OAuth callback on http://localhost:4321/...");
            
            // --- STEP 1: Wait for the initial redirect from Supabase ---
            // This request will have the token in the URL fragment (#), which the server can't see.
            var context1 = await GetContextWithTimeout(listener, 60000);
            if (context1 == null) 
            {
                GD.PrintErr("SupabaseClient: Step 1 (Supabase Redirect) timed out.");
                return false;
            }
            GD.Print("SupabaseClient: Step 1 received, sending Bridge script...");

            // Return a "Bridge" page that re-sends the fragment as query parameters
            string bridgeHtml = @"
                <html><body style='font-family:sans-serif;text-align:center;padding-top:50px;background:#0b1e1a;color:white;'>
                    <h1>Authorizing...</h1>
                    <p>Please wait while we connect your account.</p>
                    <script>
                        const hash = window.location.hash;
                        if (hash) {
                            // Re-send to Godot but move # to ? so the server can see it
                            window.location.href = window.location.origin + window.location.pathname + '?' + hash.substring(1);
                        } else {
                            document.body.innerHTML = '<h1>Error</h1><p>No authorization data found.</p>';
                        }
                    </script>
                </body></html>";
            await WriteHtmlResponse(context1, bridgeHtml);

            // --- STEP 2: Wait for the "Bridge" request containing tokens ---
            var context2 = await GetContextWithTimeout(listener, 10000);
            if (context2 == null) 
            {
                GD.PrintErr("SupabaseClient: Step 2 (Bridge Redirect) timed out.");
                return false;
            }
            GD.Print("SupabaseClient: Step 2 received, parsing tokens...");

            // Parse tokens from QueryString
            var qs = context2.Request.QueryString;
            _accessToken = qs["access_token"];
            _refreshToken = qs["refresh_token"];
            
            // Return final success page with Close button
            string successHtml = @"
                <html><body style='font-family:sans-serif;text-align:center;padding-top:50px;background:#0b1e1a;color:white;'>
                    <div style='border:2px solid #b8860b; display:inline-block; padding:40px; border-radius:15px; background:rgba(0,0,0,0.3); box-shadow: 0 10px 30px rgba(0,0,0,0.5);'>
                        <h1 style='color:#daa520; font-size: 32px; margin-bottom: 10px;'>Login Successful!</h1>
                        <p style='font-size: 18px; color: #ccc;'>Your account is now connected.</p>
                        <p id='timer' style='margin-top:20px; font-size: 16px; color: #888;'>This window will close in 5 seconds...</p>
                        <button onclick='window.close()' style='margin-top:20px; padding:15px 30px; font-size:18px; font-weight:bold; cursor:pointer; background:#daa520; color:#0b1e1a; border:none; border-radius:8px; box-shadow: 0 4px 8px rgba(0,0,0,0.5); font-family: sans-serif;'>CLOSE NOW</button>
                    </div>
                    <script>
                        let seconds = 5;
                        const timerElement = document.getElementById('timer');
                        const interval = setInterval(() => {
                            seconds--;
                            if (seconds <= 0) {
                                clearInterval(interval);
                                window.close();
                                // Fallback for when browser blocks window.close()
                                timerElement.innerHTML = '<span style=""color:#4caf50; font-weight:bold;"">✓ Login Complete.</span><br>You can now safely return to the game.';
                                document.querySelector(""button"").innerText = ""RETURN TO GAME (CLOSE)"";
                            } else {
                                timerElement.innerText = 'Closing in ' + seconds + ' seconds...';
                            }
                        }, 1000);
                        // Initial try
                        setTimeout(() => window.close(), 5000);
                    </script>
                </body></html>";
            await WriteHtmlResponse(context2, successHtml);

            listener.Stop();

            if (!string.IsNullOrEmpty(_accessToken))
            {
                GD.Print($"SupabaseClient: Captured AccessToken (len: {_accessToken.Length}) and RefreshToken (len: {_refreshToken?.Length ?? 0})");
                SaveSession();
                
                // Fetch the user object to get the ID and verify the token
                bool userOk = await FetchUser();
                if (userOk)
                {
                    GD.Print($"SupabaseClient: OAuth flow complete for User: {_userId}");
                    OnAuthStateChanged?.Invoke("signed_in");
                    return true;
                }
                else
                {
                    GD.PrintErr("SupabaseClient: Failed to fetch user after OAuth capture.");
                }
            }
            else
            {
                GD.PrintErr("SupabaseClient: No access_token found in callback.");
            }

            return false;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"SupabaseClient: Listener error: {ex.Message}");
            return false;
        }
    }

    private async Task<System.Net.HttpListenerContext> GetContextWithTimeout(System.Net.HttpListener listener, int timeoutMs)
    {
        var task = listener.GetContextAsync();
        if (await Task.WhenAny(task, Task.Delay(timeoutMs)) == task)
            return await task;
        GD.PrintErr($"SupabaseClient: Listener timed out after {timeoutMs}ms");
        return null;
    }

    private async Task WriteHtmlResponse(System.Net.HttpListenerContext context, string html)
    {
        var response = context.Response;
        byte[] buffer = Encoding.UTF8.GetBytes(html);
        response.ContentLength64 = buffer.Length;
        response.ContentType = "text/html";
        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        response.OutputStream.Close();
    }

    /// <summary>
    /// Fetches the current user profile from /auth/v1/user.
    /// Polpulates _userId and triggers signed_in if successful.
    /// </summary>
    public async Task<bool> FetchUser()
    {
        if (string.IsNullOrEmpty(_accessToken)) return false;
        
        var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/auth/v1/user");
        SetAuthHeaders(request);
        
        try
        {
            var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                var user = JsonSerializer.Deserialize<JsonElement>(json);
                if (user.TryGetProperty("id", out var uid))
                {
                    _userId = uid.GetString();
                    
                    // Also capture metadata here
                    if (user.TryGetProperty("user_metadata", out var meta))
                    {
                        if (meta.TryGetProperty("display_name", out var dn))
                            UserMetadataName = dn.GetString();
                        else if (meta.TryGetProperty("full_name", out var fn))
                            UserMetadataName = fn.GetString();
                        else if (meta.TryGetProperty("name", out var n))
                            UserMetadataName = n.GetString();

                        if (meta.TryGetProperty("avatar_url", out var av))
                            UserMetadataAvatar = av.GetString();
                        else if (meta.TryGetProperty("picture", out var pic))
                            UserMetadataAvatar = pic.GetString();
                    }
                    SaveSession();
                    return true;
                }
            }
            GD.PrintErr($"SupabaseClient: FetchUser failed ({response.StatusCode}): {json}");
        }
        catch (Exception ex) { GD.PrintErr($"SupabaseClient: FetchUser error: {ex.Message}"); }
        return false;
    }

    /// <summary>
    /// Exchange an OAuth code/token for a session.
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
            // Refresh metadata too
            await FetchUser();
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
        UserMetadataName = null;
        UserMetadataAvatar = null;
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
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            GD.Print($"SupabaseClient: Using token: {_accessToken.Substring(0, 10)}...");
        }
        else
        {
            // GD.Print("SupabaseClient: No access token available for request");
        }
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
        
        if (r.TryGetProperty("user", out var user))
        {
            if (user.TryGetProperty("id", out var uid))
                _userId = uid.GetString();
                
            if (user.TryGetProperty("user_metadata", out var meta))
            {
                if (meta.TryGetProperty("display_name", out var dn))
                    UserMetadataName = dn.GetString();
                else if (meta.TryGetProperty("full_name", out var fn))
                    UserMetadataName = fn.GetString();
                else if (meta.TryGetProperty("name", out var n))
                    UserMetadataName = n.GetString();

                if (meta.TryGetProperty("avatar_url", out var av))
                    UserMetadataAvatar = av.GetString();
                else if (meta.TryGetProperty("picture", out var pic))
                    UserMetadataAvatar = pic.GetString();
            }
        }
    }

    private void SaveSession()
    {
        try
        {
            var session = JsonSerializer.Serialize(new {
                access_token = _accessToken,
                refresh_token = _refreshToken,
                user_id = _userId,
                name = UserMetadataName,
                avatar = UserMetadataAvatar
            });
            using var file = FileAccess.Open(_sessionSavePath, FileAccess.ModeFlags.Write);
            file?.StoreString(session);
        }
        catch (Exception ex) { GD.PrintErr($"SupabaseClient: Failed to save session: {ex.Message}"); }
    }

    public bool TryLoadSession()
    {
        try
        {
            if (!FileAccess.FileExists(_sessionSavePath)) return false;
            using var file = FileAccess.Open(_sessionSavePath, FileAccess.ModeFlags.Read);
            var json = file?.GetAsText();
            if (string.IsNullOrEmpty(json)) return false;

            var session = JsonSerializer.Deserialize<JsonElement>(json);
            _accessToken = session.TryGetProperty("access_token", out var at) ? at.GetString() : null;
            _refreshToken = session.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
            _userId = session.TryGetProperty("user_id", out var uid) ? uid.GetString() : null;
            
            // Restore metadata
            UserMetadataName = session.TryGetProperty("name", out var n) ? n.GetString() : null;
            UserMetadataAvatar = session.TryGetProperty("avatar", out var a) ? a.GetString() : null;
            
            return !string.IsNullOrEmpty(_accessToken);
        }
        catch { return false; }
    }

    private void ClearSession()
    {
        try
        {
            if (FileAccess.FileExists(_sessionSavePath))
                DirAccess.Open("user://").Remove(_sessionSavePath.Replace("user://", ""));
        }
        catch { }
    }
}
