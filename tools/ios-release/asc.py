#!/usr/bin/env python3
"""Small App Store Connect API helper for the iOS release (docs/runbooks/ios-release.md).

Needs ASC_KEY_ID, ASC_ISSUER_ID and ASC_KEY_PATH (the .p8 file). Signs the token with openssl,
so no Python packages are needed.

  asc.py bundle-id gg.hulla.app "Hulla"   register the app id if missing; prints the team id
  asc.py builds gg.hulla.app              list uploaded builds and their processing state
  asc.py capabilities gg.hulla.app [TYPE…]  list the app id's capabilities; switch on the named ones
                                          (ASSOCIATED_DOMAINS, APPLE_ID_AUTH, PUSH_NOTIFICATIONS, …)
"""
import base64
import json
import os
import subprocess
import sys
import time
import urllib.error
import urllib.request

API = "https://api.appstoreconnect.apple.com/v1"


def b64(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode()


def der_to_raw(der: bytes) -> bytes:
    # ECDSA signature: SEQUENCE { INTEGER r, INTEGER s } -> r || s, 32 bytes each.
    def read_int(buf, i):
        assert buf[i] == 0x02
        length = buf[i + 1]
        value = buf[i + 2 : i + 2 + length].lstrip(b"\x00")
        return value.rjust(32, b"\x00"), i + 2 + length

    i = 2 if der[1] < 0x80 else 3
    r, i = read_int(der, i)
    s, _ = read_int(der, i)
    return r + s


def token() -> str:
    header = {"alg": "ES256", "kid": os.environ["ASC_KEY_ID"], "typ": "JWT"}
    now = int(time.time())
    payload = {"iss": os.environ["ASC_ISSUER_ID"], "iat": now, "exp": now + 1200, "aud": "appstoreconnect-v1"}
    signing_input = f"{b64(json.dumps(header).encode())}.{b64(json.dumps(payload).encode())}".encode()
    der = subprocess.run(
        ["openssl", "dgst", "-sha256", "-sign", os.environ["ASC_KEY_PATH"]],
        input=signing_input, capture_output=True, check=True,
    ).stdout
    return f"{signing_input.decode()}.{b64(der_to_raw(der))}"


def call(method: str, path: str, body=None):
    request = urllib.request.Request(
        API + path, method=method,
        data=json.dumps(body).encode() if body is not None else None,
        headers={"Authorization": "Bearer " + token(), "Content-Type": "application/json"},
    )
    try:
        with urllib.request.urlopen(request) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        sys.exit(f"{method} {path}: {error.code} {error.read().decode()}")


def bundle_id(identifier: str, name: str) -> None:
    found = call("GET", f"/bundleIds?filter[identifier]={identifier}")["data"]
    found = [b for b in found if b["attributes"]["identifier"] == identifier]
    if not found:
        body = {"data": {"type": "bundleIds", "attributes": {"identifier": identifier, "name": name, "platform": "IOS"}}}
        found = [call("POST", "/bundleIds", body)["data"]]
        print(f"registered {identifier}")
    print("team id:", found[0]["attributes"]["seedId"])


# Capabilities Apple refuses without a setting: Sign in with Apple as a primary app id.
CAPABILITY_SETTINGS = {
    "APPLE_ID_AUTH": [{"key": "APPLE_ID_AUTH_APP_CONSENT", "options": [{"key": "PRIMARY_APP_CONSENT"}]}],
}


def capabilities(identifier: str, *wanted: str) -> None:
    found = call("GET", f"/bundleIds?filter[identifier]={identifier}&include=bundleIdCapabilities")
    ids = [b for b in found["data"] if b["attributes"]["identifier"] == identifier]
    if not ids:
        sys.exit(f"no app id {identifier}; register it with bundle-id first")
    on = {c["attributes"]["capabilityType"] for c in found.get("included", []) if c["type"] == "bundleIdCapabilities"}
    for capability in wanted:
        if capability in on:
            continue
        body = {"data": {
            "type": "bundleIdCapabilities",
            "attributes": {"capabilityType": capability, "settings": CAPABILITY_SETTINGS.get(capability, [])},
            "relationships": {"bundleId": {"data": {"type": "bundleIds", "id": ids[0]["id"]}}},
        }}
        call("POST", "/bundleIdCapabilities", body)
        on.add(capability)
        print(f"enabled {capability}")
    print("capabilities:", ", ".join(sorted(on)) or "none")


def builds(identifier: str) -> None:
    apps = call("GET", f"/apps?filter[bundleId]={identifier}")["data"]
    if not apps:
        sys.exit(f"no App Store Connect app for {identifier}; create it under Apps first")
    print("app:", apps[0]["id"], apps[0]["attributes"]["name"])
    for build in call("GET", f"/builds?filter[app]={apps[0]['id']}&sort=-uploadedDate&limit=10")["data"]:
        a = build["attributes"]
        print(a["version"], a["processingState"], a["uploadedDate"])


if __name__ == "__main__":
    command, *args = sys.argv[1:] or ["help"]
    {"bundle-id": bundle_id, "builds": builds, "capabilities": capabilities}.get(command, lambda *_: sys.exit(__doc__))(*args)
