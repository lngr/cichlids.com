# Fail-closed plan-diff policy gate (ADR-0014 Layer 3). Evaluates the `resource_changes` of a
# `tofu plan -json` and denies any delete or replace of a critical resource. The default
# decision is deny: a destructive change is allowed only when the resource is explicitly
# classified ephemeral, so an unclassified destroy fails closed. Non-destructive changes
# (create / update / no-op / read) are always allowed.
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

# Resource types whose destruction is explicitly permitted. Empty here: nothing in the protected
# tiers may be destroyed by automation. The ephemeral app tier extends this set when it lands.
ephemeral_type := set()

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
