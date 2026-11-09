# Data protection impact assessment

> Draft for the owner's review (#87, GDD 25.4, 25.5). It follows the ICO's DPIA template headings. Facts come from `docs/legal/data-inventory.md`.

## 1. Why a DPIA

Hulla processes the data of players from 13 years old, carries live voice between players who may not know each other, combines data from sign-in providers and contacts, and is offered in the UK, the EU, India, Pakistan and Bangladesh. Processing children's data in an online service and large-scale processing of communications are on the ICO's list of processing likely to be high risk, so a DPIA is done before launch.

## 2. The processing

- **Nature:** a mobile game with an account per device (guest) that can be linked to Google, Facebook, Apple or a phone number; friends, presence, text and voice chat at tables; server-authoritative rounds with a fair-deal log; purchases; rewarded and interstitial ads; consented analytics and crash reports; reporting and moderation.
- **Scope:** the categories and retention in the data inventory. No special category data is collected on purpose; voice is not stored. Text chat may contain anything a player types, which is why it is filtered, kept 7 days and read only through a report.
- **Context:** players are friends and family, mostly in South Asia and the diaspora, from 13 years old. Players expect to play with people they know; Quick Play and Ranked seat strangers.
- **Purposes:** running the game, keeping players safe, fair play, payments, improving the game (with consent), funding it through ads.

## 3. Consultation

Players: the closed beta (#79) asks about the age question, the voice defaults and the privacy settings. Processors: the vendors in the privacy policy, under their data processing terms (to sign: section 6). Legal review: [reviewer, date].

## 4. Necessity and proportionality

- Guest play needs no personal data. Linking is optional.
- Only the birth year is asked, not a date of birth; it sets the age band and nothing else.
- Phone numbers and one-time codes are stored only as hashes; contacts are hashed, matched and dropped.
- Analytics carry no user id and run only after consent. Personalised ads need consent and are never shown under 18.
- Voice runs only at a table, only while two microphones are on, and is never recorded; spectators only listen.
- Retention is set per category and enforced by scheduled purges.
- Players can export and delete everything in the app.

## 5. Risks and measures

| Risk | Likelihood | Severity | Measures | Residual |
|---|---|---|---|---|
| A child under 13 plays | Possible | Significant | Neutral birth-year question at first launch; under-13s are stopped; a reported under-age account is closed ("Seems under age" report reason) | Low |
| A teenager is contacted or groomed by a stranger | Possible | Severe | Under 16: no voice and no free text with players who aren't friends; quick chat and emotes only. Spectators must be a seated player's friend. Block and report everywhere; reports carry the chat | Medium: needs the moderation runbook staffed (#91) |
| Abuse in voice chat | Likely | Significant | Push-to-talk outside private rooms; per-player mute; block; report. Voice is not recorded, so a voice report rests on the reporter's account and the table's other signals | Medium: the 30-second report buffer (GDD 25.5) is not built; if it is, this DPIA is updated first |
| Abuse or doxxing in text chat | Likely | Significant | Filters in six scripts; links and phone numbers refused; 140 characters; 2-second rate limit; mutes; auto-mute after reports; guests and under-16s can't free-text strangers | Low |
| A leak of the database | Unlikely | Significant | No passwords, no raw phone numbers, no payment data, no exact birth dates; access limited to the owner; encrypted connections; backups encrypted at rest (#90) | Low |
| Tracking or profiling without consent | Possible | Moderate | Firebase, the advertising id and Meta's events are off at build time and turn on with consent; MAX's consent flow and ATT; no personalised ads under 18 | Low |
| Data kept too long | Possible | Moderate | Purges for deleted accounts, chat (7 days), round logs (180 days), reports (1 year), phone-code counters (1 day) | Low once the cron sidecar runs (#76) |
| Transfers outside the UK and EU | Certain | Moderate | Standard contractual clauses in the vendors' terms | Low |
| India: under-18s are children under the DPDP Act and need verifiable parental consent | Certain for Indian teens | Significant | Open (GDD 25.4, #88): either an 18+ gate for Indian accounts at launch or a parental-consent flow; until legal review decides, India stays out of the soft launch | Open |

## 6. Actions before launch

- [ ] Sign or accept the data processing terms of: the hosting provider, Cloudflare, Google (Firebase), AppLovin, Meta (WhatsApp Cloud API), Twilio.
- [ ] Decide the India approach (#88) and update the age gate.
- [ ] Run the purge jobs from the box's cron sidecar (#76) and record the first run.
- [ ] Publish the privacy policy and terms on the link domain and set the URLs in both consoles and in the ad SDK's consent flow.
- [ ] UK Online Safety Act: the illegal-content risk assessment and the children's access and risk assessments (#133, GDD 25.9).

## 7. Sign-off

| Item | Name | Date |
|---|---|---|
| Measures approved by | [owner] | |
| Residual risks accepted by | [owner] | |
| DPO or legal advice | [reviewer] | |
| Review date | before the UK open test, and whenever a new data type or vendor is added | |
