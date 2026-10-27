# Look playtest: Basant and Pop

Two of the Home concepts from 26 October (`art-design/concepts/`, concepts 2 and 4) are in the app as table themes, so players can try them on every screen and tell us which one to build. Basant is the bright kite-festival afternoon for the main audience; Pop is the clean modern look for the wider market (`docs/design/design-direction.md` §1).

## What is in the app
- **Two looks** in Settings → Table theme, beside the five existing ones. Placeholder art drawn by `tools/artkit/skins.py`, not final art. They reskin today's screens; the Home layout and the five-tab dock are still the current ones, not the concepts' (friends row, Trophies and Shop dock).
- **A Home card**, "New looks to try", with both looks as tiles; one tap switches the whole app. It also has "Tell us about this look" while a new look is on. The card goes once both looks have an answer.
- **One question after a round.** Back on Home after a round in Basant or Pop, the player is asked once per look: how they like it (Love it, Like it, It's okay, Not really, Don't like it), Keep it or Back to their old look, and an optional line (280 characters). Not now asks again at the next launch. Back to their old look switches at once.
- **The server** keeps the latest answer per player and look (`look_feedback`), with the rounds played in that look. No names, no other data.
- **Off switch:** the remote-config flag `looks_test` (default on) hides the card and the question. If a config has been saved with `config_set`, add `looks_test` to it, because defaults are not merged into a saved config.

## Who to ask
Aim for 20 players or more, at least 10 answers per look:
- Friend groups, 16 to 35 (the majority): about half the testers, played as a group of 4.
- Families and older players, 25 to 55: a quarter, ideally with a younger relative setting up.
- Diaspora and Indian or Bangladeshi players who know the game as Bhabhi: a quarter.

Ask each tester to play at least one round in each look. Keep a note of which group each tester is in. The server stores no age or country, so the note is the only way to split the answers by group.

## What to watch in person
For a few moderated sessions, ask:
1. First five seconds on Home: what is this app, and what would you tap first?
2. Find how to play with your friends (Create a room, Join by code). How long did it take?
3. Outdoors or by a window: can you read everything?
4. Does anything look like gambling?
5. Which look would you show your family, and which your friends?

## Reading the answers
```
curl -s -X POST "https://<host>/v2/rpc/admin_look_feedback?http_key=<runtime http key>&unwrap" -d '{}'
```
For each look it returns the number of answers, the average rating (1 to 5), the share who want to keep it and the newest 200 comments. Each read is written to the audit log.

## Deciding
- A look **passes** with 10 or more answers, an average of 4.0 or more and a keep share of 60% or more.
- **Both pass:** take the one the friend-group testers rated higher as the default direction for the art pass. The other becomes a theme.
- **One passes:** take it, and fold what the comments liked about the other into it.
- **Neither passes:** keep Rooftop as the default and iterate on the comments.

Record the result and the numbers in `docs/STATUS.md`, then turn `looks_test` off.
