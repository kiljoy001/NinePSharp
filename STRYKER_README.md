# Stryker Mutation Testing - Anti-Slop Setup

This directory contains a complete Stryker mutation testing setup optimized to detect AI-generated "slop" code - tests that run but don't actually verify anything.

## 📚 Documentation Files

| File | Purpose |
|------|---------|
| **STRYKER_README.md** | This file - overview and getting started |
| **STRYKER_WHICH_CONFIG.md** | 👈 **START HERE** - Which config file to use |
| **STRYKER_QUICKSTART.md** | Quick reference commands |
| **STRYKER_SLOP_DETECTION.md** | How to read results and identify AI slop |
| **STRYKER_WORKFLOW.md** | Complete workflow guide with examples |
| **STRYKER_CONFIG_REFERENCE.md** | Configuration key reference |

## 🚀 Quick Start (30 seconds)

```bash
# 1. Install Stryker.NET (if not already installed)
dotnet tool install -g dotnet-stryker

# 2. Run mutation testing (will take 2-4 hours)
./stryker-ramdisk.sh stryker-config-simple.json

# 3. View results
xdg-open StrykerOutput/reports/mutation-report.html
```

> **Not sure which config to use?** See `STRYKER_WHICH_CONFIG.md` for a decision guide.

## 🎯 What This Setup Does

### Catches 5 Types of AI Slop

1. **Logic Mirroring** - Tests that repeat implementation logic
2. **Boundary Laziness** - Missing edge case tests (off-by-one, null, etc.)
3. **Dead Code** - Functions that do nothing useful
4. **Exception Hallucination** - Untested error paths
5. **Generic Tests** - `TestEverything()` that verifies nothing

### Performance Optimizations

- **40% faster**: Ignores string literal mutations
- **12 cores**: Full parallelization for your hardware
- **RAM disk**: 10-20x faster I/O (uses `/dev/shm`)
- **Git-based incremental**: Only tests changed files

### Quality Enforcement

- **70% threshold**: Builds fail below this mutation score
- **Complete mutation level**: Tests all logic paths including boundaries
- **Per-test coverage**: Maps each mutation to specific tests

## 📁 Configuration Files

### `stryker-config.json` (Git-Based Incremental)
- **Use for**: Daily development, PR checks
- **Features**: Git comparison, only mutates changed files
- **Speed**: 5-15 minutes for typical changes
- **Command**: `./stryker-diff.sh`

### `stryker-config-full.json` (Full Baseline)
- **Use for**: Initial setup, monthly quality checks
- **Features**: Full mutation, baseline comparison
- **Speed**: 2-4 hours on RAM disk
- **Command**: `./stryker-ramdisk.sh stryker-config-full.json`

**Note**: These features are mutually exclusive - you can't use `since` and `baseline` together.

## 🔧 Scripts

### `stryker-ramdisk.sh`
Runs mutation testing in RAM for maximum speed.

```bash
# Full baseline run
./stryker-ramdisk.sh stryker-config-full.json

# Incremental run
./stryker-ramdisk.sh stryker-config.json
```

### `stryker-diff.sh`
Quick incremental testing of only changed files.

```bash
# Test changes since last run
./stryker-diff.sh
```

## 📊 Expected Results

### System Performance (96GB RAM, 12 cores)

| Run Type | Time | Mutations | When |
|----------|------|-----------|------|
| Full baseline (RAM) | 2-4 hours | ~15,000 | Initial setup, monthly |
| Full baseline (SSD) | 6-8 hours | ~15,000 | If RAM disk unavailable |
| Incremental (RAM) | 5-15 min | ~500-2000 | Daily, per PR |
| Incremental (SSD) | 15-45 min | ~500-2000 | If RAM disk unavailable |

### Mutation Score Interpretation

| Score | Meaning |
|-------|---------|
| 90-100% | Excellent - AI wrote real tests |
| 75-89% | Good - Minor gaps |
| 60-74% | Mediocre - AI wrote theater tests |
| <60% | **Slop Alert** - Tests just execute, don't verify |

## 🔍 Reading Results

After running Stryker, open the HTML report:

```bash
xdg-open StrykerOutput/reports/mutation-report.html
```

### What to Look For

- 🟢 **Green (Killed)** - Good! Tests caught the mutation
- 🔴 **Red (Survived)** - **AI slop!** Test didn't catch the mutation
- 🟡 **Yellow (Timeout)** - Usually OK, created infinite loop
- ⚫ **Gray (No Coverage)** - Dead code or missing tests

### Common Slop Patterns

| Mutation Survived | What AI Skipped |
|------------------|-----------------|
| `Equality` (>=, <=, ==) | Boundary testing |
| `Arithmetic` (+, -, *, /) | Actual result verification |
| `Block` (removed code) | Everything |
| `Unary/Logical` (!, flipped conditions) | Error paths and null checks |

See `STRYKER_SLOP_DETECTION.md` for detailed patterns and fixes.

## 🎓 Recommended Workflow

### First Time Setup

1. **Install Stryker**: `dotnet tool install -g dotnet-stryker`
2. **Run baseline**: `./stryker-ramdisk.sh stryker-config-full.json` (2-4 hours)
3. **Review results**: Look for red "survived" mutations
4. **Fix tests**: Add proper assertions for survived mutations
5. **Verify**: Re-run to confirm fixes

### Daily Development

1. **Make changes**: Write code and tests
2. **Quick test**: `./stryker-diff.sh` (5-15 minutes)
3. **Check results**: Look for new survived mutations
4. **Fix immediately**: Don't let slop accumulate
5. **Commit**: Only commit when mutation score is green

### Monthly Quality Check

1. **Full rebaseline**: `./stryker-ramdisk.sh stryker-config-full.json`
2. **Track trends**: Compare to previous baseline
3. **Address decay**: Fix any quality degradation

## 🚨 Troubleshooting

### Config Error: "since and baseline are mutually exclusive"
**Solution**: Use `stryker-config.json` for incremental OR `stryker-config-full.json` for baseline. Not both.

### No Mutations Found
**Solution**: Clear cache and run full test:
```bash
rm -rf StrykerOutput/
./stryker-ramdisk.sh stryker-config-full.json
```

### Too Slow
**Solution**:
1. Use RAM disk: `./stryker-ramdisk.sh`
2. Use incremental: `./stryker-diff.sh`
3. Reduce concurrency in config if system is thrashing

### Tests Timing Out
**Solution**: Increase timeout in config:
```json
"additional-timeout": 20000
```

## 🎯 Success Criteria

You've eliminated AI slop when:

1. ✅ Mutation score >85% on new code
2. ✅ No survived `Equality` mutations
3. ✅ No survived `Block` mutations
4. ✅ Specific tests kill each mutation (not generic "TestAll")

## 📖 Further Reading

- [Stryker.NET Official Docs](https://stryker-mutator.io/docs/stryker-net/introduction/)
- [Mutation Testing Overview](https://en.wikipedia.org/wiki/Mutation_testing)
- `STRYKER_WORKFLOW.md` - Detailed workflow examples
- `STRYKER_SLOP_DETECTION.md` - Pattern recognition guide

## 💡 Key Insight

> "Stryker doesn't find bugs. It finds lies. AI writes tests that lie about correctness. Stryker proves they're lying by breaking the code and watching the tests not notice."

Your job: Make the tests stop lying.

---

**Ready to start?**

```bash
./stryker-ramdisk.sh stryker-config-full.json
```

Let it run for a few hours (overnight is fine), then check the results. You'll immediately see where AI cut corners on testing.
