TRUNCATE public.profile_identity, public.web_session,
         archive.legacy_comment, public.outbox_event, public.webhook_subscription,
         public.web_push_subscription, public.web_push_reminder_sent,
         public.video_upload, public.video_job, public.vacation,
         public.care_reminder_snooze, public.care_todo, public.comment_vote,
         public.tank_selected_member, public.used_checksum;
DELETE FROM public.follow WHERE status <> 'approved'
    OR target_tank_id IN (SELECT id FROM public.tank WHERE profile_visibility <> 'public');
WITH gone AS (DELETE FROM public.care_entry WHERE only_me RETURNING water_change_session_id)
DELETE FROM public.water_change_session WHERE id IN (SELECT water_change_session_id FROM gone);
DELETE FROM public.care_entry WHERE tank_id IN (SELECT id FROM public.tank WHERE stream_visibility <> 'public');
DELETE FROM public.water_change_session WHERE tank_id IN (SELECT id FROM public.tank WHERE stream_visibility <> 'public');
DELETE FROM public.care_plan_rule_change WHERE tank_id IN (SELECT id FROM public.tank WHERE stream_visibility <> 'public');
DELETE FROM public.care_plan_rule WHERE tank_id IN (SELECT id FROM public.tank WHERE stream_visibility <> 'public');
DELETE FROM public.streak_freeze WHERE tank_id IN (SELECT id FROM public.tank WHERE stream_visibility <> 'public');
DELETE FROM public.tank_decommissioning WHERE tank_id IN (SELECT id FROM public.tank WHERE stream_visibility <> 'public');
DELETE FROM public.tank_visibility_consent WHERE tank_id IN (SELECT id FROM public.tank WHERE stream_visibility <> 'public');
DELETE FROM public.story_subject WHERE tank_id IN (SELECT id FROM public.tank WHERE profile_visibility <> 'public')
    OR inhabitant_id IN (SELECT id FROM public.inhabitant WHERE tank_id IN (SELECT id FROM public.tank WHERE profile_visibility <> 'public'));
DELETE FROM public.inhabitant WHERE tank_id IN (SELECT id FROM public.tank WHERE profile_visibility <> 'public');
DELETE FROM public.media_link WHERE tank_id IN (SELECT id FROM public.tank WHERE profile_visibility <> 'public');
DELETE FROM public.comment WHERE tank_id IN (SELECT id FROM public.tank WHERE profile_visibility <> 'public');
DELETE FROM public.rating WHERE tank_id IN (SELECT id FROM public.tank WHERE profile_visibility <> 'public');
DELETE FROM public.collection_entry WHERE collection_id IN (SELECT id FROM public.collection WHERE NOT is_public);
UPDATE public.profile SET city = NULL, external_avatar_url = NULL,
    last_login_at = NULL, suspended_until = NULL;
UPDATE public.media_item SET checksum_sha256 = NULL, taken_at_local = NULL,
    taken_at_offset_minutes = NULL, capture_checked_at = NULL, received_at = created_at,
    captured_in_app = false;
