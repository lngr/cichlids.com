INSERT INTO profile (id,username,display_name,city,country_code,external_avatar_url,kind,created_at,last_login_at,suspended_until)
SELECT n, 'SyntheticAlice' || n, 'Synthetic Display' || n, 'Synthetic City', 'DE', 'https://avatar.example.invalid/a', 'member', '2020-01-01', '2025-01-01', '2027-01-01' FROM generate_series(1,3) n;
UPDATE profile SET username='SyntheticAlice', display_name='Synthetic Display' WHERE id=1;
INSERT INTO profile_identity(profile_id,provider,subject,created_at) VALUES (1,'oidc','synthetic-subject','2020-01-01');
INSERT INTO species(id,genus,name,display_name,breeding,aggression,intra_aggression,diet,description,created_at) VALUES (1,'Syntheticgenus','testfish','Test fish','unspecified','low','low','omnivore','Taxonomy reference','2020-01-01');
INSERT INTO species_common_name(species_id,name) VALUES (1,'Synthetic fish');
INSERT INTO species_link(species_id,url,label) VALUES (1,'urn:synthetic:taxonomy-reference','Reference');
INSERT INTO tank(id,profile_id,title,description,state,created_at,care_reminders_enabled) VALUES (1,1,'SyntheticAlice tank','mail@example.invalid','published','2020-01-01',true),(2,2,'Second tank',NULL,'published','2020-01-01',true),(3,3,'Third tank',NULL,'published','2020-01-01',false);
INSERT INTO inhabitant(tank_id,species_id,count) VALUES (1,1,5);
INSERT INTO post(id,author_profile_id,tank_id,kind,topic,title,description,state,view_count,rating_count,created_at,updated_at,published_at,delete_reason)
SELECT n,1,1,'single','unknown','SyntheticAlice title','https://personal.example.invalid','published',15,1,'2020-01-01','2020-01-02','2020-01-02','mail@example.invalid' FROM generate_series(1,5) n;
UPDATE post SET state='draft' WHERE id=4;
INSERT INTO media_item(id,owner_profile_id,kind,storage_key,original_filename,content_type,checksum_sha256,created_at)
SELECT n,1,'photo','originals/SyntheticAlice/photo-' || n || '.jpg','SyntheticAlice.jpg','image/jpeg',repeat('a',64),'2020-01-01' FROM generate_series(1,5) n;
UPDATE media_item SET kind='video', content_type='video/mp4' WHERE id=5;
INSERT INTO media_link(media_item_id,context_type,post_id,role,position,caption,created_at) SELECT n,'post',n,'item',0,'SyntheticAlice caption mail@example.invalid','2020-01-01' FROM generate_series(1,5) n;
INSERT INTO media_variant(media_item_id,label,storage_key,width) SELECT n,'thumb','variants/thumb/originals/SyntheticAlice/photo-' || n || '.jpg.jpg',200 FROM generate_series(1,5) n;
INSERT INTO media_variant(media_item_id,label,storage_key,width) SELECT n,'medium','variants/medium/originals/SyntheticAlice/photo-' || n || '.jpg.jpg',800 FROM generate_series(1,5) n;
INSERT INTO media_variant(media_item_id,label,storage_key) VALUES (1,'copy','variants/copy/originals/SyntheticAlice.bmp'),(2,'large','originals/private.jpg');
INSERT INTO media_link(media_item_id,context_type,profile_id,role,created_at) VALUES (1,'profile',1,'avatar','2020-01-01'),(2,'profile',2,'avatar','2020-01-01'),(3,'profile',3,'header','2020-01-01'),(1,'profile',2,'header','2020-01-01');
INSERT INTO media_link(media_item_id,context_type,tank_id,role,position,caption,created_at) VALUES (5,'tank',1,'main',0,NULL,'2020-01-01'),(5,'tank',1,'showcase',0,'SyntheticAlice showcase https://personal.example.invalid','2020-01-01'),(1,'tank',1,'time_lapse',0,'SyntheticAlice mail@example.invalid','2020-01-01');
INSERT INTO tank_decommissioning(tank_id,scope,started_at,start_date,start_media_item_id,ended_at,end_date,end_media_item_id) VALUES
 (1,'all_care','2020-02-01','2020-02-01',5,NULL,NULL,NULL),
 (2,'water_change','2020-02-01','2020-02-01',1,NULL,NULL,NULL),
 (3,'all_care','2020-02-01','2020-02-01',2,'2020-03-01','2020-03-01',3);
INSERT INTO comment(id,post_id,author_profile_id,poster_name,body,delete_reason,created_at) VALUES (1,1,NULL,'Synthetic Guest','SyntheticAlice mail@example.invalid','https://personal.example.invalid','2020-01-01');
INSERT INTO comment_vote(comment_id,profile_id,value,created_at) VALUES (1,2,1,'2020-01-01');
INSERT INTO rating(post_id,profile_id,stars,created_at) VALUES (1,2,4,'2020-01-01');
INSERT INTO follow(follower_profile_id,target_profile_id,created_at) VALUES (2,1,'2020-01-01');
INSERT INTO post_species(post_id,species_id) VALUES (1,1);
INSERT INTO collection(id,profile_id,title,is_public,created_at) VALUES (1,1,'SyntheticAlice collection',true,'2020-01-01'),(2,2,'Private collection',false,'2020-01-01');
INSERT INTO collection_entry(collection_id,post_id) VALUES (1,1),(2,2);
INSERT INTO slug_alias(post_id,value,is_canonical,created_at) VALUES (1,'SyntheticAlice-title',true,'2020-01-01');
INSERT INTO discussion_thread(id,category,title,state,post_count,created_at) VALUES (1,'cichlids','SyntheticAlice forum','open',2,'2020-01-01');
INSERT INTO discussion_post(id,thread_id,poster_name,body,created_at) VALUES (1,1,'Synthetic Guest','https://personal.example.invalid','2020-01-01');
INSERT INTO discussion_post(id,thread_id,author_profile_id,body,created_at) VALUES (2,1,1,'mail@example.invalid','2020-01-01');
INSERT INTO media_link(media_item_id,context_type,discussion_post_id,role,created_at) VALUES (5,'discussion_post',1,'attachment','2020-01-01');
INSERT INTO archive.legacy_comment(legacy_id,legacy_target_type,legacy_target_id,poster_name,body,payload,vault_reason,imported_at) VALUES (1,'picture',1,'SyntheticAlice','mail@example.invalid','{"email":"mail@example.invalid"}','synthetic','2020-01-01');
INSERT INTO outbox_event(id,occurred_at,event_type,aggregate_type,aggregate_id,payload,last_error) VALUES ('11111111-1111-1111-1111-111111111111','2020-01-01','synthetic','post','1','{"email":"mail@example.invalid"}','SyntheticAlice');
INSERT INTO webhook_subscription(url,secret,event_types,created_at) VALUES ('https://personal.example.invalid','synthetic-secret',ARRAY['synthetic'],'2020-01-01');
INSERT INTO web_session(session_id_hash,subject,encrypted_access_token,access_token_expires_at,encrypted_refresh_token,encrypted_id_token,csrf_token,created_at,last_seen_at) VALUES ('\x01','SyntheticAlice','synthetic-token','2027-01-01','synthetic-refresh','synthetic-id','synthetic-csrf','2020-01-01','2020-01-01');

UPDATE tank SET gravel='SyntheticAlice mail@example.invalid', plants='SyntheticAlice mail@example.invalid', decoration='SyntheticAlice mail@example.invalid', light='SyntheticAlice mail@example.invalid', light_duration='SyntheticAlice mail@example.invalid', filtration='SyntheticAlice mail@example.invalid', technic='SyntheticAlice mail@example.invalid', water_ph='SyntheticAlice mail@example.invalid', water_kh='SyntheticAlice mail@example.invalid', water_gh='SyntheticAlice mail@example.invalid', water_no2='SyntheticAlice mail@example.invalid', water_no3='SyntheticAlice mail@example.invalid', water_po4='SyntheticAlice mail@example.invalid', water_notes='SyntheticAlice mail@example.invalid', food='SyntheticAlice mail@example.invalid', notes='SyntheticAlice mail@example.invalid', delete_reason='SyntheticAlice mail@example.invalid';

INSERT INTO water_change_session(id,tank_id,profile_id,started_at,finished_at,duration_seconds) VALUES (1,1,1,'2020-03-01 10:00+00','2020-03-01 10:10+00',600);
INSERT INTO water_change_session_step(session_id,step,state,at) VALUES (1,'drained_photo','done','2020-03-01 10:02+00'),(1,'refilled_photo','skipped','2020-03-01 10:08+00');
INSERT INTO care_entry(id,tank_id,author_profile_id,kind,occurred_at,recorded_at,amount_input_value,amount_input_unit,note,only_me,water_change_session_id,duration_seconds,evidence,evidence_missing) VALUES
 (1,1,1,'water_change','2020-03-01 10:10+00','2020-03-01 10:10+00',30,'percent','SyntheticAlice mail@example.invalid',false,1,600,'partial',ARRAY['refilled_photo']),
 (2,1,1,'measurement','2020-03-02 10:00+00','2020-03-02 10:00+00',NULL,NULL,'https://personal.example.invalid',true,NULL,NULL,NULL,ARRAY[]::text[]);
INSERT INTO care_measurement_value(care_entry_id,parameter,input_value,input_unit,normalized_value,origin) VALUES (2,'ph',7.5,'none',7.5,'meter');
INSERT INTO media_link(media_item_id,context_type,care_entry_id,role,position,caption,created_at) VALUES (2,'care_entry',1,'photo',0,'SyntheticAlice mail@example.invalid','2020-03-01');
INSERT INTO media_link(media_item_id,context_type,water_change_session_id,role,position,caption,created_at) VALUES (3,'water_change_session',1,'evidence',0,'SyntheticAlice mail@example.invalid','2020-03-01');
INSERT INTO care_todo(profile_id,tank_id,care_entry_id,text,created_at,done_at) VALUES (1,1,1,'SyntheticAlice mail@example.invalid','2020-03-01',NULL),(1,1,NULL,'https://personal.example.invalid','2020-03-01','2020-03-02');
INSERT INTO care_plan_rule(tank_id,kind,rhythm_days,enabled,reason_code,reason_parameters_json,updated_at) VALUES (1,'water_change',7,true,'cichlid_default','{"stocking":"mbuna"}','2020-03-01');
INSERT INTO care_plan_rule_change(tank_id,kind,rhythm_days,enabled,at) VALUES (1,'water_change',10,true,'2020-02-01'),(1,'water_change',7,true,'2020-03-01');
INSERT INTO care_reminder_snooze(profile_id,tank_id,kind,due_date,until_date,created_at) VALUES (1,1,'water_change','2020-03-08','2020-03-10','2020-03-08');
INSERT INTO vacation(profile_id,start_date,end_date,created_at) VALUES (1,'2020-04-01','2020-04-10','2020-03-30'),(1,'2020-05-01',NULL,'2020-04-30');
INSERT INTO tank_visibility_consent(tank_id,context,answer,answered_at) VALUES (1,'creation',true,'2020-01-01');
INSERT INTO web_push_subscription(id,profile_id,endpoint,p256dh,auth,time_zone,created_at,failure_count) VALUES (1,1,'https://push.example.invalid/SyntheticAlice','synthetic-p256dh','synthetic-auth','Europe/Berlin','2020-01-01',0);
INSERT INTO web_push_reminder_sent(subscription_id,tank_id,kind,due_date,fire_date,sent_at) VALUES (1,1,'water_change','2020-03-08','2020-03-08','2020-03-08 08:00+00');
INSERT INTO video_upload(owner_profile_id,target,tank_id,original_filename,byte_size,chunk_size,"offset",storage_key,multipart_upload_id,part_e_tags,created_at,expires_at) VALUES (1,'tank',1,'SyntheticAlice.mp4',10,5,5,'private/synthetic/upload','synthetic-multipart',ARRAY['synthetic-etag'],'2020-01-01','2027-01-01');
INSERT INTO video_job(media_item_id,status,attempts,run_after,created_at) VALUES (5,'queued',0,'2020-01-01','2020-01-01');
UPDATE media_item SET captured_in_app=true, taken_at_local='2020-01-01 12:00', taken_at_offset_minutes=60, capture_checked_at='2020-01-02', received_at='2020-01-03';
INSERT INTO tank(id,profile_id,title,state,created_at,care_reminders_enabled,stream_visibility,profile_visibility) VALUES
 (4,1,'Stream private tank','published','2020-01-01',true,'private','public'),
 (5,2,'Private tank','published','2020-01-01',true,'private','private');
INSERT INTO water_change_session(id,tank_id,profile_id,started_at,finished_at,duration_seconds) VALUES
 (2,4,1,'2020-03-01 10:00+00','2020-03-01 10:10+00',600),
 (3,1,1,'2020-03-03 10:00+00','2020-03-03 10:10+00',600);
INSERT INTO care_entry(id,tank_id,author_profile_id,kind,occurred_at,recorded_at,note,only_me,water_change_session_id,duration_seconds,evidence) VALUES
 (3,1,1,'measurement','2020-03-04 10:00+00','2020-03-04 10:00+00','SyntheticAlice public note',false,NULL,NULL,NULL),
 (4,4,1,'measurement','2020-03-02 10:00+00','2020-03-02 10:00+00','SyntheticAlice stream private',false,NULL,NULL,NULL),
 (5,4,1,'water_change','2020-03-01 10:10+00','2020-03-01 10:10+00','SyntheticAlice stream private',false,2,600,'none'),
 (6,5,2,'measurement','2020-03-02 10:00+00','2020-03-02 10:00+00','SyntheticAlice private tank',false,NULL,NULL,NULL),
 (7,1,1,'water_change','2020-03-03 10:10+00','2020-03-03 10:10+00','SyntheticAlice only me',true,3,600,'none');
INSERT INTO care_measurement_value(care_entry_id,parameter,input_value,input_unit,normalized_value) VALUES (3,'ph',7.1,'none',7.1),(4,'ph',7.2,'none',7.2),(6,'ph',7.3,'none',7.3);
INSERT INTO care_plan_rule(tank_id,kind,rhythm_days,enabled,reason_code,updated_at) VALUES (4,'water_change',7,true,'cichlid_default','2020-03-01'),(5,'water_change',7,true,'cichlid_default','2020-03-01');
INSERT INTO care_plan_rule_change(tank_id,kind,rhythm_days,enabled,at) VALUES (4,'water_change',7,true,'2020-03-01'),(5,'water_change',7,true,'2020-03-01');
INSERT INTO streak_freeze(tank_id,care_entry_id,tank_year,period_start,period_end,created_at) VALUES (1,3,1,'2020-03-02','2020-03-08','2020-03-09'),(4,4,1,'2020-03-02','2020-03-08','2020-03-09'),(5,6,1,'2020-03-02','2020-03-08','2020-03-09');
INSERT INTO tank_decommissioning(tank_id,scope,started_at,start_date,start_media_item_id) VALUES (4,'all_care','2020-02-01','2020-02-01',1),(5,'all_care','2020-02-01','2020-02-01',1);
INSERT INTO tank_visibility_consent(tank_id,context,answer,answered_at) VALUES (4,'settings',true,'2020-01-01'),(5,'settings',true,'2020-01-01');
INSERT INTO inhabitant(tank_id,species_id,count) VALUES (4,1,2),(5,1,3);
INSERT INTO media_link(media_item_id,context_type,tank_id,role,position,created_at) VALUES (1,'tank',5,'showcase',0,'2020-01-01'),(2,'tank',4,'showcase',0,'2020-01-01');
INSERT INTO comment(id,tank_id,author_profile_id,body,created_at) VALUES (2,1,2,'public tank comment','2020-01-01'),(3,5,1,'private tank comment','2020-01-01'),(4,4,1,'stream private tank comment','2020-01-01');
INSERT INTO rating(tank_id,profile_id,stars,created_at) VALUES (1,2,5,'2020-01-01'),(5,1,4,'2020-01-01'),(4,2,3,'2020-01-01');
UPDATE media_item SET placed_public=false, placement_pending=true, placement_attempts=2, placement_next_attempt_at='2020-01-05', visibility_generation=7;
