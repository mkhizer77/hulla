# Store rejection response

When Google Play or the App Store rejects a build or flags the listing (GDD 25, 28.5; #91).

1. Read the rejection's guideline reference and keep the full text in `docs/research/store-reviews.md` with the date and build.
2. Classify: metadata only (screenshots, description, age rating, privacy labels) is fixed in the console and resubmitted the same day; a binary issue (crash on review, permissions, login) gets a hotfix build from `main` by the hotfix path in `docs/runbooks/first-week.md`; a policy issue (gambling optics, simulated gambling, loot boxes, age gate) is answered with the facts: no wagering, no cash-out, no chips, no loot boxes, direct purchase and the pass only (GDD 13.1, D2), with the compliance pack of #87 attached.
3. Reply within one working day; never argue, state what changed and where in the build the reviewer can see it.
4. If the rejection repeats, ask for a call where the store offers one, and in parallel prepare the alternative (a feature behind a flag off in that market through `config_set`).
5. Record the outcome and update the pre-submission checklist in CONTRIBUTING ("Releasing") so the next build does not hit it.
