# Fail-closed plan-diff policy gate (ADR-0014 Layer 3). Evaluates the `resource_changes` of a
# `tofu plan -json` and denies any delete or replace of a critical resource. The default
# decision is deny: a destructive change is allowed only when the resource is explicitly
# classified ephemeral, so an unclassified destroy fails closed. Non-destructive changes
# are subject to bucket exposure checks.
#
# Critical = a resource of a protected platform/backup type, OR one carrying the label/tag
# critical = "true" (ADR-0014). Run with `conftest test <plan.json>`; unit tests in
# destroy_gate_test.rego.
package main

import rego.v1

# Resource types that are always critical: the protected platform and backup tiers, namely the
# cluster node and its disk, the database volume, the object-storage buckets, the DNS zone.
critical_type := {
	"hcloud_server",
	"hcloud_volume",
	"aws_s3_bucket",
	"hcloud_zone",
}

# Resource types whose destruction is explicitly permitted. The app tier consists of
# terraform_data resources that install and upgrade software on the running node; replacing one
# re-runs its provisioners and removes nothing in the cloud. Nothing in the protected tiers may
# be destroyed by automation.
ephemeral_type := {"terraform_data"}

destructive(change) if "delete" in change.actions

critical(rc) if rc.type in critical_type

critical(rc) if marked_critical(rc.change.before)

critical(rc) if marked_critical(rc.change.after)

# A planned state value (before/after) carries the critical marker as a label or a tag. Guarded
# with is_object so a null side of the diff (a pure create or delete) is simply not critical
# here rather than a type error.
marked_critical(state) if {
	is_object(state)
	object.get(state, ["labels", "critical"], "") == "true"
}

marked_critical(state) if {
	is_object(state)
	object.get(state, ["tags", "critical"], "") == "true"
}

# Deny: a destructive change to a critical resource.
deny contains msg if {
	some rc in input.resource_changes
	destructive(rc.change)
	critical(rc)
	msg := sprintf("critical resource %s would be destroyed (actions %v): denied (ADR-0014 Layer 3)", [rc.address, rc.change.actions])
}

# Deny (fail-closed): a destructive change to a resource that is neither proven critical nor
# explicitly classified ephemeral. An unclassified destroy defaults to deny.
deny contains msg if {
	some rc in input.resource_changes
	destructive(rc.change)
	not critical(rc)
	not rc.type in ephemeral_type
	msg := sprintf("unclassified resource %s would be destroyed (actions %v): default deny (ADR-0014 Layer 3)", [rc.address, rc.change.actions])
}

# Exposure checks cover every S3 bucket; only the named media master has public prefixes.
exposure_types := {
	"aws_s3_bucket_policy", "aws_s3_bucket_acl", "aws_s3_bucket_public_access_block",
	"aws_s3_bucket_website_configuration", "aws_s3_bucket_cors_configuration", "aws_s3_bucket",
}

as_array(value) := value if is_array(value)
as_array(value) := [value] if not is_array(value)

media_master(state) if state.bucket == "cichlids-media-master"

anonymous(principal) if {
	walk(principal, pair)
	is_string(pair[1])
	contains(pair[1], "*")
}

anonymous(principal) if {
	walk(principal, pair)
	pair[1] == ""
}

public_media_statement(statement, state) if {
	media_master(state)
	statement.Effect == "Allow"
	as_array(statement.Action) == ["s3:GetObject"]
	resources := as_array(statement.Resource)
	count(resources) > 0
	every resource in resources {
		resource in {
			"arn:aws:s3:::cichlids-media-master/variants/*",
			"arn:aws:s3:::cichlids-media-master/forum_attachments/*",
		}
	}
}

principal_value(value) if is_string(value)
principal_value(value) if is_object(value)

valid_statement(statement) if {
	is_object(statement)
	statement.Effect in {"Allow", "Deny"}
	principal_value(statement.Principal)
	count(as_array(statement.Action)) > 0
	count(as_array(statement.Resource)) > 0
	object.get(statement, "NotPrincipal", null) == null
	object.get(statement, "NotAction", null) == null
	object.get(statement, "NotResource", null) == null
}

valid_policy(state) if {
	is_string(state.policy)
	json.is_valid(state.policy)
	document := json.unmarshal(state.policy)
	statements := as_array(document.Statement)
	count(statements) > 0
	every statement in statements {
		valid_statement(statement)
	}
}

unsafe_exposure("aws_s3_bucket_policy", state) if not valid_policy(state)

unsafe_exposure("aws_s3_bucket_policy", state) if {
	valid_policy(state)
	document := json.unmarshal(state.policy)
	some statement in as_array(document.Statement)
	anonymous(statement.Principal)
	not public_media_statement(statement, state)
}

unsafe_exposure("aws_s3_bucket_acl", state) if {
	object.get(state, "acl", "private") != "private"
}

unsafe_exposure("aws_s3_bucket_acl", state) if {
	walk(object.get(state, "access_control_policy", []), pair)
	is_string(pair[1])
	pair[1] in {
		"http://acs.amazonaws.com/groups/global/AllUsers",
		"http://acs.amazonaws.com/groups/global/AuthenticatedUsers",
	}
}

unsafe_exposure("aws_s3_bucket_public_access_block", state) if {
	some flag in {"block_public_acls", "ignore_public_acls"}
	object.get(state, flag, false) != true
}

unsafe_exposure("aws_s3_bucket_public_access_block", state) if {
	not media_master(state)
	some flag in {"block_public_policy", "restrict_public_buckets"}
	object.get(state, flag, false) != true
}

unsafe_exposure(kind, _) if {
	kind in {"aws_s3_bucket_website_configuration", "aws_s3_bucket_cors_configuration"}
}

unsafe_exposure("aws_s3_bucket", state) if {
	object.get(state, "acl", "private") != "private"
}

unsafe_exposure("aws_s3_bucket", state) if {
	some field in {"website", "cors_rule", "policy"}
	value := object.get(state, field, null)
	value != null
	value != []
	value != ""
}

deny contains msg if {
	some rc in input.resource_changes
	rc.type in exposure_types
	is_object(rc.change.after)
	unsafe_exposure(rc.type, rc.change.after)
	msg := sprintf("bucket access configuration %s risks public exposure: denied", [rc.address])
}

# Unknown access values cannot establish the privacy invariant before apply.
deny contains msg if {
	some rc in input.resource_changes
	rc.type in exposure_types
	is_object(rc.change.after)
	unknown := object.get(rc.change, "after_unknown", {})
	some field in {"policy", "acl", "access_control_policy", "block_public_acls", "ignore_public_acls", "block_public_policy", "restrict_public_buckets", "website", "cors_rule"}
	walk(object.get(unknown, field, false), pair)
	pair[1] == true
	msg := sprintf("bucket access configuration %s has unknown %s: denied", [rc.address, field])
}
