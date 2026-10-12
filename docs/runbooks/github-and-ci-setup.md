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
| `CONTENT_SIGNING_KEY` (M2, #56) | content signing job | RSA-2048 private key in PEM; store it only as an environment secret of a protected environment, never in a checkout. The public key ships in the client. |

## Remove the GDD from the public history (one-time, owner)
The first commit pushed `docs/GDD_Hulla.md`. The index already holds its staged deletion, so run these before any other commit:
1. `git branch backup-pre-rewrite && git commit --amend --no-edit`, then check `git ls-tree -r HEAD --name-only | grep -c GDD_Hulla` prints 0.
2. Settings → Branches → `main`: tick "Allow force pushes" (admins only), save.
3. `git push --force-with-lease origin main`.
4. Untick "Allow force pushes", save. Delete `backup-pre-rewrite` once satisfied.
5. The old commit stays reachable by its SHA on GitHub until support purges it: open a ticket at support.github.com asking to remove unreachable commit `116116e` from `mkhizer77/hulla`.

## Branch protection and CODEOWNERS
`.github/CODEOWNERS` names the owner for generated folders, workflows and ADRs. Leave "require review from code owners" off while the owner is the only reviewer, otherwise nobody can merge.

## Deploys and rollback
The phase 0 box keeps `/opt/hulla/.env` with `NAKAMA_IMAGE=ghcr.io/mkhizer77/hulla/nakama:<sha>` and its compose file uses `image: ${NAKAMA_IMAGE}`. The deploy job rewrites the tag to the commit it built. Rollback is editing that line to the previous SHA and `docker compose up -d`. A Nakama restart ends every running table, so deploy in a quiet window.

## Hardening the box
Replace Nakama's default `socket.server_key`, `runtime.http_key` and console username and password before the first start; bind the console (port 7351) to localhost or put it behind Cloudflare Access; expose only the socket port through Cloudflare; keep Postgres on the compose network with no published port; raise `socket.max_message_size_bytes` to fit the largest table snapshot.

## Keystore and Play App Signing
Enrol the app in Play App Signing at first upload so Google holds the signing key and a lost upload keystore can be reset; keep the upload keystore offline as well.

## Git LFS
CI caches `.git/lfs` keyed on the LFS object list, so a run only downloads objects that changed. Keep large art out of the repository where possible (Addressables built from the asset source) and watch the LFS bandwidth quota on the billing page.

## First runs
- Open a trivial pull request to see `ci` run; all jobs skip until the matching folders exist (M0 adds them).
- Tag `v0.1.0` only after the Unity project exists; the Android job needs `apps/unity/Hulla`.
- Unity edit-mode tests and Android builds use the `unityci/editor` image for 6000.4.9f1 (the tag is set in the workflows).

## Costs
Public repository: hosted runners are free, including macOS. Watch for accidental private forks, which bill minutes.
