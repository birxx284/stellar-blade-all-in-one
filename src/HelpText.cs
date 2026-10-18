namespace StellarAssist
{
    static class HelpText
    {
        public const string Content = @"MANUAL IN-GAME CONTROLS

This assisted memory editor is limited to SB-Win64-Shipping. It does not include verified addresses or an automatic profile for the latest game version. Some values may be encoded, duplicated, or stored in unsupported types.

MONEY AND CONSUMABLES

Load a save in game and connect. Click Locate on the desired control. Search for the visible quantity as an integer. Spend or collect some, enter the new quantity, and filter with Equals the number above. Repeat until few candidates remain. Each consumable type needs separate calibration.

HEALTH AND ENERGY

If the exact number is unknown, choose Float and Unknown value. Take damage or spend the selected energy in game, then filter by Decreased. Leave it unchanged and filter by Unchanged. Recover it and filter by Increased. Repeat. Try Integer if Float gives no useful results.

Scans can take time. A snapshot uses up to 8 GB of temporary storage; while filtering, two snapshots may coexist and use up to 16 GB. They are removed during a normal shutdown.

VERIFY AND HOLD

Select a candidate and use it for the chosen control. Assignment alone does not confirm it is the player value. Apply a small change in Controls and verify its effect in game before enabling Hold. The app never writes to all scan results.

Hold repeats the value four times per second. F8 or Stop all stops repetition; previously applied values are not restored. Holding health does not guarantee survival against lethal hits or scripted events.

AFTER CHANGING AREA OR SAVE

Stop holding and recalibrate. An address may remain readable while belonging to another object. Addresses are cleared when disconnecting, closing, or reconnecting, and are not saved between sessions.

BACKUPS AND REPORTS

Back up your save with the game closed. Memory changes to money or inventory may persist when the game saves. No game-folder installation, account, or network connection is required.

Export report writes the game executable version and Steam Build ID when available, plus control and scan status. Include it in a support request when troubleshooting.";
    }
}
