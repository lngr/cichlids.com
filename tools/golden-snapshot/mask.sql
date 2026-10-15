TRUNCATE public.profile_identity, public.web_session,
         archive.legacy_comment, public.outbox_event, public.webhook_subscription;
UPDATE public.profile SET city = NULL, external_avatar_url = NULL,
    last_login_at = NULL, suspended_until = NULL,
    avatar_media_id = NULL, profile_image_media_id = NULL;
UPDATE public.media_item SET checksum_sha256 = NULL;
