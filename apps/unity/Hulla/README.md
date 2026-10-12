# Hulla Unity client

Unity 6000.4.9f1 (`ProjectSettings/ProjectVersion.txt`). Open this folder in the Unity Hub.

- Scripts live in `Assets/_Project`, one assembly per layer (ARCHITECTURE §9.1). `AssemblyGraphTests` fails when a layer gains a reference §9.1 does not allow.
- Scenes: `Boot` builds the app scope and loads `Main`; `Table` is loaded additively per table.
- Protocol DTOs come from `packages/protocol/generated/csharp` through a local package; nothing generated is copied in.
- Edit-mode tests from the command line:

```
Unity -batchmode -nographics -projectPath apps/unity/Hulla -runTests -testPlatform EditMode -testResults editmode.xml
```

The CI Unity job runs only once the `UNITY_LICENSE` secret is set; until then run the tests locally and paste the result into the pull request.
