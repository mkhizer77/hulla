# Phone sign-in setup

Phone OTP sign-in (GDD 10.2, #99) runs on the server with any SMS verification provider behind `OtpProvider` (`server/nakama/src/services/phone_otp.ts`). Twilio Verify is wired in; the choice and contract are the owner's (OQ-041).

## What the server needs
| Variable | What it is |
|---|---|
| `HULLA_PHONE_PEPPER` | A long random secret, set once and never changed. Numbers are stored only as a hash with it; changing it unlinks every phone. Keep a copy in the password manager. |
| `TWILIO_ACCOUNT_SID`, `TWILIO_AUTH_TOKEN` | The Twilio account. |
| `TWILIO_VERIFY_SID` | A Verify service (`VA…`) with the SMS channel on and a code length of 6. |

Put them in the Nakama runtime env (`runtime.env` in the server config, from the box's secrets file), then restart Nakama. Without a provider the RPCs answer `auth.otp_unavailable`. On a local box (`HULLA_ENV=local`) the local provider needs nothing: no SMS is sent and the code is the number's last six digits.

## Limits and cost
- Per number: one code a minute, three per 15 minutes, six a day. Per account: five an hour, ten a day. Per address: ten an hour. Five wrong guesses lock a code; a code lasts 10 minutes.
- Only the calling codes in `OTP_COUNTRIES` are served (the markets of GDD 1.4); premium destinations used for SMS pumping are refused.
- `otp_daily_budget` in remote config caps the sends per day across everyone (2,000 by default). Setting it to 0 stops all sends at once.
- The dashboard's phone panels show codes and estimated spend by country, refusals by reason and sign-ins. Two alerts: spend above US$20 in an hour, and fewer than 30% of codes verified (likely pumping). The cents per country in `OTP_COUNTRIES` are estimates until the contract sets prices.

## Checking it
1. On the phase 0 box, with the variables set: `phone_start {"phone": "+92…"}` from a signed-in guest sends an SMS; `phone_verify {"phone", "code"}` answers `linked`.
2. A second device: the same two calls answer `signIn` with a token; `authenticateCustom(token, create=false)` signs in to the first account.
3. `phone_status {"remove": true}` takes the number off.
