-- Add invite fields to player_presence so we can deliver invites via Realtime
ALTER TABLE public.player_presence
    ADD COLUMN IF NOT EXISTS invite_room_code TEXT DEFAULT NULL,
    ADD COLUMN IF NOT EXISTS invite_from_name TEXT DEFAULT NULL;

-- RPC: send_invite — called by the inviter to push an invite to a target player
CREATE OR REPLACE FUNCTION public.send_invite(
    p_target_player_id UUID,
    p_room_code TEXT,
    p_inviter_name TEXT
)
RETURNS VOID
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
BEGIN
    UPDATE public.player_presence
    SET
        invite_room_code = p_room_code,
        invite_from_name = p_inviter_name,
        updated_at       = NOW()
    WHERE player_id = p_target_player_id;
END;
$$;

GRANT EXECUTE ON FUNCTION public.send_invite(UUID, TEXT, TEXT) TO authenticated;

-- RPC: clear_invite — called by the invitee after accepting or declining
CREATE OR REPLACE FUNCTION public.clear_invite()
RETURNS VOID
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
BEGIN
    UPDATE public.player_presence
    SET
        invite_room_code = NULL,
        invite_from_name = NULL,
        updated_at       = NOW()
    WHERE player_id = auth.uid();
END;
$$;

GRANT EXECUTE ON FUNCTION public.clear_invite() TO authenticated;
