# AutoArm

A RimWorld mod that implements intelligent weapon filtering and automatic equipping through runtime category injection and multi-threaded scoring algorithms.

### Core Components

**WeaponTabInjector** - Runtime category hierarchy modification via `[StaticConstructorOnStartup]`
- Dynamically moves Weapons category under Apparel in `DefDatabase<ThingCategoryDef>`
- Enables weapon filtering through existing outfit policy UI

**ThinkNode Integration** - Native AI behavior injection
- Custom think nodes for emergency weapon pickup (unarmed pawns)
- Weapon upgrade evaluation with job interruption logic
- SimpleSidearms-aware sidearm management

**WeaponScoreCache** - Performance-optimized scoring system
- Two-tier caching: base weapon properties + pawn-specific modifiers
- Considers quality, damage, range, skills, traits, and mod bonuses
- Automatic cache invalidation on skill changes or weapon modifications

## Performance Characteristics

- **CPU Impact**: ~0.1ms per pawn per second (amortized)
- **Memory Footprint**: ~3-5KB per active pawn (cached weapons + scores)
- **Scalability**: O(n) with colony size, adaptive check intervals

## Author

**Snues** - [GitHub](https://github.com/Snusene/AutoArm)
MIT License