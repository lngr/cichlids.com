INSERT INTO profile (id,username,display_name,city,country_code,external_avatar_url,kind,created_at,last_login_at,suspended_until)
SELECT n, 'SyntheticAlice' || n, 'Synthetic Display' || n, 'Synthetic City', 'DE', 'https://avatar.example.invalid/a', 'member', '2020-01-01', '2025-01-01', '2027-01-01' FROM generate_series(1,3) n;
UPDATE profile SET username='SyntheticAlice', display_name='Synthetic Display' WHERE id=1;
INSERT INTO profile_identity(profile_id,provider,subject,created_at) VALUES (1,'oidc','synthetic-subject','2020-01-01');
INSERT INTO species(id,genus,name,display_name,breeding,aggression,intra_aggression,diet,description,created_at) VALUES (1,'Syntheticgenus','testfish','Test fish','unspecified','low','low','omnivore','Taxonomy reference','2020-01-01');
INSERT INTO species_common_name(species_id,name) VALUES (1,'Synthetic fish');
INSERT INTO species_link(species_id,url,label) VALUES (1,'urn:synthetic:taxonomy-reference','Reference');
INSERT INTO tank(id,profile_id,title,description,state,created_at) VALUES (1,1,'SyntheticAlice tank','mail@example.invalid','published','2020-01-01');
INSERT INTO inhabitant(tank_id,species_id,count) VALUES (1,1,5);
INSERT INTO post(id,author_profile_id,tank_id,kind,topic,title,description,state,view_count,rating_count,created_at,updated_at,published_at,delete_reason)
SELECT n,1,1,'single','unknown','SyntheticAlice title','https://personal.example.invalid','published',15,1,'2020-01-01','2020-01-02','2020-01-02','mail@example.invalid' FROM generate_series(1,5) n;
UPDATE post SET state='draft' WHERE id=4;
INSERT INTO media_item(id,owner_profile_id,kind,storage_key,original_filename,content_type,checksum_sha256,created_at)
SELECT n,1,'photo','originals/SyntheticAlice/photo-' || n || '.jpg','SyntheticAlice.jpg','image/jpeg',repeat('a',64),'2020-01-01' FROM generate_series(1,5) n;
UPDATE media_item SET kind='video', content_type='video/mp4' WHERE id=5;
INSERT INTO post_media(post_id,media_item_id) SELECT n,n FROM generate_series(1,5) n;
INSERT INTO media_variant(media_item_id,label,storage_key,width) SELECT n,'thumb','variants/thumb/originals/SyntheticAlice/photo-' || n || '.jpg.jpg',200 FROM generate_series(1,5) n;
INSERT INTO media_variant(media_item_id,label,storage_key,width) SELECT n,'medium','variants/medium/originals/SyntheticAlice/photo-' || n || '.jpg.jpg',800 FROM generate_series(1,5) n;
INSERT INTO media_variant(media_item_id,label,storage_key) VALUES (1,'copy','variants/copy/originals/SyntheticAlice.bmp'),(2,'large','originals/private.jpg');
UPDATE profile SET avatar_media_id=5, profile_image_media_id=5;
UPDATE tank SET main_media_id=5;
INSERT INTO tank_media(tank_id,media_item_id,section) VALUES (1,5,'showcase');
INSERT INTO comment(id,post_id,author_profile_id,poster_name,body,delete_reason,created_at) VALUES (1,1,NULL,'Synthetic Guest','SyntheticAlice mail@example.invalid','https://personal.example.invalid','2020-01-01');
INSERT INTO comment_vote(comment_id,profile_id,value,created_at) VALUES (1,2,1,'2020-01-01');
INSERT INTO rating(post_id,profile_id,stars,created_at) VALUES (1,2,4,'2020-01-01');
INSERT INTO follow(follower_profile_id,target_profile_id,created_at) VALUES (2,1,'2020-01-01');
INSERT INTO post_species(post_id,species_id) VALUES (1,1);
INSERT INTO collection(id,profile_id,title,created_at) VALUES (1,1,'SyntheticAlice collection','2020-01-01');
INSERT INTO collection_entry(collection_id,post_id) VALUES (1,1);
INSERT INTO slug_alias(post_id,value,is_canonical,created_at) VALUES (1,'SyntheticAlice-title',true,'2020-01-01');
INSERT INTO discussion_thread(id,category,title,state,post_count,created_at) VALUES (1,'cichlids','SyntheticAlice forum','open',2,'2020-01-01');
INSERT INTO discussion_post(id,thread_id,poster_name,body,created_at) VALUES (1,1,'Synthetic Guest','https://personal.example.invalid','2020-01-01');
INSERT INTO discussion_post(id,thread_id,author_profile_id,body,created_at) VALUES (2,1,1,'mail@example.invalid','2020-01-01');
INSERT INTO discussion_post_media(discussion_post_id,media_item_id) VALUES (1,5);
INSERT INTO archive.legacy_comment(legacy_id,legacy_target_type,legacy_target_id,poster_name,body,payload,vault_reason,imported_at) VALUES (1,'picture',1,'SyntheticAlice','mail@example.invalid','{"email":"mail@example.invalid"}','synthetic','2020-01-01');
INSERT INTO outbox_event(id,occurred_at,event_type,aggregate_type,aggregate_id,payload,last_error) VALUES ('11111111-1111-1111-1111-111111111111','2020-01-01','synthetic','post','1','{"email":"mail@example.invalid"}','SyntheticAlice');
INSERT INTO webhook_subscription(url,secret,event_types,created_at) VALUES ('https://personal.example.invalid','synthetic-secret',ARRAY['synthetic'],'2020-01-01');
INSERT INTO web_session(session_id_hash,subject,encrypted_access_token,access_token_expires_at,encrypted_refresh_token,encrypted_id_token,csrf_token,created_at,last_seen_at) VALUES ('\x01','SyntheticAlice','synthetic-token','2027-01-01','synthetic-refresh','synthetic-id','synthetic-csrf','2020-01-01','2020-01-01');

UPDATE tank SET gravel='SyntheticAlice mail@example.invalid', plants='SyntheticAlice mail@example.invalid', decoration='SyntheticAlice mail@example.invalid', light='SyntheticAlice mail@example.invalid', light_duration='SyntheticAlice mail@example.invalid', filtration='SyntheticAlice mail@example.invalid', technic='SyntheticAlice mail@example.invalid', water_ph='SyntheticAlice mail@example.invalid', water_kh='SyntheticAlice mail@example.invalid', water_gh='SyntheticAlice mail@example.invalid', water_no2='SyntheticAlice mail@example.invalid', water_no3='SyntheticAlice mail@example.invalid', water_po4='SyntheticAlice mail@example.invalid', water_notes='SyntheticAlice mail@example.invalid', food='SyntheticAlice mail@example.invalid', notes='SyntheticAlice mail@example.invalid', delete_reason='SyntheticAlice mail@example.invalid';
