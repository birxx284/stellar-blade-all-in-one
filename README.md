# Stellar Blade — All in One

A portable Windows save editor with a compact interface for gold, existing item quantities, outfits, and collection records.

[**Download the Windows release**](https://github.com/birxx284/stellar-blade-all-in-one/releases/latest) · [Quick start](QUICKSTART.txt) · [Changelog](CHANGELOG.md)

![Compact main window](docs/main.png)

## What it does

- Edit gold from the main window.
- Open **Consumables and items** to edit existing consumables, ammo, materials, and supported currencies. Search and category filters keep the list focused.
- Open **Outfits and collections** for a catalog of 304 cosmetics, the 49-can collection with Black Pearl, and known NieR collection markers.
- Keep all pending edits in one session. Closing an auxiliary window keeps its selection; gold, item, and outfit changes are saved together.
- Read compatible **GVAS** and **EVAS v1** saves, validate EVAS CRC32 on opening, and recalculate it after editing.
- Create a permanent, verified backup of the complete original save every time a modified copy is saved. Existing files are never overwritten.

Optional in-game controls for money, health, energy and one consumable are included in a separate window. They require **manual memory calibration each session**. No automatic trainer profile is included.

## Use

1. Download and extract `StellarBlade-AllInOne-0.4.0.zip` from Releases.
2. Run `StellarBlade-AllInOne.exe`. No installation is needed; it can run from a USB drive.
3. Click **Open .sav**, **Paste path**, or **Find latest** on the PC containing your save.
4. Prepare your gold, item, or outfit changes. Close the auxiliary windows when finished; selections are preserved.
5. Click **Review changes**, then **Save copy** with the game closed.

The modified file is a new copy. A verified original backup and recovery note are saved in `CopiasSeguridad` beside the destination. This folder name is retained for compatibility with earlier versions.

Typical Steam save location:

```text
%LOCALAPPDATA%\SB\Saved\SaveGames\<SteamID64>\StellarBladeSave00.sav
```

Select a main save, not `StellarBladeSetting.sav`. Opening a save does not require the game to be installed on the same PC.

## Scope and limitations

Only supported items already present exactly once in the inventory can have their quantities changed. Separate equipped charges are preserved when increasing stock and clamped when reducing the total below them.

Outfit edits update inventory, ownership and known acquisition records. **Physical chests, quest completion, story state and Emil purchases are not changed.** NieR synchronization requires existing NieR save data; this tool does not activate DLC licenses. Other EVAS versions, invalid checksums, and unsupported structures are rejected with an opening report.

An end user reported successful operation of version 0.3.3 on their PC. Version 0.4.0 adds the compact English UI and separate windows. Automated tests check save structure, integrity, backups and the window workflow; they do not establish compatibility with every game build. Verify a modified copy in your own game before relying on it.

## Screenshots

### Consumables and items

![Consumables window](docs/consumables.png)

### Outfits and collections

![Outfit window](docs/outfits.png)

Screenshots use a constructed test save. No game save or account identifier is distributed.

## Build from source

Windows 64-bit with the .NET Framework 4.x C# compiler:

```powershell
.\build.ps1
```

Output: `dist\StellarBlade-AllInOne.exe`. There are no NuGet, WebView, or network dependencies.

The source includes integration checks for saves, EVAS envelopes, opening failures, the compact window workflow, and a separate memory fixture. Save tests require a compatible local test save; this repository intentionally does not include one. See [test instructions](docs/TESTING.md).

## License and credits

Apache-2.0; see [LICENSE.txt](LICENSE.txt) and [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt).

The GVAS reader and wardrobe catalog are adapted from [perillaroc/sblade-explorer](https://github.com/perillaroc/sblade-explorer). EVAS format references are documented in the notices; their code is not incorporated. No source or binaries from Stellar Blade Mod Suite are included.

Unofficial project. Stellar Blade and related names belong to their respective owners. The app is offline and does not upload saves, change Steam IDs, or modify Steam Cloud.
