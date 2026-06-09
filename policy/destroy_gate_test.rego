# Conftest unit tests for the fail-closed plan-diff gate (ADR-0014 Layer 3). Run with
# `conftest verify policy/`. Written test-first: with no policy rule present, deny is undefined
# and every test below fails (red); the rules in destroy_gate.rego make them pass (green).
package main

import rego.v1

# A delete of a critical resource (by type) must be denied.
test_deny_delete_critical_by_type if {
	count(deny) > 0 with input as {"resource_changes": [
		{
			"address": "hcloud_volume.database",
			"type": "hcloud_volume",
			"change": {"actions": ["delete"], "before": {"id": "x"}, "after": null},
		},
	]}
}

# A replace (delete+create) of a resource marked critical="true" must be denied.
test_deny_replace_critical_by_label if {
	count(deny) > 0 with input as {"resource_changes": [
		{
			"address": "aws_s3_bucket.database_backup",
			"type": "aws_s3_bucket",
			"change": {
				"actions": ["delete", "create"],
				"before": {"tags": {"critical": "true"}},
				"after": {"tags": {"critical": "true"}},
			},
		},
	]}
}

# Creating or updating non-critical resources must be allowed (no deny).
test_allow_create_and_update_noncritical if {
	count(deny) == 0 with input as {"resource_changes": [
		{
			"address": "kubernetes_namespace.app",
			"type": "kubernetes_namespace",
			"change": {"actions": ["create"], "before": null, "after": {}},
		},
		{
			"address": "kubernetes_config_map.app",
			"type": "kubernetes_config_map",
			"change": {"actions": ["update"], "before": {}, "after": {}},
		},
	]}
}

# A destroy of an unclassified resource (neither critical nor explicitly ephemeral) defaults to
# deny (fail-closed).
test_deny_unclassified_destroy if {
	count(deny) > 0 with input as {"resource_changes": [
		{
			"address": "some_provider_thing.x",
			"type": "some_provider_thing",
			"change": {"actions": ["delete"], "before": {"id": "x"}, "after": null},
		},
	]}
}

# A no-op / read change is never destructive and must be allowed.
test_allow_noop if {
	count(deny) == 0 with input as {"resource_changes": [
		{
			"address": "hcloud_volume.database",
			"type": "hcloud_volume",
			"change": {"actions": ["no-op"], "before": {"id": "x"}, "after": {"id": "x"}},
		},
	]}
}
