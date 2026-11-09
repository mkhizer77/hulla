# Compliance pack

The submission checklist of GDD 25.8 (#87), with the evidence for each row. The privacy policy and terms went live on 9 November 2026 at the owner's choice, before the legal review (OQ-053): https://hulla.khizerkhalid.de/privacy/, `/terms/`, `/delete/` and `/support/`.

| Row (GDD 25.8) | Evidence | State |
|---|---|---|
| Privacy policy and terms live | `privacy.md`, `terms.md`, built into `infra/cloudflare/links/privacy/` and `terms/` by `tools/legal-pages/build.py` | Live; legal review open (OQ-053) |
| Account deletion verified | In the app: Settings > Account > Delete account (14-day grace, `server/nakama/src/services/account_deletion.ts`). Without the app: https://hulla.khizerkhalid.de/delete/ | Page live; the app flow to verify on both phones |
| UGC moderation verified | Filters, mutes, blocks, reports with chat, strikes and bans (GDD 24; `docs/runbooks/moderation-escalation.md`) | In the code; staffing in #91 |
| ATT and consent flows verified | Analytics consent prompt and Settings toggle; MAX's consent flow with the iOS tracking text (`ProjectSettings/AppLovinInternalSettings.json`) | In the code; to verify on both phones once the AppLovin account exists |
| IARC questionnaire filed | Answers in `store/README.md` | Owner, in the consoles |
| Screenshots free of gambling language | `store/screenshots`, checked by `store.test.ts` for the text | Done |
| Age gate verified | Birth-year question at first launch; under-13s stopped; under-16 and under-18 limits on the server | In the code; to verify on both phones |
| India legal review done or India geofenced | #88 | Open |
| DPIA complete | `dpia.md` | Drafted; sign-off by the owner |
| Vendor DPAs signed | `dpia.md`, section 6 | Owner |
| Crash-free threshold met | Crashlytics during the beta (#79) | Later |

## Publishing the policies

The pages live on the links site under `hulla.khizerkhalid.de` (a CNAME at IONOS to the Pages project; `playhulla.pages.dev` serves the same files). After editing `privacy.md` or `terms.md`, run `python3 tools/legal-pages/build.py` and Prettier on the two folders; `delete/` and `support/` are hand-written. A push to `main` deploys the site. Support address: hulla.games1@gmail.com. Then set `privacy_url` and `terms_url` in remote config (`config_set`), which shows the two rows in Settings > Privacy, and put the same URLs in both consoles. The ad SDK's consent flow already points at those two paths (`ProjectSettings/AppLovinInternalSettings.json`).

Supporting documents: `data-inventory.md` (what is collected, where, for how long) and `store/README.md` (data safety form and privacy labels).
