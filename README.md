# AutoArm

A RimWorld mod that implements intelligent weapon filtering and automatic equipping through runtime category injection and multi-threaded scoring algorithms.

- **CPU Impact**: ~0.1ms per pawn per second (amortized)
- **Memory Footprint**: ~1KB per active pawn (cached weapons)
- **Scalability**: O(n) with colony size, O(r²) with weapon density

## Author

**Snues** - [GitHub](https://github.com/Snusene/AutoArm)
MIT License
