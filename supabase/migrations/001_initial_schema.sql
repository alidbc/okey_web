-- =============================================================
-- Okey Rummy: Player Account & Social System Schema
-- =============================================================

-- ===================== PLAYER PROFILES =====================
CREATE TABLE IF NOT EXISTS public.player_profiles (
    id UUID PRIMARY KEY REFERENCES auth.users(id) ON DELETE CASCADE,
    display_name TEXT NOT NULL DEFAULT 'Player',
    avatar_url TEXT DEFAULT '',
    account_type TEXT NOT NULL DEFAULT 'guest' CHECK (account_type IN ('guest', 'idp')),
    device_id TEXT,  -- for guest accounts
    level INT NOT NULL DEFAULT 1,
    xp INT NOT NULL DEFAULT 0,
    games_played INT NOT NULL DEFAULT 0,
    games_won INT NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Index for guest device lookup
CREATE INDEX IF NOT EXISTS idx_profiles_device_id ON public.player_profiles(device_id) WHERE device_id IS NOT NULL;

-- ===================== FRIENDSHIPS =====================
CREATE TABLE IF NOT EXISTS public.friendships (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    requester_id UUID NOT NULL REFERENCES public.player_profiles(id) ON DELETE CASCADE,
    recipient_id UUID NOT NULL REFERENCES public.player_profiles(id) ON DELETE CASCADE,
    status TEXT NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'accepted', 'blocked')),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (requester_id, recipient_id),
    CHECK (requester_id <> recipient_id)
);

CREATE INDEX IF NOT EXISTS idx_friendships_requester ON public.friendships(requester_id);
CREATE INDEX IF NOT EXISTS idx_friendships_recipient ON public.friendships(recipient_id);
CREATE INDEX IF NOT EXISTS idx_friendships_status ON public.friendships(status);

-- ===================== PRIVACY SETTINGS =====================
CREATE TABLE IF NOT EXISTS public.privacy_settings (
    player_id UUID PRIMARY KEY REFERENCES public.player_profiles(id) ON DELETE CASCADE,
    online_status_visibility TEXT NOT NULL DEFAULT 'friends' CHECK (online_status_visibility IN ('everyone', 'friends', 'nobody')),
    allow_friend_requests TEXT NOT NULL DEFAULT 'everyone' CHECK (allow_friend_requests IN ('everyone', 'fof', 'nobody')),
    show_last_seen BOOLEAN NOT NULL DEFAULT TRUE,
    profile_visibility TEXT NOT NULL DEFAULT 'public' CHECK (profile_visibility IN ('public', 'friends')),
    show_in_leaderboards BOOLEAN NOT NULL DEFAULT TRUE,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- ===================== PRESENCE =====================
CREATE TABLE IF NOT EXISTS public.player_presence (
    player_id UUID PRIMARY KEY REFERENCES public.player_profiles(id) ON DELETE CASCADE,
    status TEXT NOT NULL DEFAULT 'offline' CHECK (status IN ('online', 'in_game', 'away', 'offline')),
    last_heartbeat TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_seen TIMESTAMPTZ,
    current_room_code TEXT,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- ===================== FUNCTIONS =====================

-- Auto-create profile + privacy + presence on user signup
CREATE OR REPLACE FUNCTION public.handle_new_user()
RETURNS TRIGGER AS $$
DECLARE
    raw_meta JSONB;
    v_display_name TEXT;
    v_avatar TEXT;
    v_account_type TEXT;
    v_device_id TEXT;
BEGIN
    raw_meta := NEW.raw_user_meta_data;

    v_display_name := COALESCE(
        raw_meta->>'display_name',
        raw_meta->>'full_name',
        raw_meta->>'name',
        'Player_' || LEFT(NEW.id::TEXT, 6)
    );
    v_avatar := COALESCE(raw_meta->>'avatar_url', '');
    v_device_id := raw_meta->>'device_id';
    v_account_type := CASE WHEN v_device_id IS NOT NULL THEN 'guest' ELSE 'idp' END;

    INSERT INTO public.player_profiles (id, display_name, avatar_url, account_type, device_id)
    VALUES (NEW.id, v_display_name, v_avatar, v_account_type, v_device_id)
    ON CONFLICT (id) DO UPDATE SET
        display_name = EXCLUDED.display_name,
        avatar_url = EXCLUDED.avatar_url,
        updated_at = NOW();

    INSERT INTO public.privacy_settings (player_id) VALUES (NEW.id)
    ON CONFLICT (player_id) DO NOTHING;

    INSERT INTO public.player_presence (player_id) VALUES (NEW.id)
    ON CONFLICT (player_id) DO NOTHING;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;

-- Trigger on auth.users insert
DROP TRIGGER IF EXISTS on_auth_user_created ON auth.users;
CREATE TRIGGER on_auth_user_created
    AFTER INSERT ON auth.users
    FOR EACH ROW EXECUTE FUNCTION public.handle_new_user();

-- Heartbeat function (call from client every 30s)
CREATE OR REPLACE FUNCTION public.heartbeat(p_status TEXT DEFAULT 'online', p_room_code TEXT DEFAULT NULL)
RETURNS VOID AS $$
BEGIN
    INSERT INTO public.player_presence (player_id, status, last_heartbeat, current_room_code, updated_at)
    VALUES (auth.uid(), p_status, NOW(), p_room_code, NOW())
    ON CONFLICT (player_id) DO UPDATE SET
        status = p_status,
        last_heartbeat = NOW(),
        current_room_code = p_room_code,
        updated_at = NOW();
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;

-- Mark stale players as offline (run via pg_cron every 60s)
CREATE OR REPLACE FUNCTION public.cleanup_stale_presence()
RETURNS VOID AS $$
BEGIN
    UPDATE public.player_presence
    SET status = 'offline', last_seen = last_heartbeat, current_room_code = NULL, updated_at = NOW()
    WHERE status <> 'offline' AND last_heartbeat < NOW() - INTERVAL '90 seconds';
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;

-- ===================== ROW LEVEL SECURITY =====================
ALTER TABLE public.player_profiles ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.friendships ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.privacy_settings ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.player_presence ENABLE ROW LEVEL SECURITY;

-- Profile: read own or public profiles
CREATE POLICY "Users can read own profile" ON public.player_profiles
    FOR SELECT USING (id = auth.uid());
CREATE POLICY "Users can read public profiles" ON public.player_profiles
    FOR SELECT USING (
        EXISTS (SELECT 1 FROM public.privacy_settings ps WHERE ps.player_id = id AND ps.profile_visibility = 'public')
        OR EXISTS (SELECT 1 FROM public.friendships f
            WHERE f.status = 'accepted'
            AND ((f.requester_id = auth.uid() AND f.recipient_id = id) OR (f.recipient_id = auth.uid() AND f.requester_id = id)))
    );
CREATE POLICY "Users can update own profile" ON public.player_profiles
    FOR UPDATE USING (id = auth.uid());

-- Friendships: users can see their own friendships
CREATE POLICY "Users can view own friendships" ON public.friendships
    FOR SELECT USING (requester_id = auth.uid() OR recipient_id = auth.uid());
CREATE POLICY "Users can insert friendships" ON public.friendships
    FOR INSERT WITH CHECK (requester_id = auth.uid());
CREATE POLICY "Users can update own friendships" ON public.friendships
    FOR UPDATE USING (requester_id = auth.uid() OR recipient_id = auth.uid());
CREATE POLICY "Users can delete own friendships" ON public.friendships
    FOR DELETE USING (requester_id = auth.uid() OR recipient_id = auth.uid());

-- Privacy: own only
CREATE POLICY "Users can manage own privacy" ON public.privacy_settings
    FOR ALL USING (player_id = auth.uid());

-- Presence: own write, friends read (respecting privacy)
CREATE POLICY "Users can update own presence" ON public.player_presence
    FOR ALL USING (player_id = auth.uid());
CREATE POLICY "Friends can view presence" ON public.player_presence
    FOR SELECT USING (
        EXISTS (SELECT 1 FROM public.friendships f
            WHERE f.status = 'accepted'
            AND ((f.requester_id = auth.uid() AND f.recipient_id = player_id) OR (f.recipient_id = auth.uid() AND f.requester_id = player_id)))
        AND NOT EXISTS (SELECT 1 FROM public.privacy_settings ps WHERE ps.player_id = player_presence.player_id AND ps.online_status_visibility = 'nobody')
    );

-- Grant access to authenticated users
GRANT USAGE ON SCHEMA public TO anon, authenticated;
GRANT SELECT ON public.player_profiles TO anon, authenticated;
GRANT INSERT, UPDATE ON public.player_profiles TO authenticated;
GRANT ALL ON public.friendships TO authenticated;
GRANT ALL ON public.privacy_settings TO authenticated;
GRANT ALL ON public.player_presence TO authenticated;
