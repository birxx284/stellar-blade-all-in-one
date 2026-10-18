# Testing

Build on Windows using `build.ps1`. Save fixtures are not included in this repository.
Use a compatible local test save; never publish your own save or account identifier.

From the repository root, example commands:

```powershell
.\dist\StellarBlade-AllInOne.exe --test C:\TestSaves\sample.sav C:\TestSaves\save-results.txt
.\dist\StellarBlade-AllInOne.exe --open-test C:\TestSaves\sample.sav C:\TestSaves\open-results.txt
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
