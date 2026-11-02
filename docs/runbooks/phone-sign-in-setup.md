# Phone sign-in setup

Phone OTP sign-in (GDD 10.2, #99) sends codes on WhatsApp first and by SMS when WhatsApp is not set up or refuses the message (`server/nakama/src/services/otp_providers.ts`, OQ-041).

- **WhatsApp: Meta's Cloud API, direct.** The server makes the code, keeps only a peppered hash of it and sends Meta's authentication template. Meta charges its authentication rate per message, a fraction of an SMS in most of our markets, and there is no per-verification fee.
- **SMS: Twilio Verify.** Twilio makes, sends and checks the code.

Why not Twilio Verify for both (it takes `Channel=whatsapp`)? One vendor is simpler, but Verify adds its fee of about US$0.05 to every WhatsApp code on top of Meta's rate. That fee costs more than the WhatsApp message itself, so direct Meta is the cheaper path and Twilio stays for SMS only.

## What the server needs
| Variable | What it is |
|---|---|
| `HULLA_PHONE_PEPPER` | A long random secret, set once and never changed. Numbers and codes are stored only as hashes with it; changing it unlinks every phone. Keep a copy in the password manager. |
| `WHATSAPP_TOKEN` | A permanent system-user access token with `whatsapp_business_messaging`. |
| `WHATSAPP_PHONE_NUMBER_ID` | The id of the sending number (not the number itself). |
| `WHATSAPP_TEMPLATE` | The approved authentication template's name, for example `hulla_code`. |
| `WHATSAPP_TEMPLATE_LANGUAGE` | Optional. The template's language code; `en` by default. |
| `WHATSAPP_API_VERSION` | Optional. The Graph API version; `v24.0` by default. Move it forward before Meta retires it. |
| `TWILIO_ACCOUNT_SID`, `TWILIO_AUTH_TOKEN` | The Twilio account. |
| `TWILIO_VERIFY_SID` | A Verify service (`VA…`) with the SMS channel on and a code length of 6. |

Put them in the Nakama runtime env (`runtime.env` in the server config, from the box's secrets file), then restart Nakama. Either channel alone works: WhatsApp only means no "send by SMS instead"; SMS only means every code goes by SMS. Without either the RPCs answer `auth.otp_unavailable`. On a local box (`HULLA_ENV=local`) the local providers need nothing: nothing is sent, the code is the number's last six digits, and a number ending in 9 fails on WhatsApp so the fallback to SMS can be tried.

## Setting up WhatsApp (owner, Meta consoles)
1. **Business portfolio.** In Meta Business Suite, create or pick the business portfolio and start business verification (legal name, address, a document). Unverified portfolios can send only to a few test numbers.
2. **App.** In the Meta for Developers console, create an app of type Business and add the WhatsApp product. Link it to the portfolio.
3. **WhatsApp Business account and number.** Under WhatsApp → API Setup, add a phone number that is not on the WhatsApp app (a new SIM or a virtual number), verify it by SMS or call, and set its display name to Hulla. Copy the phone number id into `WHATSAPP_PHONE_NUMBER_ID`.
4. **Payment method.** In WhatsApp Manager → Payment settings, add a card. Authentication messages are billed per message delivered.
5. **Template.** In WhatsApp Manager → Message templates, create a template of category **Authentication**, name `hulla_code`, language English, with the **Copy code** button, the security recommendation on, and an expiry of 10 minutes (the server's code life). Submit it; authentication templates are usually approved within minutes. Add more languages to the same template later; set `WHATSAPP_TEMPLATE_LANGUAGE` to match.
6. **Token.** In Business Settings → Users → System users, create an admin system user, assign it the app and the WhatsApp account with full control, and generate a token that never expires with `whatsapp_business_messaging` and `whatsapp_business_management`. Put it in `WHATSAPP_TOKEN`. Never use the 24-hour test token in production.

## Setting up SMS (owner, Twilio console)
Create a Verify service with the SMS channel on and a code length of 6; turn on Fraud Guard and the geo permissions for the countries in `OTP_COUNTRIES` only.

## What the client sees
- `phone_start {"phone", "channel"?}`. Without a channel the server tries WhatsApp, then SMS in the same call. `"channel": "sms"` sends by SMS only, for "Send by SMS instead". Each call is one send against the resend wait and every limit below.
- The answer carries `channel` (`whatsapp` or `sms`, for "Sent on WhatsApp" or "Sent by SMS"), `channels` (what the server can send on, so the client offers the SMS button only when it holds `sms`) and `fallback` (WhatsApp was tried and failed).
- Meta accepts a message before it knows the number is on WhatsApp, so a number without WhatsApp shows "Sent on WhatsApp" and nothing arrives. The way out is "Send by SMS instead" once `resendAfterTs` has passed.
- A code is checked on the channel it went out on; after an SMS resend only the SMS code works.

## Limits and cost
- Per number: one code a minute, three per 15 minutes, six a day. Per account: five an hour, ten a day. Per address: ten an hour. Five wrong guesses lock a code; a code lasts 10 minutes. A send that falls back from WhatsApp to SMS counts once.
- Only the calling codes in `OTP_COUNTRIES` are served (the markets of GDD 1.4); premium destinations used for SMS pumping are refused.
- `otp_daily_budget` in remote config caps the sends per day across everyone (2,000 by default). Setting it to 0 stops all sends at once.
- The dashboard's phone panels show codes by country and channel, failed WhatsApp sends by reason, estimated spend by country and by channel, refusals by reason and sign-ins. Spend is counted in millionths of a US dollar (`hulla_otp_cost_microusd`). Two alerts: spend above US$20 in an hour, and fewer than 30% of codes verified (likely pumping). The cents per country and channel in `OTP_COUNTRIES` are estimates until the contracts set prices; update them from Meta's rate card and Twilio's price list.

## Checking it
1. On the phase 0 box, with the variables set: `phone_start {"phone": "+92…"}` from a signed-in guest answers `"channel": "whatsapp"` and the message arrives with a copy-code button; `phone_verify {"phone", "code"}` answers `linked`.
2. After a minute, `phone_start {"phone", "channel": "sms"}` answers `"channel": "sms"` and an SMS arrives.
3. A second device: start and verify answer `signIn` with a token; `authenticateCustom(token, create=false)` signs in to the first account.
4. `phone_status {"remove": true}` takes the number off.
