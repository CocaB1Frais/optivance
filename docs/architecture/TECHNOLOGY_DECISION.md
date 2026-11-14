# OPTIVANCE — Technology Decision

**Decision status:** WPF + Rust and .NET 10 LTS self-contained x64 are validated; no stack change is proposed. Packaging/signature details still need technical validation.  
**Decision date:** 2026-11-10

Windows platform policy is confirmed for v1: maintained Windows 11 x64 is the primary and development reference; Windows 10 Enterprise LTSC 2019 x64 (1809) is the secondary reference; LTSC 2021 x64 (21H2) is conditional only while the installation actively receives applicable security support. IoT Enterprise LTSC, Windows 10 Home/Pro, non-LTSC Enterprise and LTSB 2016 are outside general v1 support. WPF + Rust and .NET 10 LTS self-contained x64 are approved; packaging, licensing and runtime validation remain outstanding.

## Approved architecture baseline

- **Scanning/analysis core:** Rust stable, 64-bit Windows target; no kernel driver.
- **Desktop UI:** C# with WPF and XAML, .NET 10 LTS self-contained x64; the .NET Desktop Runtime 10 is embedded and need not be installed separately. Keep UI drawing and data requests virtualized.
- **Explorer integration:** separate Shell component; modern `IExplorerCommand` with package identity for Windows 11, and an opt-in classic per-user verb for Windows 10 Enterprise LTSC 2019. Do not host scanning/cleaning in Explorer. If integration is unavailable or fails, the main app remains usable.
- **Core/UI communication:** versioned local Windows Named Pipe protocol, bounded paged queries and progress events, with validation of versions, sizes, messages and allowlisted commands. One Rust engine is shared by the main app and widget; a worker crash leaves the UI open with a clear error.
- **Privileged operations:** one-shot, allowlisted elevated helper launched only after the user requests an MFT scan that requires elevation. No UAC at startup, permanent service or elevated UI.
- **Persistence:** SQLite is the recommended reference for compact local history/configuration, pending technical validation; migrations and retention remain required.
- **Treemap:** engine-side squarified layout/geometry with a single custom UI surface and hit testing; small geometry library may be evaluated but is not yet approved.
- **Fallback scan:** native Win32 directory enumeration with bounded concurrency and safe reparse behavior.
- **MFT:** separate opt-in prototype; do not commit to a production parser until correctness/performance gates pass.

## Phase 0 technology comparison (not an open stack selection)

This table records the earlier comparison only. The approved baseline is option A (Rust core + C# WPF, .NET 10 self-contained x64); options B and C are not active alternatives. Reopening the stack requires an explicit user decision and solid technical justification.

| Criterion | A — Rust core + C# WPF shell | B — Python/PySide6 UI + native scanner | C — Rust core + C++/WinRT WinUI 3 shell |
|---|---|---|---|
| Performance | High potential for compact index, parser and I/O control; UI choice still matters. | Native scan can be fast, but Python/Qt models can duplicate data and add runtime overhead. | Excellent if Rust owns scanning and UI stays virtualized; C++ engine increases unsafe-code and maintenance burden. |
| Memory | Rust engine can use compact arenas; measure WPF runtime and IPC. | Highest risk of extra runtime/object overhead for a very large inventory. | Native engine/UI may reduce managed runtime overhead, but no real footprint is measured. |
| Startup/idle | Rust worker can remain absent until requested; WPF startup and idle memory must be measured. | Python and Qt runtime load even while idle; package startup time needs measurement. | Windows App SDK runtime/package deployment adds startup and servicing considerations. |
| Development complexity | Moderate: Rust + C# boundary and Windows APIs, but each side has a clear role. | Fastest prototyping for Python-skilled teams; complex packaging and interop later. | Highest complexity with C++/WinRT, Rust FFI, or mixed C++/C# ABI surfaces. |
| Windows integration | Strong through Rust Windows APIs and .NET/WPF interop. | Possible via native module and Qt, but less direct for shell integration. | Strong direct Win32/WinUI integration. |
| Maintenance | Clear engine/UI boundary; Rust and .NET toolchains both need support. | Three runtime ecosystems and bindings; Python dependencies need packaging discipline. | C++/WinRT toolchain and Windows App SDK versioning add specialized maintenance. |
| Packaging | Worker + per-user classic installer; self-contained .NET 10 x64 is decided and includes exact runtime notices. | Python/Qt plus native extension packaging is heavier and more failure-prone. | Windows App SDK runtime packaging and terms require clearance. |
| Dependency footprint | Moderate; a self-contained .NET deployment may enlarge the installer. | Larger by design; avoid unless a prototype proves otherwise. | Native engine can be lean, but Windows App SDK deployment can materially enlarge or complicate packaging. |
| UI flexibility | WPF XAML supports a custom treemap surface and can consume the stable worker protocol. | High widget flexibility, but not as native Windows in feel. | High Windows fit, less portability. |
| Security | Rust reduces memory-safety exposure in the complex file parser; still validate all data and the IPC boundary. | Python scan control can be safe but uses a larger dependency/runtime surface. | C++ raw parsing places greater memory-safety burden on the team; a Rust engine avoids most parser code in C++. |
| Testing | Engine can test headlessly; UI and protocol separately. | Core/native boundary is testable, but runtime packaging adds more cases. | Strong native test options, but ABI and framework integration increases setup. |

## Rationale for the approved stack

The complex and failure-prone part is reading millions of filesystem records and building a compact index. Rust gives the project a strong type and memory-safety baseline for that work without requiring the UI to share Rust data structures. C# WPF is the selected Windows desktop XAML shell with lightweight drawing and UI virtualization; a local worker boundary keeps the UI responsive and limits cross-language data to commands and visible result pages. The product stack is approved, while its implementation remains pending the separate validation gate and explicit Phase 1 authorization.

WinUI 3 is retained only as a documented comparison, not an open alternative. Its Windows App SDK packaging/terms do not justify reopening the approved WPF stack; doing so would require a separate explicit decision and solid technical evidence. Microsoft supports .NET 10 LTS until November 2028; its Windows table lists x64 Windows 10 21H2, 1809 and 1607 for the LTSC/Enterprise group and Windows 11 releases that must still be maintained at release time. .NET 9 STS and .NET 8 LTS reach end of support in November 2026, so .NET 10 remains the selected modern runtime for this WPF + Rust architecture. WPF requires .NET Desktop Runtime, not only the base runtime; .NET 10 LTS self-contained x64 embeds it. Runtime patch servicing and the exact runtime-pack redistribution licenses/notices remain to be validated before distribution. For v1, Windows 10 Enterprise LTSC 2019 (1809) is the secondary reference; Enterprise LTSC 2021 (21H2) is conditional only while the installation actively receives applicable security support. IoT Enterprise LTSC, Home/Pro, Enterprise non-LTSC and LTSB 2016 are outside general v1 support. See Microsoft's [supported .NET on Windows matrix](https://learn.microsoft.com/en-us/dotnet/core/install/windows), [.NET release lifecycle](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support), [Windows 10 servicing table](https://learn.microsoft.com/en-us/windows/release-health/release-information), and [Enterprise LTSC 2019 lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/windows-10-enterprise-ltsc-2019). A modern Explorer command can be implemented as a separate native DLL plus package identity; it does not require replacing WPF with WinUI 3. Set `DOTNET_CLI_TELEMETRY_OPTOUT=1` for development/CI to suppress .NET CLI telemetry; the SDK setting does not control the shipped application's network behavior.

WPF + Rust remains the approved UI/engine stack. .NET 10 LTS self-contained x64 is the approved deployment decision. WPF supports lightweight drawing objects and UI virtualization, but the engine must still provide data virtualization. WinUI 3 is not under consideration unless the user explicitly reopens the stack decision based on solid technical justification. Python/PySide6 is not recommended for the shipped app. The approved stack still requires performance, compatibility and resilience validation before any guarantee.

## MFT recommendation

Start with a native Win32 fallback traversal to establish correct size, hardlink, reparse-point, cancellation and error semantics. Then prototype:

1. FSCTL_ENUM_USN_DATA enumeration with file-info queries for sizes.
2. A read-only raw MFT parser such as wangfu91/ntfs-mft, after source/dependency review.

Keep both behind one scanner interface and an elevation helper invoked only after an explicit scan request when that MFT path requires UAC. A refused UAC falls back to the non-elevated directory walk. The separate USN change journal is for optional incremental change tracking, not a replacement for a full scan. Do not prompt at startup, ship a kernel driver, or install a permanent service.

## Dependencies not yet approved

No external crate, NuGet package, or treemap library is selected. SQLite is the recommended local database reference pending technical validation; its core is public domain, but any Rust wrapper and its transitive dependencies need exact-version review. WPF + Rust and .NET 10 self-contained x64 are approved architecture decisions; exact runtime-pack redistribution terms/notices, signature and packaging still need review before distribution. Any MFT parser or geometry crate must pass technical, security, license and maintenance review.

