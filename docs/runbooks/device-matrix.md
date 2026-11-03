# Device matrix and smoke tests (#78)

## What runs

- **Every screen, every pull request.** `EveryScreenSmokeTests` opens each screen and sheet in `ScreenCases` with its fakes and fails on an error in the log, a screen with no text, a raw string key, an unfilled `{0}` or a `?` standing in for a name. Next to it, `ScreenSmokeTests` checks each screen's own bindings, and the accessibility, pseudo-locale and snapshot tests run on the same cases. All of them are in the `Unity screens in play mode` CI job.
- **The device matrix, nightly.** `.github/workflows/device-matrix.yml` builds a development APK and runs a five-minute Robo crawl on the devices in `tools/testlab/devices.txt`, one Firebase Test Lab matrix. The run's summary lists each device's outcome and links to the Test Lab results: videos, screenshots, logcat, crashes and performance. Nothing runs until the secrets below exist.

## Set up once (owner)

1. Firebase console › project `hulla-513213` › Test Lab: open it once so the project is enabled. On the free Spark plan Test Lab allows 10 virtual and 5 physical device runs a day. That is enough for the three virtual devices listed.
2. Google Cloud console › IAM › Service accounts: create `testlab-ci` with the roles **Firebase Test Lab Admin** and **Storage Object Admin** (for the results bucket). Create a JSON key.
3. GitHub › Settings › Secrets and variables › Actions:
   - secret `TESTLAB_SERVICE_ACCOUNT`: the whole JSON key;
   - variable `TESTLAB_RESULTS_BUCKET` (optional): a `gs://` bucket of your own, otherwise Test Lab uses its default bucket;
   - variable `HULLA_TESTLAB_SERVER` (optional): `host:port` of a reachable server. Without it the APK starts with no server and the crawl covers first launch, the tutorial and the offline states.
4. Actions › device-matrix › Run workflow, to check it before the first night.

## Devices

`tools/testlab/devices.txt` holds one `--device` value per line. The GDD 28.4 phones (Redmi 9A, Infinix Hot 10, Tecno Spark 7) are not all in Test Lab, so the list starts with virtual stand-ins for the low and mid tiers and an Urdu run for right-to-left layout. Add physical models after checking their ids with `gcloud firebase test android models list`; each costs physical quota.

## Still by hand

Reconnection on a real phone with airplane mode toggled (#78's third point) stays a device-session check: start a round on two phones against the local server, toggle airplane mode on one for 20 seconds, and check that the seat shows Reconnecting, auto-plays after the grace period and comes back on its own.
