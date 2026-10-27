# P18 ruling 1 apply (D-S237)

Steps 1-8 applied as specified. Only addition: the CNG test fix below.

## Checks
- Build Uat and Uat.Tests, Release, TreatWarningsAsErrors: 0 warnings, 0 errors.
- dotnet test: 73 passed, 0 failed, 0 skipped.
- `-Preflight`: exit 1 (allowed); the run folder holds results.json only, no cleanup manifest.
- `git grep -n "Success(null)" -- tests/Dhadgar.Agent.Windows.Uat tests/Dhadgar.Agent.Windows.Uat.Tests`: empty.

## Mutation checks (CleanupEngineTests, file restored byte-identical after each)
1. Reject outside QuiesceIgnoringResult: ProcessProbeThrows_DoesNotAbortCleanup fails.
2. Own-PID check removed: DriverOwnProcess_IsRefused fails.
3. StackImages arm removed: StackImage_IsKilled (index 0 and 1) fail.
4. `!image.IsRunning` returns false: ProcessNotRunning_IsAllowedAndOthersUndone and ServiceAndProcess_AreQuiescedBeforeAnyDelete fail.
5a. CertStoreEntry `=> true`: ForeignCertStore_IsRefused fails.
5b. RegistryValueEntry name rule removed: RegistryValueWithForeignName_IsRefused fails.
5c. CNG provider check removed: first run SURVIVED (the foreign-provider entry used key name "key", refused by the prefix rule anyway). Test fixed: that entry now uses "meridian-uat-key" with "Some Other Provider". Re-run: CngKeyWithForeignProviderOrName_IsRefused fails as required.
