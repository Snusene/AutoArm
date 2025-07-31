# AutoArm Weapon Scoring Performance Test

This HTML tool tests the performance impact of proposed optimizations to the AutoArm weapon scoring system.

## How to Use

1. Open `performance-test.html` in a web browser
2. Configure test parameters:
   - **Number of Weapons**: How many weapons to evaluate (default: 50)
   - **Number of Pawns**: How many pawns to test with (default: 10)  
   - **Iterations**: How many times to repeat the test (default: 100)

3. Run tests:
   - **Performance Test**: Measures calculation speed and efficiency
   - **Accuracy Test**: Compares scoring results between systems
   - **Both Tests**: Runs both and provides detailed analysis

## What the Optimizations Do

### Current System
- ~15 calculations per weapon evaluation
- Recalculates everything each time
- Separate calculations for range modifier/bonus
- Estimates AP for melee weapons without data

### Optimized System  
- ~5-7 calculations per weapon evaluation
- Caches static weapon properties
- Combined range calculations
- Simplified burst bonus formula
- No AP estimation for melee

## Expected Results

- **Performance**: 60-80% faster depending on scenario
- **Accuracy**: Most weapons within 5% of original scores
- **Memory**: Small increase due to caching (negligible)

## Key Insights

1. **Weapon properties are static** - no need to recalculate each time
2. **Pawn-specific modifiers** must still be calculated per evaluation
3. **Cache efficiency** improves with more evaluations
4. **Scoring differences** are minimal and acceptable

## Implementation Notes

If implementing these optimizations:

1. Add a static weapon property cache
2. Keep the cache size limited (LRU cache)
3. Invalidate cache when mods change
4. Consider making cache optional via settings
