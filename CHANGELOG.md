# Changelog

## 0.7.0

- Added an equipment window with 24 verified exospines and 154 gear items, including 89 Mk2 variants. Normal saves cannot receive Mk2 through this feature.
- Added selected-item, all-Mk2 and all-supported grants. Exospine levels up to 3 are granted without module costs; existing higher levels are preserved. Equipped copies keep matching identity and level.
- Added missing core/material stack creation and minimum stock requests for 15 verified resources. Genuine same-save category templates and unique identities are required.
- Added direct weapon upgrades with 1 Weapon Core per level and official caps of 15 normal / 40 NG+. Costs use the post-grant, post-quantity-edit stock; optional missing credits cover only the shortfall.
- Added Chill NG+ preset: Mk2 equipment, exospine level 3, HP/Beta 9, weapon 40, SP, gold and supplies. It prepares a copy without choosing a loadout or changing story, quests, skills or difficulty.
- Shared direct Save copy, hide/reopen, discard and permanent backup behavior includes the new window.
- Added GVAS/EVAS equipment and weapon workflow checks. Running-game validation remains pending.

## 0.6.0

- Added English acquisition descriptions, areas, locations, NG+/DLC requirements and source links for all 304 verified catalog entries. Existing item identifiers and catalog coverage are preserved.
- Added a detail panel, searchable acquisition text, offline browsing and per-item preparation.
- Highlighted Symbol of Legacy: prepare its real earrings, then equip them and enter Tachy Mode to use the Angel / Fusion appearance.
- Added reference-only guides for permanent Angel via the author's separate Red Passion replacement mod and the story Tachy transformation. No external mod files are bundled or installed.
- Show uncatalogued cosmetic-like identifiers already present in the loaded save as read-only discoveries; they cannot be granted.
- Added a full offline cosmetic guide and regression checks for reference guards, Angel accessory preparation, direct saving, backups and unrelated-section preservation. In-game validation remains pending.

## 0.5.2

- HP/Beta selectors remain enabled without spare cores, with clear MAX/game-limit labels.
- Optional Grant missing cores supplies and spends only the missing upgrade credits. Existing-core mode stays available and does not invent world pickups.
- Added Burst Expansion Gear controls: add a supported variant to inventory or equip it in an existing unlocked gear slot. Other slots and the old gear are preserved.
- Rejected locked-slot bypasses, duplicate equipped copies, unsupported aliases and levels beyond the supported core cap.
- Clear separation between permanent capacity, current gauge fill, and Burst/Tachy story unlocks.
- Added GVAS/EVAS and direct-save regression checks for grants and Burst equipment. Running-game validation remains pending.

## 0.5.1

- Fixed the grey Save copy button: a valid loaded save now enables it immediately.
- Saving collects the current gold, item and player fields and validates a fresh copy automatically. Prepare and Review are optional.
- An unchanged save can be copied too. Cancelling the destination writes nothing.
- Invalid edits are still rejected; original protection, permanent backups and unique destination names are retained.
- Added direct-button regression checks covering unprepared values across windows, backups, cancellation, invalid edits and saving after an older review.

## 0.5.0

- Separate player window for HP/Beta core upgrade levels, unspent skill points and Emil’s Stellar Tears.
- HP/Beta upgrades consume 3 already-collected cores per level, preserving invested + remaining totals and world pickup states. Normal/NG+ caps are checked. Existing upgrades cannot be lowered.
- Numeric maximum stats are calculated in game. Burst/Tachy maximum capacity is not supported.
- Stellar Tears can be edited at zero or recreated from a same-save currency template when NieR save data exists. Dummy collectible counters and shop purchases stay unchanged.
- DLC currency and Fishing category filters are now directly available in the item window.
- Player changes share review, backup and save-copy handling with items and outfits. Other actor fields, XP, skills, story, world state and NG+ snapshots are preserved.
- Added structural GVAS/EVAS, core-budget, DLC availability and player-window checks. In-game validation remains pending.

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
