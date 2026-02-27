using Godot;
using System;
using System.Threading.Tasks;
using OkieRummyGodot.Core.Networking;

namespace OkieRummyGodot.UI.Scripts;

/// <summary>
/// Login screen with IDP sign-in buttons and "Play as Guest".
/// First screen the player sees. On success, transitions to Lobby.
/// </summary>
public partial class LoginScreen : Control
{
    [Export] public Button GoogleButton;
    [Export] public Button FacebookButton;
    [Export] public Button DiscordButton;
    [Export] public Button TwitchButton;
    [Export] public Button GuestButton;
    [Export] public Label StatusLabel;
    [Export] public Label VersionLabel;
    private AudioEngine _audioEngine;

    private AccountManager _accountManager;

    private const string LOBBY_PATH = "res://UI/Scenes/Lobby.tscn";

    public override void _Ready()
    {
        _audioEngine = GetNodeOrNull<AudioEngine>("/root/AudioEngine");
        GD.Print("LoginScreen: _Ready started");
        _accountManager = GetNodeOrNull<AccountManager>("/root/AccountManager");
        GD.Print($"LoginScreen: AccountManager found: {_accountManager != null}");

        // Wire buttons
        GoogleButton?.Connect("pressed", Callable.From(() => OnIdpPressed("google")));
        FacebookButton?.Connect("pressed", Callable.From(() => OnIdpPressed("facebook")));
        DiscordButton?.Connect("pressed", Callable.From(() => OnIdpPressed("discord")));
        TwitchButton?.Connect("pressed", Callable.From(() => OnIdpPressed("twitch")));
        GuestButton?.Connect("pressed", Callable.From(OnGuestPressed));

        FilterIdpButtons();
        ApplyIdpLogos();
        ApplyTheme();

        // Auto check for stored session
        CallDeferred(nameof(TryAutoLogin));
    }

    private async void TryAutoLogin()
    {
        if (_accountManager == null) return;
        UpdateStatus("Authenticating...");
        bool ok = await _accountManager.Supabase.RefreshSession();
        if (ok)
        {
            string name = !string.IsNullOrEmpty(_accountManager.DisplayName) ? _accountManager.DisplayName : "Player";
            UpdateStatus($"Welcome back, {name}!");
            await Task.Delay(1500); 
            GoToLobby();
        }
        else
        {
            UpdateStatus("Ready to sign in.");
        }
    }

    private void OnIdpPressed(string provider)
    {
        if (_accountManager == null)
        {
            UpdateStatus("Account system not available");
            return;
        }

        string url = _accountManager.GetOAuthUrl(provider);
        GD.Print($"LoginScreen: Opening OAuth URL for {provider}");
        OS.ShellOpen(url);
        UpdateStatus($"Complete sign-in in your browser...");
        SetButtonsDisabled(true);

        // Wait for the browser redirect to hit our local listener
        _ = HandleOAuthCallback();
    }

    private async Task HandleOAuthCallback()
    {
        GD.Print("LoginScreen: Waiting for OAuth callback...");
        bool ok = await _accountManager.Supabase.ListenForOAuthCallback();
        GD.Print($"LoginScreen: OAuth callback result: {ok}");
        
        if (ok)
        {
            UpdateStatus("Signed in!");
            GD.Print("LoginScreen: Sign-in successful, delaying navigation...");
            await Task.Delay(500);
            GoToLobby();
        }
        else
        {
            GD.PrintErr("LoginScreen: OAuth callback failed or timed out.");
            UpdateStatus("Sign-in failed or timed out.");
            SetButtonsDisabled(false);
        }
    }

    private async void OnGuestPressed()
    {
        if (_accountManager == null)
        {
            // Fallback: just go to lobby without account
            GoToLobby();
            return;
        }

        SetButtonsDisabled(true);
        UpdateStatus("Signing in as guest...");

        bool ok = await _accountManager.SignInAsGuest();
        if (ok)
        {
            UpdateStatus("Welcome!");
            await Task.Delay(300);
            GoToLobby();
        }
        else
        {
            UpdateStatus("Sign-in failed. Try again.");
            SetButtonsDisabled(false);
        }
    }

    private void GoToLobby()
    {
        GD.Print($"LoginScreen: Navigating to Lobby ({LOBBY_PATH})...");
        CallDeferred(nameof(DeferredGoToLobby));
    }

    private void DeferredGoToLobby()
    {
        var error = GetTree().ChangeSceneToFile(LOBBY_PATH);
        if (error != Error.Ok)
        {
            GD.PrintErr($"LoginScreen: Failed to change scene! Error: {error}");
        }
    }

    private void UpdateStatus(string msg)
    {
        if (StatusLabel != null) StatusLabel.Text = msg;
    }

    private void SetButtonsDisabled(bool disabled)
    {
        if (GoogleButton != null) GoogleButton.Disabled = disabled;
        if (FacebookButton != null) FacebookButton.Disabled = disabled;
        if (DiscordButton != null) DiscordButton.Disabled = disabled;
        if (TwitchButton != null) TwitchButton.Disabled = disabled;
        if (GuestButton != null) GuestButton.Disabled = disabled;
    }

    private void FilterIdpButtons()
    {
        if (_accountManager == null) 
        {
            GD.PrintErr("LoginScreen: Cannot filter buttons - AccountManager is null");
            return;
        }
        
        var enabled = _accountManager.EnabledProviders;
        GD.Print($"LoginScreen: Filtering buttons. Enabled providers: {string.Join(",", enabled)}");
        
        // Hide by default
        if (GoogleButton != null) { GoogleButton.Visible = enabled.Contains("google"); GD.Print($"LoginScreen: GoogleBtn visibility -> {GoogleButton.Visible}"); }
        if (FacebookButton != null) { FacebookButton.Visible = enabled.Contains("facebook"); GD.Print($"LoginScreen: FacebookBtn visibility -> {FacebookButton.Visible}"); }
        if (DiscordButton != null) { DiscordButton.Visible = enabled.Contains("discord"); GD.Print($"LoginScreen: DiscordBtn visibility -> {DiscordButton.Visible}"); }
        if (TwitchButton != null) { TwitchButton.Visible = enabled.Contains("twitch"); GD.Print($"LoginScreen: TwitchBtn visibility -> {TwitchButton.Visible}"); }
    }

    private void ApplyIdpLogos()
    {
        // Apply Google Logo
        string googlePath = "res://Assets/google_logo.png";
        GD.Print($"LoginScreen: Applying logos. Path: {googlePath}, Exists: {FileAccess.FileExists(googlePath)}");
        if (GoogleButton != null && FileAccess.FileExists(googlePath))
        {
            try
            {
                string globalPath = ProjectSettings.GlobalizePath(googlePath);
                GD.Print($"LoginScreen: Loading logo from global path: {globalPath}");
                var image = Image.LoadFromFile(globalPath);
                if (image != null)
                {
                    GoogleButton.Icon = ImageTexture.CreateFromImage(image);
                    GoogleButton.ExpandIcon = true;
                    GD.Print("LoginScreen: Google icon applied successfully");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"LoginScreen: Failed to load Google logo: {ex.Message}");
            }
        }
    }

    private void ApplyTheme()
    {
        // Glass panel effect on the login container
        var panel = GetNodeOrNull<PanelContainer>("CenterContainer/LoginPanel");
        if (panel != null)
        {
            var style = new StyleBoxFlat();
            style.BgColor = new Color(0.05f, 0.12f, 0.10f, 0.85f);
            style.BorderColor = new Color(0.72f, 0.53f, 0.04f, 0.6f);
            style.SetBorderWidthAll(2);
            style.SetCornerRadiusAll(16);
            style.ShadowColor = new Color(0, 0, 0, 0.5f);
            style.ShadowSize = 10;
            style.SetContentMarginAll(32);
            panel.AddThemeStyleboxOverride("panel", style);
        }
    }
}
