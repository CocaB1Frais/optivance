# OPTIVANCE — Performance Analysis

**Source audit date:** 2026-11-06  
**Decision/document alignment reviewed:** 2026-11-10  
**OPTIVANCE measurements:** None. The current repository contains Phase 0 documentation only; there is no application executable or scan engine to measure.

WPF + Rust and .NET 10 self-contained x64 are approved architecture decisions, not demonstrated performance results. Performance and compatibility validation remain mandatory before claims or guarantees.

## Evidence reviewed

The strongest detailed public benchmark methodology found was in [DiskTree’s README](https://github.com/dmoore-dwmmholdings/disktree). The author reports tests on a Ryzen 9 7950X3D, NVMe, NTFS, including:

- A 244,164-file / 8,678-directory subtree: 5.27 s cold versus 0.07 s warm for directory traversal.
- A 2 TB volume with a 6.91 GB MFT and about 5.54 million files: 10–21 s for its MFT scan.
- Its MFT I/O experiment reports about 1.77 GB/s with one concurrent reader, peaking around 3.61 GB/s at eight, then declining at sixteen.

These are **upstream measurements**, not independently reproduced results and not OPTIVANCE targets. The project itself warns that filesystem-cache state makes warm measurements misleading. Its README also explains why raw directory-walk and MFT totals can differ because of hardlinks, access permissions, and reparse points.

Other projects publish claims that are not directly comparable:

- [NeoTreescan](https://github.com/neortal-technologies/NeoTreescan) describes 30–90 seconds cold and 10–30 seconds warm for a million-file SSD scan, while its MFT implementation is a stub.
- [Ferris Scan](https://github.com/Vnilabean/ferris-scan) publishes a 5.2-second scan and 78 MB memory figure on a Samsung 980 PRO, while its README says MFT support is still in development. Its exact cold/warm and correctness methodology is not enough to compare against MFT scanners.
- [eDirStat](https://github.com/xangelix/edirstat) states relative speedups in its README, but the reviewed statements do not establish matching hardware, cache state, scan scope, or validation criteria.
- [WinBlaze](https://github.com/marksmayo/WinBlaze) and [TreeLens](https://github.com/skyflyt/treelens) document MFT-centric features and benchmark plans; project statements are not independent measurements.

## Performance implications

1. **I/O often dominates directory scans.** A large thread pool can help on devices with many independent metadata requests, but it can also worsen contention, cache pressure, and HDD latency. Worker count must be bounded and profile-driven.
2. **MFT throughput depends on MFT size and layout.** A scan reads metadata records, including unused/deleted slots in some readers. A large MFT can take longer even when live file count is modest.
3. **Warm/cold cache results must be separated.** Repeating a scan without documenting cache conditions can turn an I/O benchmark into a memory-cache benchmark.
4. **Index construction, hierarchy aggregation and UI delivery are separate costs.** “Scan finished” should not mean the UI still has millions of objects to instantiate.
5. **More threads are not a universal optimization.** HDD, SATA SSD, NVMe, CPU load, antivirus filter drivers, and per-file metadata calls behave differently.
6. **Idle use must be measured independently.** An inactive application should not keep a scan worker, active sensor loop, high-frequency timer, directory watcher, or continuous animation running.

## Proposed benchmark plan

| Area | Workload and measurements | Conditions to record |
|---|---|---|
| Fallback traversal | Small/medium/large trees; millions of entries where fixture hardware permits; throughput, duration, errors | OS build, filesystem, device type, warm/cold state, file/directory counts, access level |
| MFT path | Same NTFS volume as walk; raw-MFT bytes, record count, parse time, hierarchy time, total time, error/skip count | MFT size/fragments, device, privilege, concurrent mutation, parser version |
| Index | Insert/build and bottom-up aggregate at increasing entry counts; peak RSS and allocations | Node layout, path storage policy, hardlink behavior |
| Search | Name/path/extension filters on in-memory index; p50/p95 latency, cancellation response | Dataset size and filter |
| Treemap | Layout, draw, hit-test, pan/zoom and resize on skewed and million-item data | Visible child count, frame time, UI thread time, allocation rate |
| Duplicate finder | Size grouping, prefix hash, full hash, read bandwidth, cancellation | File size distribution, cache state, hashing algorithm |
| UI responsiveness | Input, scroll, select and cancel while scan is active | Frame/input latency and peak UI thread work |
| Monitoring | Sampler disabled, page visible, widget visible/hidden, different refresh intervals | CPU/RAM/process count; 1-second or slower data sources as appropriate |
| Idle | App open with no scan and no monitoring; then main window minimized | CPU average/max over fixed window, RAM, handles/threads, disk writes/network activity |

Do not flush Windows caches or use privileged cache-dropping tricks as a release benchmark default. Report both naturally cold and warm conditions, clearly describing how each was obtained. Run on HDD, SATA SSD, and NVMe where available. Compare only with identical target paths, privilege, exclusions, tool versions, and result correctness checks.

## Initial performance budgets (proposed, not measured)

- No automatic scan or per-volume indexing at startup.
- No permanent background scanner/service.
- Widget and Windows startup are disabled by default. Monitoring is stopped when there is no active consumer unless the user explicitly enables background monitoring; widget and main page share one sampler.
- Use bounded worker pools, bounded channels, and explicit memory limits.
- Retain the canonical file index in compact engine structures. Query visible directory rows and treemap geometry; do not create one managed UI object per file.
- Avoid full file reads during normal storage scans.
- Do not save the complete per-file index to history by default.
- Refresh CPU/GPU/disk/network samples at a user-configurable low cadence; a 2–3 second default is a proposal to validate, not a measured optimum.
- Present throughput and ETA only after a stable sample window; label estimates and suppress them when too little work has been observed.

## Performance profile policy

ECONOMY, BALANCED and MAXIMUM PERFORMANCE are required V1 profiles. BALANCED is the recommended initial profile; use fixed, predictable and bounded settings as the baseline. An adaptive scheduler is conditional, not guaranteed for V1: include it only if benchmarks demonstrate a real benefit, sufficient reliability and no significant regression against the static baseline. Profile changes must not affect correctness and all adjustments must be observable and cancellable.

## Reporting rule

Every published benchmark must identify hardware, OS/build, filesystem, volume and MFT sizes, file/directory counts, cache state, privilege level, method, duration, peak RAM, average/peak CPU, disk activity, errors/skips, and whether the scan totals passed a correctness comparison. No claim that OPTIVANCE is faster than WizTree, TreeSize, WinDirStat, or another tool should be made without such a comparison.

