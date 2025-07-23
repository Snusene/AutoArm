# AutoArm Debug Logging System

## Overview
When debug logging is enabled in the AutoArm settings, all debug messages will be written to a file called `AutoArm_Debug.txt` in your RimWorld data directory (typically `%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\`).

## How to Use

### Simple Logging
```csharp
// Basic log message
AutoArmDebug.Log("[AutoArm] Something happened");

// Log with pawn context
AutoArmDebug.LogPawn(pawn, "Checking for weapons");

// Log with weapon context
AutoArmDebug.LogWeapon(pawn, weapon, "Found better weapon");

// Log errors (automatically flushes to disk)
AutoArmDebug.LogError("Something went wrong", exception);
```

### Replacing Existing Debug Logs
Replace this pattern:
```csharp
if (AutoArmMod.settings?.debugLogging == true)
{
    Log.Message($"[AutoArm] {pawn.Name}: Some message");
}
```

With:
```csharp
AutoArmDebug.LogPawn(pawn, "Some message");
```

The new system automatically checks the debug setting, so you don't need the if statement.

### Direct Logger Access
```csharp
// For more control
AutoArmDebugLogger.DebugLog("Message", forceFlush: true);

// The system automatically adds timestamps and [AutoArm] prefix
```

## Features

1. **Automatic Buffering**: Logs are buffered and written in batches for performance
2. **Automatic Flushing**: Logs are flushed:
   - Every 5 seconds of game time
   - When saving the game
   - When closing the game
   - When the buffer reaches 1000 lines
   - When logging errors

3. **Timestamps**: Each log entry includes a precise timestamp (HH:mm:ss.fff)

4. **Thread-Safe**: Multiple threads can write logs safely

5. **Console Mirror**: Messages are also sent to the RimWorld console (Log.Message)

## Example Output
```
=== AutoArm Debug Log Started at 2024-01-15 14:32:15 ===
[14:32:16.123] AutoArm mod initialized with debug logging enabled
[14:32:18.456] Victoria 'Vicky' Louene: Weapons allowed = True, isUnarmed = true
[14:32:18.457] Victoria 'Vicky' Louene: JobGiver_PickUpBetterWeapon_Emergency.TryGiveJob called
[14:32:18.458] Victoria 'Vicky' Louene: Found better weapon - Autopistol
[14:32:20.789] Barry 'McCormick' McCormick: Weapons allowed = True, isUnarmed = false
```

## Migration Guide

To migrate existing debug logs:
1. Remove the `if (AutoArmMod.settings?.debugLogging == true)` check
2. Replace `Log.Message($"[AutoArm] ...")` with appropriate AutoArmDebug method
3. The debug setting check is handled automatically by the new system
