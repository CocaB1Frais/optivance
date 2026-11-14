# OPTIVANCE — MFT Engine Analysis

**Source audit date:** 2026-11-06  
**Decision/document alignment reviewed:** 2026-11-10  
**Decision status:** MFT may be used for advanced scans. The backend remains unselected pending validation. Any UAC is one-shot and appears only after an explicit scan request if that selected MFT method requires it; no startup elevation or permanent service.

## Three mechanisms that must stay distinct

### 1. Windows FSCTL_ENUM_USN_DATA enumeration

Microsoft documents FSCTL_ENUM_USN_DATA as an operation that enumerates USN data between boundaries to obtain MFT records. It runs through DeviceIoControl on an NTFS volume handle; subsequent requests continue from the returned file-reference position. The volume must be NTFS. Microsoft’s documentation describes the operation, but the Rust wrapper reviewed for this audit says the operation requires an Administrator process.

The API returns USN record structures with a file reference, parent reference, attributes, name and journal metadata. Those record fields do not themselves provide the complete logical/allocated-size model required by OPTIVANCE. A design must decide how it obtains size and allocation data, for example by opening file IDs and querying file information, or by parsing records. The former can create many per-file API calls; the latter adds parser complexity.

References: [Microsoft FSCTL_ENUM_USN_DATA](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-fsctl_enum_usn_data), [MFT_ENUM_DATA_V0](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/ns-ntifs-mft_enum_data_v0), [USN_RECORD_V3](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/ns-ntifs-usn_record_v3), [FILE_STANDARD_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_standard_info), and [usn-journal-rs feature/privilege notes](https://github.com/wangfu91/usn-journal-rs).

### 2. Raw volume and direct $MFT parsing

A reader opens the volume for read-only raw access, reads NTFS geometry and the MFT stream, handles its data runs, and parses each FILE record. This can reduce directory-open overhead and permit extraction of file sizes from metadata, but makes OPTIVANCE responsible for validating untrusted on-disk structures and handling live mutation. Fragmented MFTs require following the MFT’s own runlist. Record parsing must validate update-sequence fixups, attribute lengths/bounds, runlists, record sequence numbers, attribute lists and extension records. A live scan is not an atomic snapshot; records can change while it runs.

[wangfu91/ntfs-mft](https://github.com/wangfu91/ntfs-mft) is a candidate implementation. Its README claims read-only raw volume access, fragmented-MFT support, file names/sizes/hardlinks/streams and serial/parallel scanning. Its documented caveats are material: volume scan defaults skip malformed/extension records, metadata enrichment is best effort, attribute-list enrichment follows one level, scan I/O errors end iteration, paths/tree construction belong to the consumer, ReFS is unsupported, and scans are not atomic. It is version 0.1 with no declared MSRV. This is a prototype lead, not evidence of production completeness.

[omerbenamram/mft](https://github.com/omerbenamram/mft) parses an MFT input/snapshot and exposes record attributes. It is useful for parser fixtures and independent cross-checks, but does not by itself acquire a live Windows MFT or provide the whole-volume scanner.

[Colin Finck’s ntfs crate](https://github.com/ColinFinck/ntfs) provides read-only NTFS filesystem and directory-index parsing. It is safe Rust, but its README lists compression, encryption, reparse points, security descriptors, journaling and caching as unsupported. It is not a drop-in WizTree-class inventory engine.

### 3. USN change journal

The USN journal records changes; it is not an authoritative full-volume snapshot and should not be confused with MFT enumeration. A journal can be missing, wrapped, reset, or have a gap since the saved checkpoint. OPTIVANCE must never create, delete, resize, or otherwise modify it during ordinary scanning. If incremental refresh is eventually added, store the journal ID and USN checkpoint, validate continuity, and fall back to an explicit full scan whenever continuity cannot be proven.

The [usn-journal-rs README](https://github.com/wangfu91/usn-journal-rs) documents USN_RECORD_V2/V3 iteration, FSCTL enumeration, path resolution, and Windows/admin requirements. It exposes useful APIs, but does not eliminate correctness work around checkpoint recovery, deleted names, hardlinks, or file-ID reuse.

## Proposed scan strategy

1. **Foundation:** Implement the unprivileged, cancellable Win32 directory walk first. Use it as the correctness reference and supported fallback on non-NTFS volumes and selected directories.
2. **MFT prototype:** Build an opt-in NTFS volume scanner behind a narrow read-only interface. Evaluate both FSCTL_ENUM_USN_DATA plus file-info queries and a raw MFT reader. Use a least-privilege helper only for explicitly authorized operations that require UAC; do not elevate the UI, prompt at startup, or install a permanent service.
3. **Cross-check:** Run the MFT path and directory traversal on the same fixture trees and safe test volumes. Compare file IDs, names, logical size, allocation size, parent relations, hardlinks, sparse/compressed/reparse metadata, inaccessible entries, and totals. Record every expected difference.
4. **Production gate:** Require extensive corpus tests, fuzzing/malformed-record resilience, cancellation, live-mutation tests, coverage on maintained Windows 11 x64 and the included Windows 10 targets (Enterprise LTSC 2019 x64; Enterprise LTSC 2021 x64 only while applicable security support is actively received), and measured performance/memory. IoT LTSC and the other excluded Windows 10 editions are outside the v1 support matrix. A successful parse of one system volume is not sufficient.
5. **Fallback:** If elevation is declined, the feature is unsupported, or a record cannot be safely interpreted, report that limitation and offer the ordinary walk. Never silently mix scan methods without labeling the result.

## Required identity and accounting model

- Use volume identity plus NTFS file reference number **and its sequence number**; do not use a path or filename as a record identity. Sequence numbers help detect reference reuse.
- Separate a unique file record from one or more file-name/link records. Keep every hardlink path visible and show its logical size. One deterministic representative path carries the unique weight in directory aggregates and treemap; mark all other paths as aliases with no second contribution. Recommended deterministic convention: canonical volume root and normalized separators, then the minimum absolute path under ordinal case-insensitive comparison, with ordinal case-sensitive comparison as tie-breaker. This must not depend on scan order and must be validated on NTFS.
- Keep directory hierarchy by parent file reference. Detect missing parents, stale references, cycles, duplicate names, and multiple namespaces; preserve recoverable orphan records in an error/unattached bucket.
- Track path aliases and reparse-point tags separately. Do not recursively follow symlinks, junctions, or mount points by default.
- Parse resident and nonresident data attributes, alternate data streams, sparse/compressed flags, logical EOF and allocated-size fields. NTFS Alternate Data Streams are excluded from ordinary totals; expose them separately as metadata only when reliably readable. Do not infer physical flash usage from logical or reported allocation values.
- Exclude deleted records from the ordinary inventory. If a future forensic view is added, label it separately and never merge it into current storage totals.
- Do not assume that one MFT record corresponds to one visible path or one unique physical extent.

## Size definitions

| Name | Meaning and use |
|---|---|
| Logical size | End-of-file byte length. Appropriate for file content and common Explorer comparisons. |
| Allocated size | Bytes the filesystem reports allocated to a stream/file. Windows FILE_STANDARD_INFO exposes AllocationSize separately from EndOfFile. Sparse, compressed, deduplicated and placeholder content may not map cleanly to physical media bytes. |
| Directory cumulative size | Derived rollup under a documented hardlink/stream policy. It is not a field read directly from a directory record. |
| Physical estimate | Optional and explicitly labeled estimate. Do not present it as exact device-cell consumption, especially for SSDs, deduplication, compression, cloud placeholders, or storage tiers. |

## Security and robustness requirements for the MFT path

- Open raw volumes read-only. Do not write or lock the volume, issue repair controls, or create/change journals.
- Validate every offset, length, integer conversion, record size, runlist, parent reference, and attribute before accessing data. Use checked arithmetic and bounded allocations.
- Treat names, paths, attributes, and records as untrusted input. A corrupt entry must become a structured scan error, not a panic or an unchecked path action.
- Bound read buffers, worker counts, result queues, and per-record allocations. Cancellation should stop between bounded reads/batches.
- Log record IDs and error types by default; avoid logging full personal paths unless diagnostic mode explicitly enables it.
- Keep all elevated operations behind an allowlisted, one-shot helper. No generic command execution and no automatic ownership/ACL changes.

## Decision

**Recommendation:** use the standard directory walk as the non-elevated baseline and fallback. Evaluate an optional NTFS MFT path separately; invoke a limited helper with UAC only after the requested scan needs that access. Evaluate the maintained Windows API route and raw reader independently; do not pretend FSCTL enumeration is identical to parsing raw $MFT. Do not ship a kernel driver or permanent service. Do not promise WizTree parity until measured and correctness-gated.

**Unresolved technical questions:** Which MFT route supplies sufficient size/allocation coverage with acceptable per-file overhead? Can an external parser ship after source and transitive-dependency review? Can ADS metadata be read reliably enough to expose separately, without entering ordinary totals? What target Windows builds and NTFS features are required? Hardlink visibility, representative-path weighting and single-count volume aggregation are approved product rules, not open questions.

