# Moderation escalation

Who may act, with what, and when (GDD 24.3, 24.4, 10.11; #91).

## Who
The admin RPCs (`admin_sanction`, `admin_table_inspect`, `admin_round_replay`, `admin_audit_list`, `admin_grant`, `admin_drain`, `admin_look_feedback`, `reports_list`, `chat_filters_set`, `missions_set`, `achievements_set`, `catalog_set`, `config_set`) accept the server key and the owner accounts listed in `HULLA_ADMIN_USER_IDS` on the box. Nobody else. Every call writes an audit row (`admin_audit_list`), and the audit log is reviewed in the weekly ritual.

## The ladder (GDD 24.3)
1. Filters act first: masked text in Standard rooms, blocked in Family rooms, links and numbers refused.
2. Three reports from distinct players within 24 hours, one per table, mute the target for 24 hours automatically (`social.muted`).
3. A reviewer reads the report (`reports_list`): the table's last seven days of chat and the target's card are attached. Decide within 48 hours (the 90% target).
4. Actions by `admin_sanction`, in order: `warning`; `mute` for 1 hour or 24 hours; `voice_suspend` for 24 hours, 7 days or permanent; `ban` for 24 hours, 7 days or permanent; `ban` with `device: true` for the device. Repeat offences move one step up the ladder; cheating or collusion proven by `admin_round_replay` and the log starts at a 7-day ban; underage reports go to account deletion with the compliance note (GDD 25.4).
5. Appeals arrive by email within 14 days; `admin_sanction` with `clear` lifts everything and keeps the history. The reviewer writes one line in the audit detail (`reason`).

## Never
No sanction without a report or a replay behind it; no reading of chat beyond the attached context; no sharing of player data outside the ticket.
