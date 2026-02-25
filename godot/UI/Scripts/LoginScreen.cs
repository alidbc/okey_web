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

    private AccountManager _accountManager;

    private const string LOBBY_PATH = "res://UI/Scenes/Lobby.tscn";

    public override void _Ready()
    {
        _accountManager = GetNodeOrNull<AccountManager>("/root/AccountManager");

        // Wire buttons
        GoogleButton?.Connect("pressed", Callable.From(() => OnIdpPressed("google")));
        FacebookButton?.Connect("pressed", Callable.From(() => OnIdpPressed("facebook")));
        DiscordButton?.Connect("pressed", Callable.From(() => OnIdpPressed("discord")));
        TwitchButton?.Connect("pressed", Callable.From(() => OnIdpPressed("twitch")));
        GuestButton?.Connect("pressed", Callable.From(OnGuestPressed));

        if (VersionLabel != null)
            VersionLabel.Text = $"v{ProjectSettings.GetSetting("application/config/version", "0.1.0")}";

        // If already signed in (restored session), skip login
        if (_accountManager?.Supabase?.IsAuthenticated == true)
        {
            GD.Print("LoginScreen: Session restored, skipping to lobby");
            GoToLobby();
            return;
        }

        ApplyTheme();

        // Auto check for stored session
        CallDeferred(nameof(TryAutoLogin));
    }

    private async void TryAutoLogin()
    {
        if (_accountManager == null) return;
        UpdateStatus("Checking session...");
        bool ok = await _accountManager.Supabase.RefreshSession();
        if (ok)
        {
            UpdateStatus("Welcome back!");
            await Task.Delay(500);
            GoToLobby();
        }
        else
        {
            UpdateStatus("Sign in to play");
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

        // Poll for session completion (user finishes OAuth in browser)
        _ = PollForOAuthCompletion();
    }

    private async Task PollForOAuthCompletion()
    {
        for (int i = 0; i < 60; i++) // Poll for up to 2 minutes
        {
            await Task.Delay(2000);
            if (_accountManager?.Supabase?.IsAuthenticated == true)
            {
                UpdateStatus("Signed in!");
                await Task.Delay(300);
                GoToLobby();
                return;
            }
            // Try refreshing session in case callback wrote tokens
            bool ok = await _accountManager.Supabase.RefreshSession();
            if (ok)
            {
                UpdateStatus("Signed in!");
                await Task.Delay(300);
                GoToLobby();
                return;
            }
        }
        UpdateStatus("Sign-in timed out. Try again.");
        SetButtonsDisabled(false);
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
        GetTree().ChangeSceneToFile(LOBBY_PATH);
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
