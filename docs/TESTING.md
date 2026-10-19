# Testing

Build on Windows using `build.ps1`. Save fixtures are not included in this repository.
Use a compatible local test save; never publish your own save or account identifier.

From the repository root, example commands:

```powershell
.\dist\StellarBlade-AllInOne.exe --test C:\TestSaves\sample.sav C:\TestSaves\save-results.txt
.\dist\StellarBlade-AllInOne.exe --open-test C:\TestSaves\sample.sav C:\TestSaves\open-results.txt
.\dist\StellarBlade-AllInOne.exe --player-test C:\TestSaves\sample.sav C:\TestSaves\player-results.txt
.\dist\StellarBlade-AllInOne.exe --ui-test C:\TestSaves\sample.sav C:\TestSaves\ui-results.txt
.\dist\StellarBlade-AllInOne.exe --live-test C:\TestSaves\memory-results.txt
```

The save suite was authored for the documented public research fixture (24 top-level
sections). Its EVAS CRC oracle is the independently computed Python zlib checksum
`F739AEB9` for that exact fixture. A different fixture can fail those known-reference
assertions without indicating a regression. See `EvasTests.cs` and the notices.

The save suite checks byte-identical no-edit round trips; quantity, outfit and
collection edits; unchanged world/quest data; unique GUIDs; idempotence; permanent
backups; moved and changed sources; destination collisions; and EVAS integrity.

Opening checks cover quoted paths, file URIs, environment variables, missing files,
folders, empty saves, double extensions, wrong save classes and CRC errors.

Compact UI checks exercise shared gold/item/outfit edits, closing/reopening windows,
review invalidation, minimum window size and child-window disposal.

Memory checks use an independent local fixture process, not the game. No automatic
Stellar Blade memory profile is validated. These checks do not replace in-game testing.

Player checks cover UInt32 core level arrays, case-insensitive core aliases, core
budgets and limits, unchanged current gauges and actor fields, skill point bounds,
GVAS/EVAS output integrity, combined gold/item/currency edits, permanent backups,
no duplicate currency on repeated edits, NieR prerequisites, and shared player UI
state across hide/reopen, discard, another save and main-window close.

Capacity levels normally consume existing held cores. The optional 0.5.2 grant
mode supplies and spends only missing upgrade credits; this never marks bodies
as collected. The normal/NG+ level cap remains in place. These operations are
not validated in a running game on this PC. Verify actual capacity, remaining
cores, equipped Burst gear, SP spending, an Emil purchase, saving and reloading.

0.5.1 direct-save regression checks click the real Save copy button and substitute
only the destination picker and notification messages. They read the actual output
save to check unprepared gold, consumables, SP and Stellar Tears from simultaneous
windows; original backups; unchanged-copy saving; destination cancellation; invalid
edit rejection; and fresh values after a previous review. No manual review click
is required. Save parsing, CRC and permanent-backup code are unchanged.

0.5.2 adds --capacity-test <sample.sav> <report.txt>. It constructs low-capacity
fixtures with no spare cores; checks explicit versus disabled grant mode; exact
missing-credit reporting; GVAS/EVAS integrity; actual Burst gear identity and
other-slot preservation; unlocked slot checks; idempotence; direct Save copy;
unchanged world/quest/skill/NG+ data and permanent original backups.
Core levels are still capped. Burst bonuses are applied through equipment, not
by writing current energy as a fake maximum. In-game verification is pending.

0.6.0 adds --guide-test <sample.sav> <report.txt>. It checks guide coverage,
offline browsing, accent-insensitive search, reference-only checkbox guards,
Angel accessory preparation using the real Save copy button, actual inventory
and ownership/acquisition output, permanent backup bytes, preservation of all
unrelated sections, external-mod base item preparation and read-only unknown IDs.
No external source button is clicked and no mod is downloaded during these tests.
These checks do not validate the in-game appearance or external-mod compatibility.

0.7.0 adds --equipment-test <sample.sav> <report.txt>. It checks the equipment
allowlist against actual inventory facts; Mk2 grants; exospine identity and level
synchronization; missing material creation; actual weapon core costs and explicit
quantity precedence; normal/NG+ caps; repeat operations; duplicate/template guards;
world/item-pocket/quest/story preservation; direct UI saving; shared gold/SP;
hide/reopen, discard, base-save switching and permanent backups in GVAS/EVAS.
The fixture supplies test data only and is not distributed. In-game validation
of equipment effects, weapon damage, saving and reloading is still required.
