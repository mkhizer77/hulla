# Hosting options: research notes (11 Oct 2026)

Goal: zero running cost while developing and testing from Europe; a player-facing region near Pakistan, India and Bangladesh when real users arrive. Everything runs as Docker Compose (Nakama + Postgres) so the stack moves between providers unchanged. Decision: ADR-0013.

## Free options in Europe
| Option | What you get | Catches | Verdict |
|---|---|---|---|
| **Oracle Cloud Always Free** (home region London, Frankfurt, Amsterdam, Paris, Zurich…) | ARM A1 up to 2 OCPU / 12 GB (halved in August 2026) plus two AMD Micro VMs (1 OCPU, 1 GB each), 200 GB block storage | ARM capacity often "out of capacity" for weeks in EU regions; idle instances reclaimed (7-day 95th-percentile CPU and network under 20%); no DDoS protection; home region cannot change later | First choice if capacity appears; keep a heartbeat workload or convert to pay-as-you-go to avoid reclamation |
| **Azure free account** (UK South, West Europe) | $200 credit for 30 days plus twelve months of 750 hours a month of a B1s, B2pts v2 (ARM) or B2ats v2 VM | Twelve months, then pay-as-you-go (about €10–15 a month for the same VM); 1 GB RAM is tight, so Postgres must be tuned small | Best fallback when Oracle has no capacity |
| **AWS Free plan** (London, Frankfurt, Ireland) | $100 credit, up to $200 with onboarding tasks, valid six months; an "always free" tier for some services | Six months only; EC2 is paid from credits | A short bridge, not a home |
| **Own Mac or a spare machine + Cloudflare Tunnel** | Unlimited, free, no inbound ports needed, Tunnel is on Cloudflare's free plan | Must stay on; home upload bandwidth; not for a public beta | Ideal for daily development and friend tests |
| Koyeb free instance (Frankfurt) | 0.1 vCPU, 512 MB, free Postgres with 5 compute hours a month | Scales to zero after an hour idle; too small for Nakama + Postgres; card pre-authorisation | Not suitable |
| Render free web service | Sleeps after 15 minutes idle, no persistent disk, free Postgres expires after 30 days, no EU free region found | | Not suitable |
| Fly.io | No free tier for new organisations since October 2024 (trial only) | | Not suitable |

## Cheapest paid options in Europe (2026 prices)
| Option | Monthly | Notes |
|---|---|---|
| Hetzner CX23 (2 vCPU, 4 GB), Nuremberg or Falkenstein | about €6 incl. IPv4; snapshots about €1 | Prices rose in April and June 2026, still the cheapest reliable EU VPS; 20–40 ms from the UK |
| Hetzner CAX11 (2 ARM vCPU, 4 GB) | about €6.50 | ARM images exist for Nakama and Postgres |
| DigitalOcean (Amsterdam, London) | $6–12 | Managed Postgres upgrade path |
| Fly.io (Amsterdam, London) | from about $2–7 per machine plus volumes | GitHub-driven deploys; Postgres unmanaged |

## Around the players, later
| Option | Monthly | Notes |
|---|---|---|
| DigitalOcean Bangalore | $12–48 | Closest predictable region to Pakistan and India |
| Hetzner Singapore | from about €8, higher egress price | 90–110 ms from Karachi |
| AWS Lightsail or EC2 Mumbai | $10–40 | When managed services are wanted |

## Pieces that are the same everywhere
Cloudflare free plan for DNS, TLS, proxying and WebSockets (supported on Free, low concurrency); Cloudflare R2 for content and nightly dumps (free egress); GitHub Container Registry for images (free for public repos); Grafana Cloud free tier for metrics; the compose stack and a short bootstrap script in `infra/`.

## Cost plan
| Stage | Monthly |
|---|---|
| Phase 0, development and testing on a free EU tier | $0 plus domains |
| Phase 1, soft launch: Hetzner EU box plus one player-region box | $20–50 |
| Phase 2, growth: larger nodes, managed Postgres, second region | $150–400 |
