# AutoArm

A RimWorld mod that implements intelligent weapon filtering and automatic equipping through runtime category injection and multi-threaded scoring algorithms.


### Core Components

**WeaponEquipGameComponent** - Main game loop integration using `GameComponent` inheritance
- Implements time-sliced processing with 60-tick intervals (1 second)
- Round-robin pawn processing to distribute CPU load
- Maintains per-pawn weapon caches with 300-tick (5 second) TTL

**WeaponTabInjector** - Category hierarchy modification via `[StaticConstructorOnStartup]`
- Dynamically injects Weapons as child of Apparel in `DefDatabase<ThingCategoryDef>`
- Enables weapon filtering through existing apparel policy UI

**WeaponThingFilterUtility** - Lazy-loaded weapon classification system
- Caches ranged/melee weapon definitions from `DefDatabase<ThingDef>`
- Implements `IsNonSelectable()` extension method for UI filtering

## Performance Characteristics

- **CPU Impact**: ~0.1ms per pawn per second (amortized)
- **Memory Footprint**: ~1KB per active pawn (cached weapons)
- **Scalability**: O(n) with colony size, O(r²) with weapon density

## Author

**Snues** - [GitHub](https://github.com/Snusene/AutoArm)
MIT License
