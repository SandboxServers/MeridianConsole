Recorded by P15 from run 20261027-182612-65dd (Process mode); launched by agent (D-S239); source .uat/agent-windows/20261027-182612-65dd/results.json.

# Windows agent UAT 20261027-182612-65dd

- Verb: run (mode process)
- Overall: **pass** (exit 0); cleanup: clean
- Git: 65302536fe4dc3858fdd3137af726103b71bf0a9; Windows build 26300; .NET 10.0.14
- Launched by: agent (D-S239), tree clean: True; elevated: True

| Step | Name | Status | Evidence | Duration |
| --- | --- | --- | --- | --- |
| U1 | process | pass | ports=postgres 53845, rabbitmq 5672, nodes 53846, gateway 53847; postgresImage=sha256:d4c3314b2dd74ff23cd6cd07e4d7e737cb6ef791c22a7cbd744cfbfb4815df72; rabbitmqImage=sha256:e582c0bc7766f3342496d8485efb5a1df782b5ce3886ad017e2eaae442311f69; nodesPid=139332; gatewayPid=315520; U1=pass; organizationId=1e33e523-cd59-4597-b194-c9510524ea59; tokenId=f57299a4-429e-44ce-ad26-d88188c1c7b6; S-mint=pass; enrollStatus=201; nodeId=3781c7c5-7ef3-4719-812e-9a8068d17f0b; thumbprint=7DBAFEF6F0512D004D79028EB8873B8949034915E1EBE995EF62BF0C2DEC152C; S-enroll=pass; negotiateWithCertificate=200; negotiateWithoutCertificate=401; S-negotiate=pass; hubTransport=WebSockets; hubAckServerTime=2026-10-27T18:26:47.9749650+00:00; S-hub-ws=pass; lastHeartbeat=2026-10-27T18:26:51.1779600Z; S-db=pass; legsOrganizationId=1e33e523-cd59-4597-b194-c9510524ea59; warnings=System.Collections.Generic.List`1[System.String]; U2.tokenId=4d49eca5-ccae-4981-a1a2-ab5f80469877; U2=pass; keyName={EB81C1A9-46B5-41C7-AEB2-D40A989FAC45}; notAfter=2027-01-25T18:26:48.0000000Z; stateFileAclSids=S-1-5-18,S-1-5-32-544,S-1-5-21-4211940003-2562466698-853368739-1001; U3=pass; U4.token2Id=4923cab5-d859-48bf-ab10-fd24caeed3dc; U4.heartbeatBefore=2026-10-27T18:26:51.1779600Z; U4.certificateRowsBefore=1; U4.heartbeatAfter=2026-10-27T18:26:54.9803600Z; U4.token2StillInRegistry=True; U4=pass; U5.graceUntil=2026-10-27T18:31:56.7299760Z; U5.negotiateC1=200; U5.negotiateC2=200; U5=pass; U6.thumbprint=5CE01AE8EDDBEF7F6FFDF1682B31B81C74256AF3C2D64308EAD5660FA5D82172; U6.lastHeartbeat=2026-10-27T18:27:09.2927860Z; U6=pass; U7.c2Gateway=401; U7.c3Gateway=200; U7.c1NodesInGrace=200; U7.waitUntil=2026-10-27T18:32:06.7299760Z; U7.c1NodesDirect=401; U7.c1Path=Nodes direct, loopback, X-Client-Cert (C1's key is gone; the Gateway path proved C1 in U5); U7=pass; U8.certificateRowsBefore=3; U8.heartbeatAfter=2026-10-27T18:32:10.6962810Z; U8=pass | 346102 ms |
| U9 | Cleanup and scan | pass | cleanup=clean; undone=18; filesScanned=5; secretFindings=0; passwordWarnings=0 | 13 ms |
