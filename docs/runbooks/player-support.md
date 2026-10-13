# Player support

The support channel, the response targets and the common cases (GDD 22.7, 10.2; #91).

## Channel and targets
Email at the address the owner sets (OQ-031), linked from Settings and the store listings, with an auto-reply that states the 48-hour target. In-app, the help centre and FAQ answer the common questions first. Refunds go through the stores; the support reply says how.

## Common cases
- **Lost progress after reinstalling.** The account was a guest. If the player can sign in on the old device, link a provider there (Settings, Account) and sign in with it on the new one; the profile, wallet and inventory follow. If the old device is gone, nothing can be restored (GDD 9.4), and the reply says so plainly.
- **Missing reward.** `wallet_get` from the player's side shows the ledger; on ours, the audit and the ledger line with the source id say whether it was paid. A real loss (a crash between the round end and the credit, an incident) is settled with `admin_grant` and a source id that names the ticket, so a second grant for the same ticket is a no-op.
- **Banned or muted.** `admin_audit_list` shows the action and reason; the reply quotes the reason and the expiry. Appeals follow `moderation-escalation.md`.
- **Account deletion or export.** The player uses Settings (`account_delete`, `account_export`); support confirms the 14-day grace period and that the export is a JSON file of their own data.
- **A rules dispute.** The round log is authoritative: `admin_round_replay` reproduces the round; the reply names the rule id from the rulebook. The game settles disputes, support does not relitigate them.
- **Purchase not delivered.** Receipt validation (#58) is retried from the stores' receipt; until it exists, premium is granted by hand with `pass_premium_grant` after the receipt is seen.

Every reply is short, in the player's language where we have it, and never includes another player's data.
