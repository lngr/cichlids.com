TRUNCATE public.profile_identity, public.web_session,
         archive.legacy_comment, public.outbox_event, public.webhook_subscription,
         public.web_push_subscription, public.web_push_reminder_sent,
         public.video_upload, public.video_job, public.vacation,
         public.care_reminder_snooze, public.care_todo, public.comment_vote,
         public.tank_selected_member, public.used_checksum, public.device_credential,
         public.device_challenge;
-- Provisional profiles belong to unregistered installations; they and their content are removed.
UPDATE public.comment SET deleted_by_profile_id = NULL WHERE deleted_by_profile_id IN (SELECT id FROM public.profile WHERE kind = 'provisional');
UPDATE public.story SET deleted_by_profile_id = NULL WHERE deleted_by_profile_id IN (SELECT id FROM public.profile WHERE kind = 'provisional');
UPDATE public.tank SET deleted_by_profile_id = NULL WHERE deleted_by_profile_id IN (SELECT id FROM public.profile WHERE kind = 'provisional');
DELETE FROM public.care_entry WHERE author_profile_id IN (SELECT id FROM public.profile WHERE kind = 'provisional') OR tank_id IN (SELECT id FROM public.tank WHERE profile_id IN (SELECT id FROM public.profile WHERE kind = 'provisional'));
DELETE FROM public.water_change_session WHERE profile_id IN (SELECT id FROM public.profile WHERE kind = 'provisional') OR tank_id IN (SELECT id FROM public.tank WHERE profile_id IN (SELECT id FROM public.profile WHERE kind = 'provisional'));
DELETE FROM public.care_plan_rule_change WHERE tank_id IN (SELECT id FROM public.tank WHERE profile_id IN (SELECT id FROM public.profile WHERE kind = 'provisional'));
DELETE FROM public.care_plan_rule WHERE tank_id IN (SELECT id FROM public.tank WHERE profile_id IN (SELECT id FROM public.profile WHERE kind = 'provisional'));
DELETE FROM public.comment WHERE author_profile_id IN (SELECT id FROM public.profile WHERE kind = 'provisional') OR tank_id IN (SELECT id FROM public.tank WHERE profile_id IN (SELECT id FROM public.profile WHERE kind = 'provisional')) OR story_id IN (SELECT id FROM public.story WHERE author_profile_id IN (SELECT id FROM public.profile WHERE kind = 'provisional'));
DELETE FROM public.rating WHERE profile_id IN (SELECT id FROM public.profile WHERE kind = 'provisional') OR tank_id IN (SELECT id FROM public.tank WHERE profile_id IN (SELECT id FROM public.profile WHERE kind = 'provisional')) OR story_id IN (SELECT id FROM public.story WHERE author_profile_id IN (SELECT id FROM public.profile WHERE kind = 'provisional'));
DELETE FROM public.follow WHERE follower_profile_id IN (SELECT id FROM public.profile WHERE kind = 'provisional') OR target_profile_id IN (SELECT id FROM public.profile WHERE kind = 'provisional') OR target_tank_id IN (SELECT id FROM public.tank WHERE profile_id IN (SELECT id FROM public.profile WHERE kind = 'provisional'));
DELETE FROM public.slug_alias WHERE story_id IN (SELECT id FROM public.story WHERE author_profile_id IN (SELECT id FROM public.profile WHERE kind = 'provisional'));
DELETE FROM public.collection_entry WHERE story_id IN (SELECT id FROM public.story WHERE author_profile_id IN (SELECT id FROM public.profile WHERE kind = 'provisional'));
DELETE FROM public.collection WHERE profile_id IN (SELECT id FROM public.profile WHERE kind = 'provisional');
DELETE FROM public.discussion_post WHERE author_profile_id IN (SELECT id FROM public.profile WHERE kind = 'provisional');
DELETE FROM public.tank_decommissioning WHERE tank_id IN (SELECT id FROM public.tank WHERE profile_id IN (SELECT id FROM public.profile WHERE kind = 'provisional'))
    OR start_media_item_id IN (SELECT id FROM public.media_item WHERE owner_profile_id IN (SELECT id FROM public.profile WHERE kind = 'provisional')) OR end_media_item_id IN (SELECT id FROM public.media_item WHERE owner_profile_id IN (SELECT id FROM public.profile WHERE kind = 'provisional'));
DELETE FROM public.story WHERE author_profile_id IN (SELECT id FROM public.profile WHERE kind = 'provisional');
DELETE FROM public.media_item WHERE owner_profile_id IN (SELECT id FROM public.profile WHERE kind = 'provisional');
DELETE FROM public.tank WHERE profile_id IN (SELECT id FROM public.profile WHERE kind = 'provisional');
DELETE FROM public.profile WHERE kind = 'provisional';
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
