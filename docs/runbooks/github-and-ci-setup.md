# GitHub and CI setup

One-time steps for the owner. Nothing here is automated on purpose.

## Repository
1. Inside `hulla/`: `git init -b main`, commit the documents, then `gh repo create hulla --public --source=. --push` (or create it in the web UI and add the remote).
2. Replace `OWNER` in `NOTICE` with the GitHub account name.
3. Settings → Branches → protect `main`: require a pull request, require the `ci` status check, require linear history, allow squash merges only.
4. Settings → Actions → allow GitHub Actions; keep the default `GITHUB_TOKEN` permissions read-only (workflows request what they need).

## Secrets (Settings → Secrets and variables → Actions)
| Secret | Used by | How to get it |
|---|---|---|
| `UNITY_LICENSE` | ci.yml, release-android.yml | Contents of the `.ulf` licence file for Unity 6000.4.9f1. Run the GameCI activation workflow once to produce the `.alf`, upload it at license.unity3d.com/manual, download the `.ulf`, paste its contents. Repeat when the editor version changes. |
| `UNITY_EMAIL`, `UNITY_PASSWORD` | same | The Unity account the licence belongs to. |
| `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASS`, `ANDROID_KEYALIAS_NAME`, `ANDROID_KEYALIAS_PASS` | release-android.yml | `keytool -genkey` once, keep the keystore offline, base64-encode it for the secret. Losing it means losing the ability to update the app on Google Play. |
| `PHASE0_HOST`, `PHASE0_USER`, `PHASE0_SSH_KEY` | server-image.yml | The phase 0 box and a deploy-only SSH key. Create the `phase0` environment with required reviewers so deploys need a manual approval. |

## First runs
- Open a trivial pull request to see `ci` run; all jobs skip until the matching folders exist (M0 adds them).
- Tag `v0.1.0` only after the Unity project exists; the Android job needs `apps/unity/Hulla`.
- Unity edit-mode tests and Android builds use the `unityci/editor` image for 6000.4.9f1 (the tag is set in the workflows).

## Costs
Public repository: hosted runners are free, including macOS. Watch for accidental private forks, which bill minutes.
