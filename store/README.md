# Store listings

The Play Console and App Store Connect listings, kept in the repository (#86, GDD 1.5, 25.2, 25.3, 26.7). The layout follows fastlane's metadata folders so the text can be uploaded by a tool later; until then it is pasted into the consoles by hand. `tools/content-validate/src/__tests__/store.test.ts` checks every field's length and fails on gambling words.

| Path | Field | Limit |
|---|---|---|
| `android/<locale>/title.txt` | App name | 30 |
| `android/<locale>/short_description.txt` | Short description | 80 |
| `android/<locale>/full_description.txt` | Full description | 4,000 |
| `ios/<locale>/name.txt` | Name | 30 |
| `ios/<locale>/subtitle.txt` | Subtitle | 30 |
| `ios/<locale>/keywords.txt` | Keywords, comma-separated | 100 |
| `ios/<locale>/promotional_text.txt` | Promotional text | 170 |
| `ios/<locale>/description.txt` | Description | 4,000 |
| `screenshots/android/` | Phone screenshots, 1080 × 1920 | 2 to 8 |
| `screenshots/ios-6.7/` | iPhone 6.7-inch screenshots, 1290 × 2796 | up to 10 |

English (`en-US`) is the default for every storefront at launch; the storefronts in GDD 26.7 (PK, IN, BD, UK, AE, SA, US, CA, AU) take the same text until Urdu and Hindi land (#100).

Naming follows GDD 1.5: "Hulla: Desi Games with Friends". The GDD's subtitle, "Bhabhi, Thulla, Rung & more", waits for Rung: store rules ask that a listing describe what the app does now, so the subtitle is "Thulla & Bhabhi with friends", and the keywords leave out games that aren't in the app yet.

## Screenshots

Five shots: Home, the table on your turn, a private room with the room code, the results with the Bhabhi, and the rules. Make them again after an art change:

```
cd apps/unity/Hulla
HULLA_SNAPSHOT_DIR=/tmp/shots Unity -batchmode -projectPath . -runTests -testPlatform PlayMode \
  -testFilter "Hulla.Screens.Tests.AuditSnapshots"
cd ../../..
python3 tools/store-shots/frame.py /tmp/shots store/screenshots
```

GDD 26.7 also asks for the voice room and the WhatsApp invite. Those need two real phones in a call and the share sheet, so they come from the device session (#36) together with a preview video. The issue's "final art on a mid-tier phone" waits for pass B art (#80).

## Content rating (IARC questionnaire, both stores)

Answer it the same way in Play Console and App Store Connect (GDD 25.3):

| Question | Answer |
|---|---|
| Category | Game; card game |
| Violence, fear, sexuality, nudity, language, drugs, alcohol, tobacco | None |
| Crude humour | None (the "Bhabhi" and "Donkey" titles are a game result, not a joke at anyone) |
| Gambling: real money | No |
| Simulated gambling (betting with virtual items on chance) | No. Coins are never staked on a game's result; they buy cosmetics only |
| Users can interact or communicate | Yes: text chat, voice chat, quick chat and emotes with other players, filtered, with report, mute and block |
| Users can share personal information | No: chat refuses links and phone numbers |
| Location shared with others | No |
| Digital purchases | Yes |
| Unrestricted internet access | No |

Expected ratings: Apple 13+ (GDD 25.3 says 12+, which Apple's 2025 age ratings replaced; players talking to each other sets the floor) and Google Play Teen or Everyone 10+, with "Users Interact" and "In-Game Purchases".

## Google Play data safety

From `docs/legal/data-inventory.md`. Data is encrypted in transit: yes. Users can ask for deletion: yes, in the app and at the link domain's `/delete` page.

| Data type | Collected | Shared | Optional | Purpose |
|---|---|---|---|---|
| Name (display name, handle) | Yes | No | No | App functionality, account management |
| User ids (account id, sign-in ids) | Yes | Yes (ad networks get the Hulla id for rewarded ads) | No | App functionality, account management, advertising |
| Phone number | Yes | Yes (Meta, Twilio, to send codes) | Yes | Account management |
| Other info: birth year | Yes | No | No | App functionality (age limits) |
| In-app messages (text chat) | Yes | No | Yes | App functionality, safety |
| Audio (voice chat) | No (not stored; live only) | — | — | — |
| Contacts | Yes (hashed and matched, not stored) | No | Yes | App functionality (find friends) |
| Purchase history | Yes | No | Yes | App functionality |
| App interactions, crash logs, diagnostics | Yes | No | Yes (consent) | Analytics |
| Device or other ids (advertising id, device id, push token) | Yes | Yes (ad networks) | Partly (ads consent) | Advertising, app functionality |

Families policy: not enrolled; target audience 13 and over, not designed for children.

## App Store privacy labels

- **Data used to track you:** advertising data and device id, only after the tracking prompt (ATT) is allowed.
- **Data linked to you:** contact info (phone number, if linked), user content (chat, gameplay), identifiers (user id, device id), purchases, other data (birth year).
- **Data not linked to you:** usage data and diagnostics (analytics and crash reports carry no user id).
- Account deletion: in the app, Settings > Account > Delete account.

## Console steps (owner)

1. Paste the text and upload the screenshots for each store.
2. File the IARC questionnaire with the answers above and keep the certificates in this folder.
3. Fill the data safety form and the privacy labels with the tables above.
4. Set the privacy policy URL and the deletion URL to the pages on the link domain once they are published (#87).
