# Invite and referral links

Shared links look like `https://playhulla.pages.dev/j/K7Q2MX` (a table) and `https://playhulla.pages.dev/r/K7Q2MXAB` (add a friend). Messaging apps make them clickable. Where the app is installed the phone opens it straight away; elsewhere the link page shows the code, an Open Hulla button and the stores.

The site is `infra/cloudflare/links`, plain files on Cloudflare Pages (free):

- `open.html` serves `/j/*` and `/r/*` (`_redirects`).
- `.well-known/assetlinks.json` lets Android open the links in the app (App Links). It lists the SHA-256 fingerprint of every key that signs the app.
- `.well-known/apple-app-site-association` does the same for iOS (Universal Links).

## Set up (once)

1. Create a free Cloudflare account.
2. Workers & Pages → Create → Pages → Connect to Git, and allow the Cloudflare Pages app on `mkhizer77/hulla`.
3. Project name `playhulla`, production branch `main`, no framework, no build command, root directory `infra/cloudflare/links`.
4. Every push to `main` that changes the folder redeploys. Pull requests get preview addresses.

Check after a deploy:

```bash
curl -sI https://playhulla.pages.dev/.well-known/assetlinks.json | grep -i content-type
adb shell pm get-app-links gg.hulla.app
```

The second line should say `playhulla.pages.dev: verified` after installing a build.

## Signing keys

`assetlinks.json` holds the fingerprint of this Mac's debug key, which signs development builds. Add the release upload key when the keystore exists, and Google's app signing key from Play Console (Setup → App signing) once the app is on Play. List a key with `keytool -list -v -keystore <file>`.

## iOS

Universal Links need the Associated Domains capability, which a free personal team cannot sign. Until the paid Apple Developer account exists, iPhones open the link page and its Open Hulla button opens the app through `hulla://`. Then: put the new team id in `apple-app-site-association` and set `InviteLinkXcode.UniversalLinks` to true.

## Moving to our own domain

Add the domain to the Pages project (Custom domains), add it to `InviteLink.Hosts` and point `InviteLink.Host` at it. Keep `playhulla.pages.dev` in the list so links already shared still open the app. Update the `og:image` address in the HTML.

## Later

Installs that start from a link: the store buttons already pass `link=j/CODE` to Google Play's install referrer. Reading it on first launch waits for the Play listing.
