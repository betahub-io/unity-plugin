# EditTests: fast test loop

Compiles `Runtime/Scripts` and `Tests/Editor` against Unity's DLLs with the dotnet SDK and runs the
plain-C# NUnit tests. Takes seconds. Needs no Unity license.

```bash
cd Tools~/EditTests
dotnet test --filter "FullyQualifiedName~BugReportSessionTests|FullyQualifiedName~ReportSubmissionTests|FullyQualifiedName~ReporterConfigurationTests"
```

Only tests that never touch the Unity runtime can run here. `VideoEncoderCleanupTests` needs Unity; do not
include it in the filter. The adapter version in use rejects `!~` filters, so list the suites explicitly.

Requirements: dotnet SDK 8+, a Unity 2022.3 install (override with `UNITY_EDITOR_PATH`), and a Unity project that
consumes this plugin and has been opened once so `Library/ScriptAssemblies` exists (override with `BETAHUB_UNITY_WORKSPACE`).

This is the inner loop. Before finishing a change, run the real thing:

```bash
Unity -batchmode -nographics -projectPath "<workspace>" -runTests -testPlatform EditMode -testResults /tmp/editmode.xml -logFile /tmp/unity_tests.log
```
