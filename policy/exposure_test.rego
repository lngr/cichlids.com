# Story: TASK-2.9
package main

import rego.v1

exposure_plan(kind, state, unknown) := {"resource_changes": [{
	"address": sprintf("%s.exposure", [kind]),
	"type": kind,
	"change": {"actions": ["update"], "before": {}, "after": state, "after_unknown": unknown},
}]}

test_exposure_public_policy_cichlids_db_backup_0 if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_policy", {
			"bucket": "cichlids-db-backup",
			"policy": "{\"Statement\": [{\"Effect\": \"Allow\", \"Principal\": \"*\", \"Action\": [\"s3:GetObject\"], \"Resource\": \"arn:aws:s3:::cichlids-db-backup/*\"}]}",
		},
		{},
	)
}

test_exposure_public_policy_cichlids_db_backup_1 if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_policy", {
			"bucket": "cichlids-db-backup",
			"policy": "{\"Statement\": [{\"Effect\": \"Allow\", \"Principal\": {\"AWS\": \"*\"}, \"Action\": [\"s3:GetObject\"], \"Resource\": \"arn:aws:s3:::cichlids-db-backup/*\"}]}",
		},
		{},
	)
}

test_exposure_public_policy_cichlids_db_backup_2 if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_policy", {
			"bucket": "cichlids-db-backup",
			"policy": "{\"Statement\": [{\"Effect\": \"Allow\", \"Principal\": {\"AWS\": [\"*\"]}, \"Action\": [\"s3:GetObject\"], \"Resource\": \"arn:aws:s3:::cichlids-db-backup/*\"}]}",
		},
		{},
	)
}

test_exposure_public_policy_cichlids_db_backup_3 if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_policy", {
			"bucket": "cichlids-db-backup",
			"policy": "{\"Statement\": [{\"Effect\": \"Allow\", \"Principal\": {\"CanonicalUser\": \"\"}, \"Action\": [\"s3:GetObject\"], \"Resource\": \"arn:aws:s3:::cichlids-db-backup/*\"}]}",
		},
		{},
	)
}

test_exposure_public_policy_cichlids_db_backup_4 if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_policy", {
			"bucket": "cichlids-db-backup",
			"policy": "{\"Statement\": [{\"Effect\": \"Allow\", \"Principal\": {\"AWS\": \"arn:aws:iam::*:root\"}, \"Action\": [\"s3:GetObject\"], \"Resource\": \"arn:aws:s3:::cichlids-db-backup/*\"}]}",
		},
		{},
	)
}

test_exposure_acl_cichlids_db_backup_public_read if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_acl", {
			"bucket": "cichlids-db-backup",
			"acl": "public-read",
		},
		{},
	)
}

test_exposure_acl_cichlids_db_backup_public_read_write if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_acl", {
			"bucket": "cichlids-db-backup",
			"acl": "public-read-write",
		},
		{},
	)
}

test_exposure_acl_cichlids_db_backup_authenticated_read if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_acl", {
			"bucket": "cichlids-db-backup",
			"acl": "authenticated-read",
		},
		{},
	)
}

test_exposure_grant_cichlids_db_backup_AllUsers if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_acl", {
			"bucket": "cichlids-db-backup",
			"access_control_policy": [{"grant": [{
				"grantee": [{
					"type": "Group",
					"uri": "http://acs.amazonaws.com/groups/global/AllUsers",
				}],
				"permission": "READ",
			}]}],
		},
		{},
	)
}

test_exposure_grant_cichlids_db_backup_AuthenticatedUsers if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_acl", {
			"bucket": "cichlids-db-backup",
			"access_control_policy": [{"grant": [{
				"grantee": [{
					"type": "Group",
					"uri": "http://acs.amazonaws.com/groups/global/AuthenticatedUsers",
				}],
				"permission": "READ",
			}]}],
		},
		{},
	)
}

test_exposure_cichlids_db_backup_website_configuration if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_website_configuration", {"bucket": "cichlids-db-backup"},
		{},
	)
}

test_exposure_cichlids_db_backup_cors_configuration if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_cors_configuration", {"bucket": "cichlids-db-backup"},
		{},
	)
}

test_exposure_cichlids_db_backup_block_public_acls if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_public_access_block", {
			"bucket": "cichlids-db-backup",
			"block_public_acls": false,
			"ignore_public_acls": true,
			"block_public_policy": true,
			"restrict_public_buckets": true,
		},
		{},
	)
}

test_exposure_cichlids_db_backup_ignore_public_acls if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_public_access_block", {
			"bucket": "cichlids-db-backup",
			"block_public_acls": true,
			"ignore_public_acls": false,
			"block_public_policy": true,
			"restrict_public_buckets": true,
		},
		{},
	)
}

test_exposure_cichlids_db_backup_block_public_policy if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_public_access_block", {
			"bucket": "cichlids-db-backup",
			"block_public_acls": true,
			"ignore_public_acls": true,
			"block_public_policy": false,
			"restrict_public_buckets": true,
		},
		{},
	)
}

test_exposure_cichlids_db_backup_restrict_public_buckets if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_public_access_block", {
			"bucket": "cichlids-db-backup",
			"block_public_acls": true,
			"ignore_public_acls": true,
			"block_public_policy": true,
			"restrict_public_buckets": false,
		},
		{},
	)
}

test_exposure_private_cichlids_db_backup if {
	count(deny) == 0 with input as exposure_plan(
		"aws_s3_bucket_acl", {
			"bucket": "cichlids-db-backup",
			"acl": "private",
		},
		{},
	)
}

test_exposure_unknown_policy_cichlids_db_backup if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_policy", {
			"bucket": "cichlids-db-backup",
			"policy": null,
		},
		{"policy": true},
	)
}

test_exposure_public_policy_cichlids_media_backup_0 if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_policy", {
			"bucket": "cichlids-media-backup",
			"policy": "{\"Statement\": [{\"Effect\": \"Allow\", \"Principal\": \"*\", \"Action\": [\"s3:GetObject\"], \"Resource\": \"arn:aws:s3:::cichlids-media-backup/*\"}]}",
		},
		{},
	)
}

test_exposure_public_policy_cichlids_media_backup_1 if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_policy", {
			"bucket": "cichlids-media-backup",
			"policy": "{\"Statement\": [{\"Effect\": \"Allow\", \"Principal\": {\"AWS\": \"*\"}, \"Action\": [\"s3:GetObject\"], \"Resource\": \"arn:aws:s3:::cichlids-media-backup/*\"}]}",
		},
		{},
	)
}

test_exposure_public_policy_cichlids_media_backup_2 if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_policy", {
			"bucket": "cichlids-media-backup",
			"policy": "{\"Statement\": [{\"Effect\": \"Allow\", \"Principal\": {\"AWS\": [\"*\"]}, \"Action\": [\"s3:GetObject\"], \"Resource\": \"arn:aws:s3:::cichlids-media-backup/*\"}]}",
		},
		{},
	)
}

test_exposure_public_policy_cichlids_media_backup_3 if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_policy", {
			"bucket": "cichlids-media-backup",
			"policy": "{\"Statement\": [{\"Effect\": \"Allow\", \"Principal\": {\"CanonicalUser\": \"\"}, \"Action\": [\"s3:GetObject\"], \"Resource\": \"arn:aws:s3:::cichlids-media-backup/*\"}]}",
		},
		{},
	)
}

test_exposure_public_policy_cichlids_media_backup_4 if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_policy", {
			"bucket": "cichlids-media-backup",
			"policy": "{\"Statement\": [{\"Effect\": \"Allow\", \"Principal\": {\"AWS\": \"arn:aws:iam::*:root\"}, \"Action\": [\"s3:GetObject\"], \"Resource\": \"arn:aws:s3:::cichlids-media-backup/*\"}]}",
		},
		{},
	)
}

test_exposure_acl_cichlids_media_backup_public_read if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_acl", {
			"bucket": "cichlids-media-backup",
			"acl": "public-read",
		},
		{},
	)
}

test_exposure_acl_cichlids_media_backup_public_read_write if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_acl", {
			"bucket": "cichlids-media-backup",
			"acl": "public-read-write",
		},
		{},
	)
}

test_exposure_acl_cichlids_media_backup_authenticated_read if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_acl", {
			"bucket": "cichlids-media-backup",
			"acl": "authenticated-read",
		},
		{},
	)
}

test_exposure_grant_cichlids_media_backup_AllUsers if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_acl", {
			"bucket": "cichlids-media-backup",
			"access_control_policy": [{"grant": [{
				"grantee": [{
					"type": "Group",
					"uri": "http://acs.amazonaws.com/groups/global/AllUsers",
				}],
				"permission": "READ",
			}]}],
		},
		{},
	)
}

test_exposure_grant_cichlids_media_backup_AuthenticatedUsers if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_acl", {
			"bucket": "cichlids-media-backup",
			"access_control_policy": [{"grant": [{
				"grantee": [{
					"type": "Group",
					"uri": "http://acs.amazonaws.com/groups/global/AuthenticatedUsers",
				}],
				"permission": "READ",
			}]}],
		},
		{},
	)
}

test_exposure_cichlids_media_backup_website_configuration if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_website_configuration", {"bucket": "cichlids-media-backup"},
		{},
	)
}

test_exposure_cichlids_media_backup_cors_configuration if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_cors_configuration", {"bucket": "cichlids-media-backup"},
		{},
	)
}

test_exposure_cichlids_media_backup_block_public_acls if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_public_access_block", {
			"bucket": "cichlids-media-backup",
			"block_public_acls": false,
			"ignore_public_acls": true,
			"block_public_policy": true,
			"restrict_public_buckets": true,
		},
		{},
	)
}

test_exposure_cichlids_media_backup_ignore_public_acls if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_public_access_block", {
			"bucket": "cichlids-media-backup",
			"block_public_acls": true,
			"ignore_public_acls": false,
			"block_public_policy": true,
			"restrict_public_buckets": true,
		},
		{},
	)
}

test_exposure_cichlids_media_backup_block_public_policy if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_public_access_block", {
			"bucket": "cichlids-media-backup",
			"block_public_acls": true,
			"ignore_public_acls": true,
			"block_public_policy": false,
			"restrict_public_buckets": true,
		},
		{},
	)
}

test_exposure_cichlids_media_backup_restrict_public_buckets if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_public_access_block", {
			"bucket": "cichlids-media-backup",
			"block_public_acls": true,
			"ignore_public_acls": true,
			"block_public_policy": true,
			"restrict_public_buckets": false,
		},
		{},
	)
}

test_exposure_private_cichlids_media_backup if {
	count(deny) == 0 with input as exposure_plan(
		"aws_s3_bucket_acl", {
			"bucket": "cichlids-media-backup",
			"acl": "private",
		},
		{},
	)
}

test_exposure_unknown_policy_cichlids_media_backup if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_policy", {
			"bucket": "cichlids-media-backup",
			"policy": null,
		},
		{"policy": true},
	)
}

test_exposure_public_policy_cichlids_media_master_0 if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_policy", {
			"bucket": "cichlids-media-master",
			"policy": "{\"Statement\": [{\"Effect\": \"Allow\", \"Principal\": \"*\", \"Action\": [\"s3:GetObject\"], \"Resource\": \"arn:aws:s3:::cichlids-media-master/*\"}]}",
		},
		{},
	)
}

test_exposure_public_policy_cichlids_media_master_1 if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_policy", {
			"bucket": "cichlids-media-master",
			"policy": "{\"Statement\": [{\"Effect\": \"Allow\", \"Principal\": {\"AWS\": \"*\"}, \"Action\": [\"s3:GetObject\"], \"Resource\": \"arn:aws:s3:::cichlids-media-master/*\"}]}",
		},
		{},
	)
}

test_exposure_public_policy_cichlids_media_master_2 if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_policy", {
			"bucket": "cichlids-media-master",
			"policy": "{\"Statement\": [{\"Effect\": \"Allow\", \"Principal\": {\"AWS\": [\"*\"]}, \"Action\": [\"s3:GetObject\"], \"Resource\": \"arn:aws:s3:::cichlids-media-master/*\"}]}",
		},
		{},
	)
}

test_exposure_public_policy_cichlids_media_master_3 if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_policy", {
			"bucket": "cichlids-media-master",
			"policy": "{\"Statement\": [{\"Effect\": \"Allow\", \"Principal\": {\"CanonicalUser\": \"\"}, \"Action\": [\"s3:GetObject\"], \"Resource\": \"arn:aws:s3:::cichlids-media-master/*\"}]}",
		},
		{},
	)
}

test_exposure_public_policy_cichlids_media_master_4 if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_policy", {
			"bucket": "cichlids-media-master",
			"policy": "{\"Statement\": [{\"Effect\": \"Allow\", \"Principal\": {\"AWS\": \"arn:aws:iam::*:root\"}, \"Action\": [\"s3:GetObject\"], \"Resource\": \"arn:aws:s3:::cichlids-media-master/*\"}]}",
		},
		{},
	)
}

test_exposure_acl_cichlids_media_master_public_read if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_acl", {
			"bucket": "cichlids-media-master",
			"acl": "public-read",
		},
		{},
	)
}

test_exposure_acl_cichlids_media_master_public_read_write if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_acl", {
			"bucket": "cichlids-media-master",
			"acl": "public-read-write",
		},
		{},
	)
}

test_exposure_acl_cichlids_media_master_authenticated_read if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_acl", {
			"bucket": "cichlids-media-master",
			"acl": "authenticated-read",
		},
		{},
	)
}

test_exposure_grant_cichlids_media_master_AllUsers if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_acl", {
			"bucket": "cichlids-media-master",
			"access_control_policy": [{"grant": [{
				"grantee": [{
					"type": "Group",
					"uri": "http://acs.amazonaws.com/groups/global/AllUsers",
				}],
				"permission": "READ",
			}]}],
		},
		{},
	)
}

test_exposure_grant_cichlids_media_master_AuthenticatedUsers if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_acl", {
			"bucket": "cichlids-media-master",
			"access_control_policy": [{"grant": [{
				"grantee": [{
					"type": "Group",
					"uri": "http://acs.amazonaws.com/groups/global/AuthenticatedUsers",
				}],
				"permission": "READ",
			}]}],
		},
		{},
	)
}

test_exposure_cichlids_media_master_website_configuration if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_website_configuration", {"bucket": "cichlids-media-master"},
		{},
	)
}

test_exposure_cichlids_media_master_cors_configuration if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_cors_configuration", {"bucket": "cichlids-media-master"},
		{},
	)
}

test_exposure_cichlids_media_master_block_public_acls if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_public_access_block", {
			"bucket": "cichlids-media-master",
			"block_public_acls": false,
			"ignore_public_acls": true,
			"block_public_policy": true,
			"restrict_public_buckets": true,
		},
		{},
	)
}

test_exposure_cichlids_media_master_ignore_public_acls if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_public_access_block", {
			"bucket": "cichlids-media-master",
			"block_public_acls": true,
			"ignore_public_acls": false,
			"block_public_policy": true,
			"restrict_public_buckets": true,
		},
		{},
	)
}

test_exposure_private_cichlids_media_master if {
	count(deny) == 0 with input as exposure_plan(
		"aws_s3_bucket_acl", {
			"bucket": "cichlids-media-master",
			"acl": "private",
		},
		{},
	)
}

test_exposure_unknown_policy_cichlids_media_master if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_policy", {
			"bucket": "cichlids-media-master",
			"policy": null,
		},
		{"policy": true},
	)
}

test_exposure_media_root if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_policy", {
			"bucket": "cichlids-media-master",
			"policy": "{\"Statement\": [{\"Effect\": \"Allow\", \"Principal\": \"*\", \"Action\": [\"s3:GetObject\"], \"Resource\": \"arn:aws:s3:::cichlids-media-master/*\"}]}",
		},
		{},
	)
}

test_exposure_media_originals if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_policy", {
			"bucket": "cichlids-media-master",
			"policy": "{\"Statement\": [{\"Effect\": \"Allow\", \"Principal\": \"*\", \"Action\": [\"s3:GetObject\"], \"Resource\": \"arn:aws:s3:::cichlids-media-master/originals/*\"}]}",
		},
		{},
	)
}

test_exposure_media_list if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_policy", {
			"bucket": "cichlids-media-master",
			"policy": "{\"Statement\": [{\"Effect\": \"Allow\", \"Principal\": \"*\", \"Action\": \"s3:ListBucket\", \"Resource\": [\"arn:aws:s3:::cichlids-media-master/variants/*\", \"arn:aws:s3:::cichlids-media-master/forum_attachments/*\"]}]}",
		},
		{},
	)
}

test_exposure_media_wildcard_action if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_policy", {
			"bucket": "cichlids-media-master",
			"policy": "{\"Statement\": [{\"Effect\": \"Allow\", \"Principal\": \"*\", \"Action\": \"s3:*\", \"Resource\": [\"arn:aws:s3:::cichlids-media-master/variants/*\", \"arn:aws:s3:::cichlids-media-master/forum_attachments/*\"]}]}",
		},
		{},
	)
}

test_exposure_media_not_action if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_policy", {
			"bucket": "cichlids-media-master",
			"policy": "{\"Statement\": [{\"Effect\": \"Allow\", \"Principal\": \"*\", \"Action\": [\"s3:GetObject\"], \"Resource\": [\"arn:aws:s3:::cichlids-media-master/variants/*\", \"arn:aws:s3:::cichlids-media-master/forum_attachments/*\"], \"NotAction\": \"s3:DeleteObject\"}]}",
		},
		{},
	)
}

test_exposure_media_not_principal if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_policy", {
			"bucket": "cichlids-media-master",
			"policy": "{\"Statement\": [{\"Effect\": \"Allow\", \"Principal\": \"*\", \"Action\": [\"s3:GetObject\"], \"Resource\": [\"arn:aws:s3:::cichlids-media-master/variants/*\", \"arn:aws:s3:::cichlids-media-master/forum_attachments/*\"], \"NotPrincipal\": {\"AWS\": \"writer\"}}]}",
		},
		{},
	)
}

test_exposure_media_not_resource if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_policy", {
			"bucket": "cichlids-media-master",
			"policy": "{\"Statement\": [{\"Effect\": \"Allow\", \"Principal\": \"*\", \"Action\": [\"s3:GetObject\"], \"Resource\": [\"arn:aws:s3:::cichlids-media-master/variants/*\", \"arn:aws:s3:::cichlids-media-master/forum_attachments/*\"], \"NotResource\": \"originals/*\"}]}",
		},
		{},
	)
}

test_exposure_public_variants if {
	count(deny) == 0 with input as exposure_plan(
		"aws_s3_bucket_policy", {
			"bucket": "cichlids-media-master",
			"policy": "{\"Statement\": [{\"Effect\": \"Allow\", \"Principal\": \"*\", \"Action\": [\"s3:GetObject\"], \"Resource\": [\"arn:aws:s3:::cichlids-media-master/variants/*\", \"arn:aws:s3:::cichlids-media-master/forum_attachments/*\"]}]}",
		},
		{},
	)
}

test_exposure_public_aws_variants if {
	count(deny) == 0 with input as exposure_plan(
		"aws_s3_bucket_policy", {
			"bucket": "cichlids-media-master",
			"policy": "{\"Statement\": [{\"Effect\": \"Allow\", \"Principal\": {\"AWS\": \"*\"}, \"Action\": [\"s3:GetObject\"], \"Resource\": [\"arn:aws:s3:::cichlids-media-master/variants/*\", \"arn:aws:s3:::cichlids-media-master/forum_attachments/*\"]}]}",
		},
		{},
	)
}

test_exposure_private_writer if {
	count(deny) == 0 with input as exposure_plan(
		"aws_s3_bucket_policy", {
			"bucket": "cichlids-db-backup",
			"policy": "{\"Statement\": [{\"Effect\": \"Allow\", \"Principal\": {\"AWS\": \"arn:aws:iam:::user/p123:writer\"}, \"Action\": [\"s3:ListBucket\"], \"Resource\": \"arn:aws:s3:::cichlids-db-backup\"}]}",
		},
		{},
	)
}

test_exposure_malformed_policy if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_policy", {
			"bucket": "cichlids-db-backup",
			"policy": "not JSON",
		},
		{},
	)
}

test_exposure_unknown_acl if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_acl", {"bucket": "cichlids-media-master"},
		{"acl": true},
	)
}

test_exposure_unknown_block if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket_public_access_block", {"bucket": "cichlids-db-backup"},
		{"block_public_acls": true},
	)
}

test_exposure_public_inline_acl if {
	count(deny) > 0 with input as exposure_plan(
		"aws_s3_bucket", {
			"bucket": "cichlids-media-master",
			"acl": "public-read",
		},
		{},
	)
}
