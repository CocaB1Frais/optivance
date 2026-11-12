# OPTIVANCE — Third-Party License Register

**Status:** Phase 0 candidate register only. **Source audit date:** 2026-11-06. **Decision/document alignment reviewed:** 2026-11-10. No third-party code, assets, or binary dependencies are currently included in this repository. This file must be updated from actual locked dependencies before any distributable build.

## Candidate register

“Copyright holder” below is taken from an upstream LICENSE/README when visible. Where the repository did not expose an explicit holder during the audit, that is stated rather than inferred from a GitHub account. Confirm file-level notices before any use.

| Repository / component | Copyright holder or publisher | Exact declared license | Distribution obligations to track | Potential use |
|---|---|---|---|---|
| [WinBlaze](https://github.com/marksmayo/WinBlaze) | marksmayo repository publisher; exact license-file holder not independently confirmed | MIT | Include copyright and permission notice with substantial copies; no source disclosure obligation from MIT | Architecture and MFT behavior reference; no code selected |
| [DiskTree](https://github.com/dmoore-dwmmholdings/disktree) | dmoore-dwmmholdings publisher; individual holders to verify | MIT OR Apache-2.0 | Retain MIT notice or, if selecting Apache, license/NOTICE/modified-file and patent terms | Benchmark/size-semantics reference; no code selected |
| [DiskSleuth](https://github.com/Swatto86/disksleuth) | Swatto86 publisher; verify file headers | MIT | Preserve MIT notice | Arena and UI/core architecture reference; no code selected |
| [RustySpaceSniffer](https://github.com/GRodolphe/RustySpaceSniffer) | GRodolphe publisher; verify file headers | MIT OR Apache-2.0 | Retain selected license and applicable notices | MFT and treemap research only |
| [TreeLens](https://github.com/skyflyt/treelens) | Skylar Pearce | MIT | Preserve copyright and permission notice | User-selected visual reference only; no assets/code selected |
| [eDirStat](https://github.com/xangelix/edirstat) | Cody Neiman (LICENSE copyright line) | MIT | Preserve copyright and permission notice; clarify separate README packaging statement before product reuse | Compare arena and MFT scanner; no code selected |
| [NeoTreescan](https://github.com/neortal-technologies/NeoTreescan) | Neortal Technologies Inc. | Apache-2.0 | Preserve license, copyright and NOTICE; mark modifications; retain patent/trademark notices | WPF comparison only |
| [WinDirStat application](https://github.com/windirstat/windirstat) | WinDirStat Team | GPL-3.0-or-later | Corresponding source and GPL terms for covered distributed derivatives | Behavior reference only; no code selected |
| [WinDirStat artwork](https://github.com/windirstat/windirstat) | WinDirStat Team | CC BY 3.0 | Attribution, license link and indication of modifications as applicable | No artwork selected |
| [SquirrelDisk](https://github.com/adileo/squirreldisk) | adileo and contributors; preserve individual headers | AGPL-3.0 | Copyleft/source obligations on distributed derivatives and network-interaction provisions | Behavior reference only; no code selected |
| [Ferris Scan](https://github.com/Vnilabean/ferris-scan) | Vnilabean publisher; verify file headers | MIT | Preserve copyright and permission notice | Benchmark/maturity comparison only |
| [Colin Finck’s ntfs crate](https://github.com/ColinFinck/ntfs) | Colin Finck | MIT OR Apache-2.0 | Retain selected license and applicable notices | Possible parser test oracle; no dependency approved |
| [omerbenamram/mft](https://github.com/omerbenamram/mft) | Omer Ben Amram repository author; check individual headers | MIT OR Apache-2.0 | Retain selected license and applicable notices | Offline MFT fixture parser/cross-check only |
| [Cyber-Experts/usn-parser](https://github.com/Cyber-Experts/usn-parser) | Publisher page says Cyber Experts; GitHub source unavailable | Apache-2.0 claimed by publisher, not verified against repository | Unknown until repository, license and dependency manifests are accessible | No use permitted by this register |
| [wangfu91/usn-journal-rs](https://github.com/wangfu91/usn-journal-rs) | Fu Wang (LICENSE copyright line) | MIT | Preserve copyright and permission notice | Potential Windows API wrapper prototype; not approved |
| [wangfu91/ntfs-mft](https://github.com/wangfu91/ntfs-mft) | wangfu91 publisher; verify LICENSE copyright line and fixture provenance | MIT | Preserve MIT notice; separately account for test fixture notices | Potential raw-MFT experiment; not approved |
| [bacongobbler/treemap-rs](https://github.com/bacongobbler/treemap-rs) | bacongobbler publisher; verify LICENSE copyright line | MIT | Preserve copyright and permission notice | Optional squarified-layout helper; not approved |
| [.NET / WPF distribution](https://github.com/dotnet/core/blob/main/license-information.md) | .NET source repositories use MIT; Windows product distributions use the .NET Library License and include runtime packs | Windows product runtime terms and exact runtime-pack notices must be retained/reviewed for the selected deployment | Phase 1 UI baseline; no runtime package added yet |
| [Windows App SDK 2.6.5](https://www.nuget.org/packages/Microsoft.WindowsAppSDK/2.6.5/License) | Microsoft package terms; SDK source repository is MIT | Reviewed NuGet package terms contain data-collection and distribution language; requires product/legal clearance against the no-automatic-transmission requirement | Not selected for the baseline; reconsider only after clearance |
| [SQLite core](https://www.sqlite.org/copyright.html) | SQLite authors; public-domain dedication | Core code may be copied, modified, sold, and distributed; keep provenance and inspect wrapper/build package terms separately | Proposed embedded database; no code added |
| [rusqlite / libsqlite3-sys](https://github.com/rusqlite/rusqlite) | Rust wrapper contributors | MIT crate terms; bundled SQLite is public domain; pin and review exact dependencies/notices | Possible Rust binding; not approved |

## Dependency approval checklist for Phase 1

1. Pin the exact release/tag/commit and crate/package version.
2. Read the LICENSE, copyright headers, NOTICE files, bundled assets, code generation outputs, and dependency manifests at that exact revision.
3. Scan every direct and transitive dependency; reject unlicensed and incompatible components.
4. Record source URL, version, copyright holder, license identifier, modifications, and required notices.
5. Include notices in the installer and in an offline About/Licenses screen.
6. Generate the final register from Cargo.lock, NuGet lock files, and any frontend lock file in CI.
7. Re-run the audit whenever a dependency or bundled runtime changes.

## Shipping decision

No third-party source library, parser, or geometry component is selected or approved for reuse. The product packaging decision is .NET 10 LTS self-contained x64, but the exact embedded .NET Desktop Runtime pack, redistribution terms and notices have not yet been cleared for distribution. SQLite is the recommended database reference pending technical validation; the core is public domain, while any wrapper still requires an exact dependency audit. Use upstream work as technical evidence and implement the initial architecture independently unless a later review approves a narrow component after code, correctness, maintenance, dependency and license checks.

