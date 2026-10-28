Note N1 pass 2: committed ebe031e; mutation (Task.Delay(wait,ct)) failed by timeout, restored, 8/8 UatRunnerTests pass; build TreatWarningsAsErrors ok.
Note N2 pass 2: committed (see git log); fact LogClauseSource_FromTheContainer_ReadsTheSupervisorRing added; reverting UatArea wiring fails it with [Lab.Unavailable]; restored; build TWAE ok (only IL2026/IL2072).
Note N3 pass 2: committed; added UatAttestTool_RecordsTheLeaseOwnerAndTheCallContextCaller and UatAttestTool_UnknownLease_IsRefusedAndTheRowIsUnchanged; owner->string.Empty mutation fails 1 fact, restored; 12/12 UatAttest pass; build TWAE ok.
Note N4 pass 2: committed; IdPattern \\z; added human-look newline row to BadNoteOrClauseId asserting 'clause id is not valid'; fails with old $ (1 fact), passes with \\z; restored; 13/13 UatAttest pass; build TWAE ok.
Note N5 pass 2 part (b): committed; RowFile_TruncatesAnObservedValueOverTheEvidenceCap added (FakeDbSource takes optional payload); MaxEvidenceChars=int.MaxValue fails it, restored; 5/5 RowEvidence pass. Part (a) (fingerprint.json secret-failed) pending.

## Continuation (session 2 of 3)
Done: N1 ebe031e, N2 dfc444d, N3 da9f69b, N4 de6df5d, N5(b) cf4ce35. Last check: RowEvidenceTests 5 passed, 0 failed.
Left: N5(a) in BundleSecretTests.cs (see report). Next step: read LiveFingerprintParts (grep in src/Tools/Dhadgar.Lab/Uat/Bundle), add a minimal helper in the test if needed, plant the vault value in fingerprint.json, assert the file is absent and secret_scan_removed lists it, mutate BundleWriter.cs:226 (!secretFailed to true) to confirm, restore, commit.
Then run: build TreatWarningsAsErrors (tests project), lab tests with filter RequiresDocker!=true&FullyQualifiedName!~DockerTests, and report.
Note N5 pass 2 part (a): committed ca88db4; BundleSecretTests.UnredactableSecret_InFingerprint_IsRemovedAndNotRewritten (Password= in toxiproxy version; redactor keeps Password=, scanner removes fingerprint.json); !secretFailed->true fails it, restored; 3/3 BundleSecret pass. Also CA1849 async reads in UatAttestTests (build error from N3).
Next: final TWAE build (CI=true) and lab tests filter RequiresDocker!=true&FullyQualifiedName!~DockerTests, then report. Last check: BundleSecretTests 3 passed, 0 failed.
