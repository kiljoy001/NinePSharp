# Stryker Anti-Slop Quick Start

> **Which config should I use?** See `STRYKER_WHICH_CONFIG.md` for a decision guide.

## TL;DR Commands

```bash
# Simplest: Full test, no complications (2-4 hours on RAM)
./stryker-ramdisk.sh stryker-config-simple.json

# Fastest: Test only changed files via git (5-15 minutes)
./stryker-diff.sh

# View results
xdg-open StrykerOutput/reports/mutation-report.html
```

---

## What Changed

### ✅ Configuration Optimized (`stryker-config.json`)

- **Mutation level**: `Standard` → `Complete` (catches boundary bugs)
- **Concurrency**: `4` → `12` cores (uses your hardware)
- **Thresholds**: `break: 0` → `break: 70` (enforces quality)
- **String filtering**: Ignores `StringLiteral`, `InterpolatedString` (40% faster)
- **Coverage**: Added `perTest` analysis (finds generic tests)
- **Incremental**: Enabled baseline comparison (only test changes)

### 🚀 New Scripts

1. **`stryker-ramdisk.sh`** - Runs Stryker in RAM (10-20x faster)
2. **`stryker-diff.sh`** - Tests only changed files (2-day → 2-hour)
3. **`STRYKER_SLOP_DETECTION.md`** - How to read results

---

## The Three-Step Workflow

### Step 1: Baseline (First Time Only)
```bash
./stryker-ramdisk.sh
```
- Creates mutation baseline
- Takes 2-4 hours on your 96GB system
- Run once, reuse forever

### Step 2: Test New Code (After AI Writes Code)
```bash
# Test everything changed since main branch
./stryker-diff.sh main

# Or test since a specific commit
./stryker-diff.sh feature/my-feature
```
- Only mutates files you changed
- Takes 5-15 minutes typically
- Run this daily/per-PR

### Step 3: Fix the Slop
```bash
# Open the HTML report
xdg-open StrykerOutput/reports/mutation-report.html

# Look for red "Survived" mutations
# Fix the tests that missed them
# Re-run stryker-diff.sh
```

---

## Quick Slop Reference

| Survived Mutation | AI Skipped | Fix It By |
|------------------|------------|-----------|
| `Equality` (>, <, ==) | Boundary testing | Add tests for edges (0, -1, max, min) |
| `Arithmetic` (+, -, *, /) | Actual verification | Assert expected values, not mirrored logic |
| `Block` (removed code) | Everything | Delete dead code or write real tests |
| `Unary/Logical` (!, flipped conditions) | Error paths | Test null cases, exceptions, failures |
| `Logical` (&&, \|\|) | Condition combos | Test all true/false combinations |

---

## Performance Expectations

**Your System76 (96GB RAM, 12 cores):**

| Test Type | Time | When to Use |
|-----------|------|-------------|
| Full (RAM disk) | 2-4 hours | Initial baseline, major refactors |
| Diff (RAM disk) | 5-15 min | Daily testing of new code |
| Diff (SSD) | 15-45 min | If RAM disk unavailable |

---

## Configuration Tuning

### If Stryker is Too Slow
Edit `stryker-config.json`:
```json
"concurrency": 8,  // Reduce from 12 if system thrashes
"timeout-ms": 10000  // Reduce from 15000 for faster tests
```

### If Too Many Mutations
Edit `stryker-config.json`:
```json
"ignore-mutations": [
  "StringLiteral",
  "InterpolatedString",
  "RegexChange",
  "ArrayDeclaration"  // Add this if needed
]
```

### To Test Specific Projects
Edit `stryker-config.json`:
```json
"test-projects": [
  "NinePSharp.Tests/NinePSharp.Tests.csproj",
  "NinePSharp.Backends.Cloud.Tests/NinePSharp.Backends.Cloud.Tests.csproj"
]
```

---

## What Good Results Look Like

### ✅ Good Mutation Score
```
Mutation score: 87.3%
   Killed: 1,234
   Survived: 179
   Timeout: 12
```

### ✅ Specific Test Coverage
```
Mutation killed by:
   - CalculateTests.AddBoundaryTest()
```

### ✅ No Boundary Survivors
```
All Equality mutations killed
```

---

## What AI Slop Looks Like

### 🔴 Bad Mutation Score
```
Mutation score: 43.2%
   Killed: 432
   Survived: 789
```

### 🔴 Generic Test Coverage
```
Mutation survived
   Covered by:
   - IntegrationTests.TestEverything()
```

### 🔴 Boundary Blindness
```
Survived: Changed >= to >
   No test failed
```

---

## Emergency: RAM Disk Failed

If `stryker-ramdisk.sh` fails:

```bash
# Run normally on SSD (slower but works)
dotnet stryker --config-file stryker-config.json

# Or disable RAM disk for diff testing
USE_RAMDISK=false ./stryker-diff.sh main
```

---

## Integration with CI/CD

Add to your pipeline:
```yaml
- name: Mutation Testing (Changed Files Only)
  run: |
    chmod +x stryker-diff.sh
    ./stryker-diff.sh ${{ github.event.pull_request.base.ref }}

- name: Check Mutation Score
  run: |
    SCORE=$(jq '.thresholds.break' StrykerOutput/reports/mutation-report.json)
    if [ $SCORE -lt 70 ]; then
      echo "Mutation score too low: $SCORE%"
      exit 1
    fi
```

---

## Common Questions

**Q: Why 12 cores if I have more?**
A: .NET's test runner has diminishing returns above 12 parallel tests. More can cause thrashing.

**Q: Why ignore string mutations?**
A: They rarely find bugs and waste 30-50% of testing time. Focus on logic, not text.

**Q: What's the difference between Stryker and code coverage?**
A: Coverage says "this line ran." Stryker says "this line was actually tested."

**Q: Can I run this on the Dell?**
A: Yes, but reduce concurrency to 6-8 cores and RAMDISK_SIZE to 16G in the scripts.

---

## Next Steps

1. **Run baseline**: `./stryker-ramdisk.sh` (let it run overnight)
2. **Test your current branch**: `./stryker-diff.sh main`
3. **Read the report**: Look for red "Survived" mutations
4. **Fix the tests**: See `STRYKER_SLOP_DETECTION.md` for patterns
5. **Re-run**: Should be green now

**Remember:** Mutation score above 85% on new code = AI slop eliminated.

---

## Full Documentation

- **Configuration details**: `stryker-config.json`
- **Slop patterns**: `STRYKER_SLOP_DETECTION.md`
- **Official Stryker docs**: https://stryker-mutator.io/docs/stryker-net/introduction/
