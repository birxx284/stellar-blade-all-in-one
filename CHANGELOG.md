# Changelog

## 0.4.0

- Compact main window with direct gold editing and a shared pending-change summary.
- Separate consumable/item and outfit/collection windows. Closing them keeps edits.
- Consumables are shown first; category and search filters expose the other items.
- English UI, help, diagnostics, change reports and documentation.
- In-game controls load only when opened; their manual-calibration behavior is unchanged.
- Cache DLC availability while populating the outfit list to avoid repeated ownership scans.
- Additional window workflow checks cover shared edits, hide/reopen, review invalidation,
  minimum window size and disposal of auxiliary windows.
- EVAS v1 validation, CRC32 recalculation and permanent backups are retained.

## 0.3.3

- Added EVAS v1 reading/writing with CRC32 verification and recalculation.
- Preserved the original EVAS envelope and complete original backups.
- Rejected corrupt checksums and unknown EVAS versions explicitly.
- Successful operation reported by an end user on their PC.

## 0.3.2

- Full opening error report with copy/export actions.
- Pasted paths support Windows quotes, spaces, local file URIs and environment variables.
- Clear loaded/failed state and removal of stale pending edits after an opening failure.

## 0.3.1

- Improved save-copy destination handling and automatic unique copy names.
- Permanent, verified original backups on every save.
