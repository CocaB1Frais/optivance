# OPTIVANCE — Repository Comparison

**Audit date:** 2026-11-06. Findings below summarize upstream documentation and visible repository structure. Project benchmark claims are not independent OPTIVANCE measurements.

## Product and architecture comparison

| Project | Core and language | Scan/index method | Visualization and interaction | Key evidence / limitation |
|---|---|---|---|---|
| [WinBlaze](https://github.com/marksmayo/WinBlaze) | Rust core + C++/WinRT WinUI 3 | Direct raw MFT plus parallel walk fallback; compact persistent index | Progressive folder tree, Direct2D treemap, search | Strongest example of a Windows-native hybrid. README documents unfinished batching work; treat throughput assertions as self-reported. |
| [DiskTree](https://github.com/dmoore-dwmmholdings/disktree) | Rust CLI/TUI | Parallel directory walk plus raw MFT stream reader | Terminal directory tree; no desktop treemap | Detailed size semantics and cold/warm performance notes. Not a desktop UI template. |
| [DiskSleuth](https://github.com/Swatto86/disksleuth) | Rust workspace; egui frontend | jwalk + Rayon; optional FSCTL_ENUM_USN_DATA; arena nodes | Virtualized tree, progressive treemap, exports and duplicate funnel | Useful engine/UI boundary. Startup auto-scan conflicts with OPTIVANCE’s explicit-scan principle. |
| [RustySpaceSniffer](https://github.com/GRodolphe/RustySpaceSniffer) | Rust crates + egui | Directory walk and an MFT path; USN watcher for live changes | Live treemap, filter DSL, tags, snapshots | README says MFT runtime validation is pending. Do not treat “implemented” status as proven compatibility. |
| [TreeLens](https://github.com/skyflyt/treelens) | Rust + TypeScript/Tauri 2 | Parallel walk; newer NTFS MFT fast path | WebView2 canvas treemap, virtualized details, tabs, search | Best visual reference per user. Use interaction patterns, not assets or code. New MFT path and unsigned releases merit independent evaluation. |
| [eDirStat](https://github.com/xangelix/edirstat) | Rust + egui | Work-stealing scan, arena, NTFS MFT scanner; snapshots | Interactive treemap, stats, duplicate analysis | Active release history and broad feature set. README benchmark claims lack enough shared test conditions for selection. |
| [NeoTreescan](https://github.com/neortal-technologies/NeoTreescan) | C#/.NET 9 + WPF | Win32 FindFirstFileExW; limited top-level parallelism; MFT class is a stub | Folder tree, custom WPF treemap, file type/age/size analysis | Useful WPF example. Its own architecture is core/UI separated, but current MFT and large-file limits matter. |
| [WinDirStat](https://github.com/windirstat/windirstat) | Native C++ Windows solution | Classic filesystem scan; no MFT fast path documented in the reviewed README | Tree list, treemap, cleanup | Mature benchmark for user expectations; GPL-3.0-or-later for the application. |
| [SquirrelDisk](https://github.com/adileo/squirreldisk) | Rust desktop app | Cross-platform scanner; linked-folder and cloud-placeholder handling | Sunburst and treemap, collection/cleanup | Good examples of interaction and protection warnings. AGPL, online/update/cloud features, and erase messaging do not fit direct reuse. |
| [Ferris Scan](https://github.com/Vnilabean/ferris-scan) | Rust library with TUI and GUI | Parallel directory walk via jwalk/Rayon | Directory tree and treemap | README says MFT support is in development; published figures are not enough for a comparable benchmark. |

## What appears reusable in principle

- **Rust arena/index approach:** Multiple repositories store compact nodes in vectors and use integer parent/child references. Adopt the idea, then benchmark our data layout; do not copy implementation without reviewing each license and test.
- **Directory-walk pattern:** Use a bounded worker pool and native wide-character Windows enumeration. Preserve per-path errors and never recurse through reparse points by default.
- **Progressive, virtualized UI:** Send batches/events and render visible rows/rectangles only. Keep millions of records out of WPF/WinUI view-model object graphs.
- **Treemap geometry:** A squarified algorithm can be implemented independently or adapted from MIT-licensed treemap-rs with its notice. The library alone does not solve hit testing, virtualization, zoom, or accessible selection.
- **Scanner evidence and caveats:** DiskTree’s documentation is especially useful for recording cache state, hardlink differences, and size definitions. Treat its measured numbers as a test design example only.

## What should not be adopted directly

- GPL/AGPL code or artwork for a proprietary distribution without a deliberate license decision.
- TreeLens or SquirrelDisk network/update/cloud pathways; OPTIVANCE’s baseline is local/offline and has no automatic updates.
- Any project’s automatic startup scan; OPTIVANCE must wait for an explicit scan request.
- A raw MFT parser solely because it returns a fast scan on one machine. Verify all records, IDs, hardlinks, streams, fragments, errors, and concurrent mutation behavior.
- UI object-per-file approaches or “one chart per data node” rendering.
- Benchmark numbers without hardware, OS, dataset, cache state, scan scope, correctness criteria, peak memory, and tool versions.

## Relative fit for OPTIVANCE

1. **Best overall architecture reference:** WinBlaze’s Rust scanning core + separate Windows UI.
2. **Best visual interaction reference:** TreeLens, per the product brief; keep the reference conceptual and asset-free.
3. **Best documented MFT/walk comparison:** DiskTree. Its size-accounting notes make clear that a fast inventory still needs explicit semantics.
4. **Best WPF implementation reference:** NeoTreescan, with the important caveat that the MFT scanner is a stub and the package is sizeable.
5. **Best candidate MFT libraries to prototype:** wangfu91/ntfs-mft for direct raw-volume read experiments; wangfu91/usn-journal-rs for documented Windows enumeration/journal APIs. Neither is approved for production at this stage.
6. **Best simple treemap crate candidate:** treemap-rs, subject to a test against skewed sizes, zero-sized items, stable ordering, and very large child counts.

## Benchmark interpretation

Only DiskTree published a reproducible-looking combination of hardware, file counts, cache conditions, and MFT size in its reviewed README. It reports wide ranges and explicitly warns that warm scans can be misleading. These are maintainer results and were not reproduced. WinBlaze, eDirStat, NeoTreescan, Ferris Scan, and other projects publish their own feature/performance assertions; do not compare those values without reproducing conditions.

