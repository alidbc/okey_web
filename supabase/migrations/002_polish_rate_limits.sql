-- =============================================================
-- Okey Rummy: Phase 5 Polish — Rate Limiting & Profanity Filter
-- =============================================================

-- ===================== RATE LIMITING =====================
-- Limit friend requests: max 10 per hour per user
CREATE OR REPLACE FUNCTION public.check_friend_request_rate_limit()
RETURNS TRIGGER AS $$
DECLARE
    recent_count INT;
BEGIN
    SELECT COUNT(*) INTO recent_count
    FROM public.friendships
    WHERE requester_id = NEW.requester_id
      AND created_at > NOW() - INTERVAL '1 hour';

    IF recent_count >= 10 THEN
        RAISE EXCEPTION 'Rate limit exceeded: max 10 friend requests per hour';
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;

DROP TRIGGER IF EXISTS check_friend_rate_limit ON public.friendships;
CREATE TRIGGER check_friend_rate_limit
    BEFORE INSERT ON public.friendships
    FOR EACH ROW EXECUTE FUNCTION public.check_friend_request_rate_limit();

-- ===================== DISPLAY NAME VALIDATION =====================
-- Block common profanity in display names (server-side enforcement)
CREATE OR REPLACE FUNCTION public.validate_display_name()
RETURNS TRIGGER AS $$
DECLARE
    blocked_words TEXT[] := ARRAY[
        'fuck', 'shit', 'ass', 'bitch', 'dick', 'cock', 'cunt',
        'nigger', 'nigga', 'faggot', 'retard', 'whore', 'slut',
        'bastard', 'piss', 'damn', 'penis', 'vagina', 'nazi',
        'hitler', 'rape', 'kill', 'murder', 'terrorist'
    ];
    lower_name TEXT;
    word TEXT;
BEGIN
    lower_name := LOWER(NEW.display_name);

    -- Check length
    IF LENGTH(NEW.display_name) < 2 OR LENGTH(NEW.display_name) > 24 THEN
        RAISE EXCEPTION 'Display name must be 2-24 characters';
    END IF;

    -- Check for blocked words
    FOREACH word IN ARRAY blocked_words
    LOOP
        IF lower_name LIKE '%' || word || '%' THEN
            RAISE EXCEPTION 'Display name contains inappropriate language';
        END IF;
    END LOOP;

    -- Only allow alphanumeric, underscores, spaces, hyphens
    IF NEW.display_name !~ '^[a-zA-Z0-9_\- ]+$' THEN
        RAISE EXCEPTION 'Display name can only contain letters, numbers, spaces, hyphens, and underscores';
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;

DROP TRIGGER IF EXISTS validate_name_on_update ON public.player_profiles;
CREATE TRIGGER validate_name_on_update
    BEFORE UPDATE OF display_name ON public.player_profiles
    FOR EACH ROW
    WHEN (OLD.display_name IS DISTINCT FROM NEW.display_name)
    EXECUTE FUNCTION public.validate_display_name();

-- ===================== DISPLAY NAME COOLDOWN =====================
-- Track name changes — allow 1 free, then 7-day cooldown
ALTER TABLE public.player_profiles ADD COLUMN IF NOT EXISTS last_name_change TIMESTAMPTZ;
ALTER TABLE public.player_profiles ADD COLUMN IF NOT EXISTS name_change_count INT NOT NULL DEFAULT 0;

CREATE OR REPLACE FUNCTION public.enforce_name_change_cooldown()
RETURNS TRIGGER AS $$
BEGIN
    -- First change is free
    IF OLD.name_change_count = 0 THEN
        NEW.name_change_count := 1;
        NEW.last_name_change := NOW();
        RETURN NEW;
    END IF;

    -- Subsequent changes: 7-day cooldown
    IF OLD.last_name_change IS NOT NULL AND OLD.last_name_change > NOW() - INTERVAL '7 days' THEN
        RAISE EXCEPTION 'You can change your name once every 7 days. Next change available: %',
            (OLD.last_name_change + INTERVAL '7 days')::TEXT;
    END IF;

    NEW.name_change_count := OLD.name_change_count + 1;
    NEW.last_name_change := NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;

DROP TRIGGER IF EXISTS name_change_cooldown ON public.player_profiles;
CREATE TRIGGER name_change_cooldown
    BEFORE UPDATE OF display_name ON public.player_profiles
    FOR EACH ROW
    WHEN (OLD.display_name IS DISTINCT FROM NEW.display_name)
    EXECUTE FUNCTION public.enforce_name_change_cooldown();

-- ===================== ACCOUNT DELETION (GDPR) =====================
-- Delete all user data — call this via RPC before deleting auth.users entry
CREATE OR REPLACE FUNCTION public.delete_my_account()
RETURNS VOID AS $$
BEGIN
    -- Delete all friendships
    DELETE FROM public.friendships
    WHERE requester_id = auth.uid() OR recipient_id = auth.uid();

    -- Delete presence
    DELETE FROM public.player_presence WHERE player_id = auth.uid();

    -- Delete privacy settings
    DELETE FROM public.privacy_settings WHERE player_id = auth.uid();

    -- Delete profile
    DELETE FROM public.player_profiles WHERE id = auth.uid();

    -- Note: auth.users deletion must be done via admin API
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;

-- ===================== DUPLICATE FRIEND REQUEST PREVENTION =====================
-- Prevent sending a request if one already exists in either direction
CREATE OR REPLACE FUNCTION public.prevent_duplicate_friendship()
RETURNS TRIGGER AS $$
DECLARE
    existing_count INT;
BEGIN
    SELECT COUNT(*) INTO existing_count
    FROM public.friendships
    WHERE (requester_id = NEW.requester_id AND recipient_id = NEW.recipient_id)
       OR (requester_id = NEW.recipient_id AND recipient_id = NEW.requester_id);

    IF existing_count > 0 THEN
        RAISE EXCEPTION 'A friendship or request already exists between these players';
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;

DROP TRIGGER IF EXISTS prevent_duplicate_friend ON public.friendships;
CREATE TRIGGER prevent_duplicate_friend
    BEFORE INSERT ON public.friendships
    FOR EACH ROW EXECUTE FUNCTION public.prevent_duplicate_friendship();
