# P20 ruling 1 apply (D-S238)

Worktree: .claude/worktrees/sprint2-agent-nodes-p20, branch s2/p20-uat-process.

## Steps
1. Parent-key rule: AgentWindowsFacts.RegistryParentKeyPath; CleanupEngine Reject allows HKLM exact parent path (IsParentKey, no prefix); Undo arm calls new IMachineActions.DeleteEmptyRegistryKey (only when empty).
2. Agent key deleted through its certificate: WindowsMachineActions.RemoveMatchedCertificate / DeleteCertificateKey (key first, then certificate; My store only; machine key required for LocalMachine; NTE_BAD_KEYSET / NTE_NOT_FOUND treated as success). cngKey name rule not loosened; no CngKeyEntry appended.
3. Temp directory entry dropped from AgentProcess.FirstStartEntries.
4. Producer lists (RegistryToken.ManifestEntries, AgentProcess.FirstStartEntries, AgentLegs.CertificateEntries) and seam test AgentCleanupEntriesTests through the real CleanupEngine.
5. AgentProcess: StopAsync disposes child; log appended (append: true); SawRecoveryWarning reads with FileShare.ReadWrite; read after Stop in AgentLegs finally.
6. AgentLegs: certificates recorded before the other conditions; TokenAsync returns Result<TokenRow> (no Success(null)).
7. AgentPublishConfig: endpoint must be https://localhost:<port>, no userinfo (R-LOCALURL).
8. StoreInspector, SecretScan hardening; scripts/uat/README.md threat model updated.

R-CLEANUP: the parent-key rule and its refusal and allowed tests are added; the agent's key is reached through its certificate (D-S238)

## Checks
- Build driver and test project with -p:TreatWarningsAsErrors=true: 0 warnings, 0 errors.
- dotnet test (Release, TreatWarningsAsErrors): Passed 134, Failed 0, Skipped 0.
- git grep `Success\(null\)|Result<X?>` over both projects: no matches.
- git grep `new CngKeyEntry` in Scenarios: no matches.
- git grep `MachineTempDirectory|GetDirectoryName(machineDir)` in driver: no matches.
- HResult in key-already-gone test: none observed; test passed without widening.

## Mutation table (each applied, test run, failed as required, reverted; tree re-verified 134/134)
| # | Mutation | Test | Result |
| --- | --- | --- | --- |
| 1 | Remove IsParentKey from Reject | AgentCleanupEntriesTests, CleanupEngineTests.RegistryParentKeyNotExistedBefore | fails |
| 2 | IsParentKey uses StartsWith | UndoAsync_RegistryKeyNearParent_IsRefused | fails |
| 3 | Delete Undo parent arm | UndoAsync_RegistryParentKeyNotExistedBefore | fails |
| 4 | DeleteEmptySubKey skips empty check | DeleteEmptySubKey_HasValue_KeepsAndFails | fails |
| 5 | remove(cert) before key delete | RemoveMatchedCertificate_* (4) | fails |
| 6 | Drop requireMachineKey check | RemoveMatchedCertificate_* (2) | fails |
| 7 | Drop storeName == "My" | RemoveMatchedCertificate_AgentCertificateInRoot_KeepsKey | fails |
| 8 | Restore Temp entry | UndoAsync_EveryEntryP20Appends | fails |
| 9 | Restore CngKeyEntry | UndoAsync_EveryEntryP20Appends | fails |
| 10 | Remove dispose in StopAsync | StopThenStart_* | fails |
| 11 | append: false | StopThenStart_* | fails |
| 12 | File.ReadLines-style share (FileShare.Read) | StopThenStart_* | fails |
| 13 | Old finally order | RunAsync_ReadsRecoveryWarningAfterStop | fails |
| 14 | Record call moved back after checks | U3_RecordsCertificateBeforeOtherConditionsHold | fails (first script run did not apply; redone by hand-script) |
| 15 | Endpoint check IsAbsoluteUri only | Apply_EndpointNotLocalhostHttps_Fails | fails |

## -StackOnly
`pwsh -NoProfile -File scripts/uat/Invoke-AgentWindowsUat.ps1 -StackOnly`: exit code 2, "stack-only refused: devCerts: run 'dotnet dev-certs https --trust'" (untrusted dev cert, accepted). No elevated or machine-changing mode, no msiexec, cert not trusted.
