# Compliance pack

The submission checklist of GDD 25.8 (#87), with the evidence for each row. The legal texts here are drafts for the owner's legal review; they go live on the link domain only after that review.

| Row (GDD 25.8) | Evidence | State |
|---|---|---|
| Privacy policy and terms live | `privacy.md`, `terms.md`; the pages on the link domain after review | Drafted; publishing waits for review |
| Account deletion verified | In the app: Settings > Account > Delete account (14-day grace, `server/nakama/src/services/account_deletion.ts`). Without the app: the link domain's `/delete` page | In the code; to verify on both phones |
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

After the legal review: fill the square brackets, turn `privacy.md` and `terms.md` into pages at `infra/cloudflare/links/privacy/` and `infra/cloudflare/links/terms/`, and add a `delete/` page that explains deletion in the app and gives the support address for a request without it (Google Play and Meta both ask for a web route). A push to `main` deploys the site. Then set `privacy_url` and `terms_url` in remote config (`config_set`), which shows the two rows in Settings > Privacy, and put the same URLs in both consoles. The ad SDK's consent flow already points at those two paths (`ProjectSettings/AppLovinInternalSettings.json`).

Supporting documents: `data-inventory.md` (what is collected, where, for how long) and `store/README.md` (data safety form and privacy labels).
