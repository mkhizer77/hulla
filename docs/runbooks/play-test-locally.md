# Play-test locally

A private Thulla table with friends on the same Wi-Fi, using a laptop as the server. Development builds only.

## 1. Server
From `server/nakama`: `pnpm --filter @hulla/server build`, then `HULLA_BIND=0.0.0.0 docker compose up -d --wait` so phones on the Wi-Fi can reach it (without `HULLA_BIND` it listens on 127.0.0.1 only). Check it with `curl "http://<laptop-ip>:7350/v2/rpc/hello?http_key=local-only-http-key"`.

## 2. Clients
- **Android:** `Unity -batchmode -quit -projectPath apps/unity/Hulla -executeMethod Hulla.BuildTools.BuildCommands.AndroidDevelopment` writes `apps/unity/Hulla/Builds/Android/hulla-dev.apk`. Install it with `adb install -r` (adb ships with Unity's Android module under `PlaybackEngines/AndroidPlayer/SDK/platform-tools`) or copy the file to the phone.
- **macOS:** `...BuildCommands.MacDevelopment` writes `Builds/macOS/Hulla.app`. Several copies can run side by side with `-hulla-device <name>` so each signs in as a different player; `-hulla-server <host[:port]>` sets the server.
- **Editor:** open the project, press Play in the `Boot` scene.

## 3. Play
1. Each player enters a name. In a development build the home screen also shows **Server**: type the laptop's LAN IP (port 7350 is the default). It is remembered.
2. One player taps **Create a table** and reads out the room code; the others type it and tap **Join**.
3. Everyone taps **Ready**; the host taps **Start round** (three to six players).
4. Legal cards are lit on your turn; tap one to play it. **Play again** at the end votes for a rematch.

## Automated check
With Nakama running, `HULLA_NAKAMA_HOST=127.0.0.1 HULLA_NAKAMA_PORT=7350 Unity -batchmode -nographics -projectPath apps/unity/Hulla -runTests -testPlatform PlayMode` runs `RoundAgainstNakamaTests`: three real client stacks create, join by code and play a round to its result.
