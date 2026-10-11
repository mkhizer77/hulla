# ADR-0013: Hosting and cost plan: free EU box for testing, paid regions with real users
Status: Accepted
Date: 2026-10-11

## Context
Running costs must stay near zero until real players arrive; the owner develops and tests from Europe; the first players will be in Pakistan, India and Bangladesh. Research in `docs/research/hosting-options.md` (2026 prices): Hetzner is the cheapest reliable EU VPS (about €6 a month), Oracle's Always Free tier is the only "free forever" compute but its ARM allowance was halved in August 2026 and EU capacity is often unavailable, Azure's free account gives twelve months of a small VM, AWS gives six months of credits, Fly.io has no free tier, Koyeb and Render free tiers sleep and are too small for a game server.

## Decision
The whole server is one Docker Compose stack (Nakama, Postgres, backup job) so any host is a copy of a compose file and a DNS record.
- **Phase 0, development and testing:** a free EU box: Oracle Cloud Always Free with the home region in London or Frankfurt (ARM A1 if capacity exists, otherwise two AMD Micro instances); if no capacity, an Azure free-account VM in UK South or West Europe (twelve months), with AWS Free-plan credits as a bridge; daily development may also run on the owner's Mac exposed through Cloudflare Tunnel. Cloudflare's free plan provides DNS, TLS, proxying and WebSockets; Cloudflare R2 holds content and nightly dumps.
- **Phase 1, soft launch:** a Hetzner CX23 in Nuremberg or Falkenstein for EU and UK testers and one player-region box (DigitalOcean Bangalore or Hetzner Singapore). The first public cohort never runs on a free tier.
- **Phase 2, growth:** larger nodes, managed Postgres, a second player region; Heroic Cloud only if operations should be bought.

## Consequences
Near-zero cost during development; a tested migration path because the stack is identical everywhere; free tiers bring idle-reclamation and capacity risks, which is why they are confined to Phase 0 and backed up nightly to R2.

## Alternatives considered
Paying from day one (unnecessary before testers); a managed game backend (lock-in and cost); hosting only in South Asia from the start (worse latency for the owner's own testing).
