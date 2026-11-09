"""Stops every VM in the phase 0 project once the billing budget is spent (docs/runbooks/gcp-phase0.md).

Cloud Billing posts the budget's state to a Pub/Sub topic several times a day. The budget counts cost
before credits, so `costAmount` is how much of the free credit is used. At or past the budget, the
running instances are stopped; disks, snapshots and backups stay, so starting them again loses nothing.
"""

import base64
import json
import os

import functions_framework
from google.cloud import compute_v1

PROJECT = os.environ["TARGET_PROJECT"]


@functions_framework.cloud_event
def guard(event):
    data = json.loads(base64.b64decode(event.data["message"]["data"]))
    cost = float(data.get("costAmount", 0))
    budget = float(data.get("budgetAmount", 0))
    print(f"budget check: {cost:.2f} of {budget:.2f}")
    if budget <= 0 or cost < budget:
        return

    instances = compute_v1.InstancesClient()
    for zone, scoped in instances.aggregated_list(project=PROJECT):
        for vm in scoped.instances or []:
            if vm.status == "RUNNING":
                print(f"budget spent: stopping {vm.name} in {zone}")
                instances.stop(project=PROJECT, zone=zone.split("/")[-1], instance=vm.name)
