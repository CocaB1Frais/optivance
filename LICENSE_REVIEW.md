# OPTIVANCE — License Review

**Source audit date:** 2026-11-06  
**Decision/document alignment reviewed:** 2026-11-10  
**Distribution assumption:** A proprietary Windows desktop application may be distributed commercially. The final commercial model is not yet approved. No third-party code or asset is currently in the repository.

## Findings by candidate

| Candidate | License as declared upstream | Reuse decision for a proprietary product |
|---|---|---|
| [WinBlaze](https://github.com/marksmayo/WinBlaze) | MIT | Permissive in principle. Preserve copyright and license notices for any copied/derived code; verify file headers and dependencies. No code copied. |
| [DiskTree](https://github.com/dmoore-dwmmholdings/disktree) | MIT OR Apache-2.0 | Permissive in principle under either declared license. Preserve selected license terms and notices. Apache-2.0 has additional NOTICE/modified-file and patent provisions where applicable. No code copied. |
| [DiskSleuth](https://github.com/Swatto86/disksleuth) | MIT | Permissive in principle with notice retention. Audit per-file notices and Cargo dependency licenses first. No code copied. |
| [RustySpaceSniffer](https://github.com/GRodolphe/RustySpaceSniffer) | MIT OR Apache-2.0 | Permissive in principle. Dual license does not remove the need to retain attribution and to audit its dependencies. No code copied. |
| [TreeLens](https://github.com/skyflyt/treelens) | MIT, Copyright (c) 2026 Skylar Pearce | Permissive in principle. Preserve notice for any code use. No branding/assets or code copied; current interface is a reference only. |
| [eDirStat](https://github.com/xangelix/edirstat) | MIT, Copyright (c) 2026 Cody Neiman | MIT permits commercial distribution subject to notice requirements. Its README separately describes how third-party package managers may distribute its paid upstream binaries; that statement is not in the MIT license text, but it creates enough ambiguity to clarify with the maintainer before using code in a commercial product. No code copied. |
| [NeoTreescan](https://github.com/neortal-technologies/NeoTreescan) | Apache-2.0 | Permissive in principle. Preserve the license, copyright, and any NOTICE attributions; mark modified files; review patent and trademark terms. Its README identifies Neortal Technologies Inc. as publisher. No code copied. |
| [WinDirStat](https://github.com/windirstat/windirstat) | GPL v3 or later for the application; logo and derivatives CC BY 3.0 | Do not incorporate application code into a proprietary distribution without a deliberate GPL-compliant distribution strategy. Do not reuse logo/derivative artwork without separate attribution and license review. Use only as a behavior reference. |
| [SquirrelDisk](https://github.com/adileo/squirreldisk) | AGPL-3.0 | Do not incorporate into a proprietary distribution without accepting AGPL obligations for a combined/modified work and reviewing network-use implications. Use only as a feature/interaction reference. |
| [Ferris Scan](https://github.com/Vnilabean/ferris-scan) | MIT | Permissive in principle with notice retention. The MFT implementation is not mature enough to justify adoption based on the README. No code copied. |
| [Colin Finck’s ntfs crate](https://github.com/ColinFinck/ntfs) | MIT OR Apache-2.0 | Permissive in principle; retain chosen license and notices. Consider only after feature-gap and dependency review. |
| [omerbenamram/mft](https://github.com/omerbenamram/mft) | MIT OR Apache-2.0 | Permissive in principle; useful as an offline fixture/parser comparison candidate, not a full live scanner. Retain notices if adopted. |
| [Cyber-Experts/usn-parser](https://github.com/Cyber-Experts/usn-parser) | Publisher website claims Apache-2.0; linked repository returned 404 during this audit | Not cleared. License text, exact source, copyright owner and dependency set were inaccessible. Do not use code until the repository and license are verified. |
| [wangfu91/usn-journal-rs](https://github.com/wangfu91/usn-journal-rs) | MIT; license file identifies Copyright (c) 2025 Fu Wang | Permissive in principle with notice retention. Candidate wrapper for a prototype only; check crate dependencies and API behavior before adoption. |
| [wangfu91/ntfs-mft](https://github.com/wangfu91/ntfs-mft) | MIT, as declared in its README | Permissive in principle, but verify copyright text and binary-fixture provenance before redistribution. Its documented parser gaps and early version are separate technical blockers. |
| [bacongobbler/treemap-rs](https://github.com/bacongobbler/treemap-rs) | MIT | Permissive in principle with notice retention. Candidate geometry helper only; verify copyright line and crate dependency tree before adoption. |

| [.NET / WPF distribution](https://github.com/dotnet/core/blob/main/license-information.md) | .NET source repositories use MIT; Windows product distributions use the .NET Library License and include runtime packs | WPF is the baseline UI; .NET 10 self-contained x64 is selected. For a shipped build, retain the exact embedded runtime-pack licenses and THIRD-PARTY-NOTICES for the selected .NET release. No runtime added. |
| [Windows App SDK 2.6.5 package terms](https://www.nuget.org/packages/Microsoft.WindowsAppSDK/2.6.5/License) | Microsoft package terms; SDK source repository is MIT | Not selected for the baseline. The reviewed package terms contain data-collection and distribution language that needs product/legal clearance against the no-automatic-transmission requirement. Private-build statements do not by themselves clear commercial distribution. No package added. |
| [SQLite](https://www.sqlite.org/copyright.html) | SQLite authors; public-domain dedication | Core SQLite can be copied, modified, sold, and distributed under its dedication. Keep provenance and review any wrapper/build package separately. No code added. |
| [rusqlite / libsqlite3-sys](https://github.com/rusqlite/rusqlite) | Rust wrapper contributors | MIT wrapper/crate terms; bundled SQLite code is public domain. Pin exact versions and audit transitive/build dependencies and notices before approval. No crate added. |

## License obligations in practical terms

- **MIT:** Include the copyright and permission notice with copies or substantial portions. It does not require source disclosure for a proprietary binary.
- **Apache-2.0:** Include the license; preserve applicable NOTICE attributions; mark modified files; review patent provisions and the rule that the license does not grant trademark rights. It does not generally require source disclosure for proprietary binaries.
- **GPL-3.0-or-later:** Distribution of a covered/derived work requires providing corresponding source under the GPL and preserving the required rights/notices. Do not combine GPL application code into a proprietary executable without choosing and meeting the applicable obligations.
- **AGPL-3.0:** Includes GPL-style distribution obligations and an additional source-offer requirement for users interacting with a modified program over a network. A normal offline desktop product still needs a distribution analysis if code is copied or combined.
- **CC BY 3.0:** Attribution and license notices are needed for the separately licensed WinDirStat artwork; do not treat code and logo as one license.

These are engineering screening summaries, not a legal opinion. Before shipping any external code, inspect the exact version/tag, all copyright headers, bundled assets, generated files, and transitive dependencies. If proprietary distribution remains the plan, obtain legal review for any GPL/AGPL combination and for any additional README terms that conflict with a repository’s license.

## Current clearance status

- No third-party source, binaries, artwork, fonts, or runtime packages were added to the local project by this audit.
- No third-party dependency is approved for Phase 1 yet.
- The WPF baseline still needs an exact .NET runtime-pack and notices review before distribution. Set `DOTNET_CLI_TELEMETRY_OPTOUT=1` for .NET CLI use in developer/CI environments; this setting does not establish the application’s own network behavior.
- Technical ideas (arena indexing, bounded traversal, treemap algorithms) will be implemented independently unless a later review selects a specific, license-compatible component.
- When dependencies are chosen, pin versions, generate a complete license/notice inventory from the lockfiles, retain source URLs and checksums, and add the correct notices to the shipped installer and About/Licenses view.

