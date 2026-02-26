-- =============================================================
-- Phase 4: Realtime Support & RLS Policy Simplification
-- =============================================================

-- 1. Enable Realtime for key social tables
-- This allows the Godot client to receive instant updates when 
-- a friend request is sent or a player's status changes.
BEGIN;
  DROP PUBLICATION IF EXISTS supabase_realtime;
  CREATE PUBLICATION supabase_realtime FOR TABLE 
    public.friendships, 
    public.player_presence,
    public.player_profiles;
COMMIT;

-- 2. Simplify RLS for player_profiles
-- The previous "exists" check could be slow or cause circularity.
-- We want any authenticated user to be able to see ANY public profile.
DROP POLICY IF EXISTS "Users can read public profiles" ON public.player_profiles;
DROP POLICY IF EXISTS "Public profiles are viewable by everyone authenticated" ON public.player_profiles;
CREATE POLICY "Public profiles are viewable by everyone authenticated" 
ON public.player_profiles FOR SELECT 
TO authenticated 
USING (TRUE); -- We allow viewing all profiles, but privacy_settings table can still be used by app logic to filter.

-- 3. Fix friendships RLS
-- Ensure players can only insert friendships where they are the requester.
DROP POLICY IF EXISTS "Users can insert friendships" ON public.friendships;
DROP POLICY IF EXISTS "Users can initiate friendships" ON public.friendships;
CREATE POLICY "Users can initiate friendships" 
ON public.friendships FOR INSERT 
TO authenticated 
WITH CHECK (auth.uid() = requester_id);

-- 4. Fix presence RLS
-- Allow all authenticated users to see online statuses. 
-- The client-side privacy settings handle the filters.
DROP POLICY IF EXISTS "Friends can view presence" ON public.player_presence;
DROP POLICY IF EXISTS "Authenticated users can view presence" ON public.player_presence;
CREATE POLICY "Authenticated users can view presence" 
ON public.player_presence FOR SELECT 
TO authenticated 
USING (TRUE);

-- 5. Ensure the heartbeat function is strictly tied to auth.uid()
DROP FUNCTION IF EXISTS public.heartbeat(TEXT, TEXT);
CREATE OR REPLACE FUNCTION public.heartbeat(p_status TEXT DEFAULT 'online', p_room_code TEXT DEFAULT NULL)
RETURNS VOID AS $$
BEGIN
    INSERT INTO public.player_presence (player_id, status, last_heartbeat, current_room_code, updated_at)
    VALUES (auth.uid(), p_status, NOW(), p_room_code, NOW())
    ON CONFLICT (player_id) DO UPDATE SET
        status = p_status,
        last_heartbeat = NOW(),
        current_room_code = p_room_code,
        updated_at = NOW()
    WHERE player_presence.player_id = auth.uid(); -- Extra safety
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;

-- 6. Add explicit GRANTS for authenticated users
-- RLS handles the rows, but PostgREST needs base permission to call functions and query tables.
GRANT USAGE ON SCHEMA public TO authenticated;
GRANT USAGE ON SCHEMA public TO anon;
GRANT ALL ON TABLE public.player_profiles TO authenticated;
GRANT ALL ON TABLE public.friendships TO authenticated;
GRANT ALL ON TABLE public.player_presence TO authenticated;
GRANT ALL ON TABLE public.privacy_settings TO authenticated;
GRANT EXECUTE ON FUNCTION public.heartbeat(TEXT, TEXT) TO authenticated;
GRANT EXECUTE ON FUNCTION public.heartbeat(TEXT, TEXT) TO anon;

-- PostgREST role switching
GRANT authenticated TO authenticator;
GRANT anon TO authenticator;

-- Ensure sequence permissions if any (though these use UUIDs mostly)
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO authenticated;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO anon;
