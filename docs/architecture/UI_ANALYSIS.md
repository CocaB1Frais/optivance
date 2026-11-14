# OPTIVANCE — UI Analysis

**Source audit date:** 2026-11-06  
**Decision/document alignment reviewed:** 2026-11-11 (D-17: no dedicated Dashboard)  
**Visual reference:** TreeLens, as requested. This is a reference for hierarchy, navigation and storage visualization only; do not reuse its branding, protected assets, or code without separate license review.

## Reference findings

[TreeLens](https://github.com/skyflyt/treelens) documents a Rust core, Tauri 2, TypeScript frontend and WebView2; the design includes drive overview cards, a canvas treemap, virtualized folder/file details, breadcrumbs, search, keyboard navigation, scan status, and a local-only policy. Its 2026 roadmap describes keeping the large tree in Rust and returning small queries and progress events over IPC. Its MFT path is new and must be tested independently.

[WinBlaze](https://github.com/marksmayo/WinBlaze) is a second useful comparison: Rust engine plus C++/WinRT WinUI 3, with Direct2D treemap drawing and live tree results. [NeoTreescan](https://github.com/neortal-technologies/NeoTreescan) shows a .NET/WPF core/UI split and custom-rendered treemap; its MFT scanner remains a stub. [DiskSleuth](https://github.com/Swatto86/disksleuth) documents a Rust arena model and virtualized tree, but also auto-scans at startup, which OPTIVANCE must not do.

## UI requirements derived from the brief

- Keep Storage, Cleaning, Monitoring, Optimization, History and Settings as distinct navigation destinations; there is no dedicated Dashboard (D-17). Storage is the landing page, a global activity status bar is visible from every page, and the floating widget stays outside the navigation.
- The first release UI should be functional and legible, with progress, cancellation, inaccessible-path counts and error details visible.
- Display MFT, fallback walk, and partial/incomplete state explicitly for each scan.
- Let directory-tree and treemap selections synchronize through stable engine IDs, not path-string equality.
- The Storage page must load children on demand, virtualize long lists, render the treemap as a single custom surface, and provide breadcrumb and keyboard navigation.
- Accessibility is required: keyboard navigation, accessible labels, respect for Windows contrast settings, and no information conveyed only by color.
- Search should query the core index and stream bounded pages; it must not traverse files again for every text change.
- Cleaning must show every candidate path, why it was selected, the risk/explanation, and explicit per-file opt-in before a deletion summary and confirmation.
- Show unavailable sensor readings as unavailable, never as invented values.
- Do not run a scan automatically on launch. Do not block the UI thread during scans, search, aggregation, or export.

## Phase 0 UI framework comparison (historical)

The comparison documents the Phase 0 evaluation; WPF + Rust is the approved stack. The other rows are not open product alternatives and must not be treated as a pending framework choice.

| Approach | Strengths | Costs / risks | Audit view |
|---|---|---|---|
| Rust UI (egui/Tauri-style) | Engine and app logic share Rust types; small native backend; relevant open-source examples; fast iteration for engineering UI. | egui requires custom accessibility/native-control work; Tauri adds WebView2/JavaScript and UI supply-chain/build layers; either needs care to keep all file data out of per-widget object graphs. | Viable, but Tauri introduces an additional frontend stack and egui is less aligned with the requested Windows-native product. |
| C# WPF | Mature Windows desktop model, XAML, data binding, custom DrawingVisual rendering, familiar tools; a core/UI project split is straightforward. Microsoft documents drawing objects as lightweight and offers UI virtualization, although it does not provide data virtualization automatically. | Older visual system than WinUI 3; style and accessibility need product work; self-contained .NET distribution increases package size; developer must request data from engine in pages. | Selected UI under the validated C# WPF + Rust architecture and .NET 10 LTS self-contained x64 deployment decision. NeoTreescan is an upstream example, not proof of the target app’s performance. |
| C# or C++/WinRT WinUI 3 | Microsoft’s current recommended native UI framework; current Windows 11 design, XAML, C#/C++ options. | Windows App SDK package terms and runtime deployment/package identity choices add clearance, installer, servicing, and support work. | Comparative record only; not selected. Do not reopen the approved WPF + Rust stack without an explicit user decision and solid technical justification. |
| PySide6 + native Rust/C++ scanner | Rapid UI prototyping, large widget ecosystem, clear engine boundary possible. | Adds Python/Qt runtime and packaging work, larger distribution footprint, and risks duplicating large file indexes in Python/Qt objects. | Not recommended for the “extremely lightweight” target unless team experience makes it materially faster and a prototype proves acceptable memory. |

Microsoft recommends WinUI 3 for new Windows desktop applications, and its [deployment guidance](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/use-windows-app-sdk-run-time) describes the runtime initialization required for unpackaged applications. The current Windows App SDK NuGet package terms include data-collection and distribution language that requires review against OPTIVANCE’s no-automatic-transmission requirement; Microsoft’s [SDK repository](https://github.com/microsoft/WindowsAppSDK) separately says private builds do not collect data, which does not settle the terms or behavior of a commercial distribution. This is a clearance risk to resolve, not evidence that a shipped app automatically transmits scan data. For WPF, Microsoft documents [lightweight drawing objects](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-2d-graphics-and-imaging) and [UI versus data virtualization](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-controls). These are platform facts, not application benchmarks.

## Approved UI architecture and implementation constraints

Approved baseline: a C# WPF shell over a Rust engine worker, deployed as .NET 10 LTS self-contained x64. This is the selected product architecture, not a claim of proven memory use or rendering speed. Performance, compatibility, accessibility and cancellation still require technical validation. Keep scan/index operations and high-cardinality data in Rust; send versioned commands, progress and bounded query results to the UI. A custom treemap surface should draw batches of rectangles, not instantiate one framework control per file. Use UI virtualization for visible tree rows and engine-side pagination for data virtualization. Set `DOTNET_CLI_TELEMETRY_OPTOUT=1` in development and CI to disable [.NET SDK CLI telemetry](https://learn.microsoft.com/en-us/dotnet/core/tools/telemetry); this is a build-tool setting and does not substitute for auditing the application’s own network behavior. The Windows 11 context-menu requirement is a separate native Shell/packaging component and does not reopen the approved WPF choice.

Make the interface deliberately plain in Phase 1. Build the layout, drive selector, scan controls, progress/error pane, tree, and treemap interactions first. Revisit polish only after large-result rendering, cancellation, accessibility, and test-fixture behavior are reliable.

## UI implementation and validation work

- Verify the exact notices/licenses for the selected self-contained .NET 10 x64 runtime pack and its signed package.
- Validate the approved .NET 10 self-contained package, runtime notices, accessibility behavior, large-tree virtualization, treemap responsiveness, and OS matrix in `ARCHITECTURE_VALIDATION_PLAN.md`.
- The widget remains in the WPF application process and uses the shared sampler; whether it is a separate top-level WPF window is an implementation detail, not a product arbitration.
- The accessibility baseline is approved: keyboard navigation, accessible labels, Windows contrast settings, and no color-only information. Verify it in the tree, treemap, widget and Settings.

## Phase 0 review — optional Windows context menu

The user has confirmed the modern Windows 11 main-menu path via `IExplorerCommand`. Microsoft documents that it needs a native COM DLL and package identity, registered as a `windows.fileExplorerContextMenus` extension. A WPF/Win32 app can use a sparse package for identity while retaining a classic installer, but that adds COM, signing and package registration/removal lifecycle. Windows 10 does not provide the same modern menu extension; the proposed compatibility route is an opt-in per-user Win32 verb in the classic menu. The shell handler must remain fast and only start/activate OPTIVANCE after invocation; it must never scan or delete inside File Explorer. See [Microsoft’s modern context-menu guide](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer), [identity guidance](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps), and [legacy verb documentation](https://learn.microsoft.com/en-us/windows/win32/shell/fa-verbs).

**Product decision:** modern `IExplorerCommand` on Windows 11 and a per-user classic verb only on Windows 10 Enterprise LTSC 2019; integration is off until explicit consent and controlled from Settings. Commands apply only to recognized folders and volumes; unsupported target types are hidden, and “Analyze with OPTIVANCE” opens Storage with confirmation. **Technical implementation remains to validate:** sparse package lifecycle, signing, installer coupling and Shell target validation. Refusal means no registration; disable/uninstall must remove OPTIVANCE entries cleanly.

