# The phase 0 and phase 1 image: Nakama plus the bundled runtime modules and the configs.
# Build after `pnpm --filter @hulla/server build`; server-image.yml does both.
ARG NAKAMA_VERSION=3.41.0
FROM heroiclabs/nakama:${NAKAMA_VERSION}
COPY build/index.js /nakama/data/modules/index.js
COPY config/ /nakama/data/config/
