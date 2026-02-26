-- Add more tracking fields to game invitations
ALTER TABLE public.player_presence
    ADD COLUMN IF NOT EXISTS invite_from_id UUID DEFAULT NULL,
    ADD COLUMN IF NOT EXISTS invite_response TEXT DEFAULT NULL; -- 'accepted', 'declined'

-- Updated RPC: send_invite — include the inviter's ID
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
        invite_from_id   = auth.uid(), -- Capture the inviter's ID automatically
        invite_response  = NULL,       -- Reset response
        updated_at       = NOW()
    WHERE player_id = p_target_player_id;
END;
$$;

-- New RPC: decline_invite — called by the invitee to notify the inviter
CREATE OR REPLACE FUNCTION public.decline_invite(
    p_inviter_id UUID
)
RETURNS VOID
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
BEGIN
    -- 1. Mark our own presence as 'declined' so the inviter (who is our friend) sees it
    UPDATE public.player_presence
    SET
        invite_response = 'declined',
        invite_room_code = NULL,
        invite_from_name = NULL,
        updated_at = NOW()
    WHERE player_id = auth.uid();
    
    -- Note: The inviter should be watching the 'player_presence' of their friends.
END;
$$;

GRANT EXECUTE ON FUNCTION public.decline_invite(UUID) TO authenticated;

-- Updated RPC: clear_invite — called after acceptance to reset state
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
        invite_from_id   = NULL,
        invite_response  = NULL,
        updated_at       = NOW()
    WHERE player_id = auth.uid();
END;
$$;
