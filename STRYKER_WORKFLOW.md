# Stryker Mutation Testing Workflow

## The Two Config Files

Stryker.NET has a limitation: **`since` and `baseline` features are mutually exclusive**. So we have two configs:

| Config File | Use Case | Features | When to Use |
|------------|----------|----------|-------------|
| `stryker-config-full.json` | Full baseline runs | Baseline comparison, full mutation | Initial run, monthly quality checks |
| `stryker-config.json` | Incremental testing | Git-based `since`, only changed files | Daily development, PR checks |

## The Recommended Workflow

### 1️⃣ Initial Baseline (Do This Once)

Create your baseline by running a full mutation test:

```bash
# Full run with baseline storage (2-4 hours on RAM disk)
./stryker-ramdisk.sh stryker-config-full.json

# Or without RAM disk (slower, 6-8 hours)
dotnet stryker --config-file stryker-config-full.json
```

**What this does:**
- Mutates all code in `NinePSharp.Server/`
- Runs all tests to find which mutations survive
- Stores results in `StrykerOutput/` as baseline
- Creates HTML report showing mutation score

### 2️⃣ Daily Development (Git-Based Incremental)

After making changes, test only what you modified:

```bash
# Fast incremental test (5-15 minutes on RAM disk)
./stryker-diff.sh

# Or without RAM disk
dotnet stryker --config-file stryker-config.json
```

**What this does:**
- Uses git to detect which files changed since last test
- Only mutates those changed files
- Much faster than full run
- Updates results incrementally

### 3️⃣ Monthly Full Rebaseline (Optional)

Every month or after major refactors, recreate the baseline:

```bash
# Clear old baseline and run fresh
rm -rf StrykerOutput/
./stryker-ramdisk.sh stryker-config-full.json
```

## How Stryker's `since` Feature Works

The `since` feature in `stryker-config.json`:

```json
"since": {
  "enabled": true,
  "ignore-changes-in": [
    "**/bin/**",
    "**/obj/**",
    "**/*.md"
  ]
}
```

**Automatic Git Integration:**
1. Stryker looks at your git history
2. Compares current code to previous Stryker run (stored in `StrykerOutput/`)
3. Only mutates files that changed between those two points
4. Ignores build artifacts and documentation

**Example:**
```bash
# You modify NinePSharp.Server/utils/LuxVault.cs
# Stryker automatically detects this via git
# Only LuxVault.cs gets mutated (not the other 100+ files)
# Result: 5 minutes instead of 2 hours
```

## Common Scenarios

### Scenario: New Feature Branch

```bash
# 1. Create branch from main
git checkout -b feature/my-feature

# 2. Write code and tests
# ... code code code ...

# 3. Run incremental mutation test
./stryker-diff.sh

# 4. Check results
xdg-open StrykerOutput/reports/mutation-report.html

# 5. Fix any survived mutations
# ... fix tests ...

# 6. Re-run to verify
./stryker-diff.sh
```

### Scenario: Large Refactor

```bash
# 1. After major changes, do a full run
./stryker-ramdisk.sh stryker-config-full.json

# 2. This becomes the new baseline
# Future incremental runs compare against this
```

### Scenario: CI/CD Pipeline

```yaml
# .github/workflows/mutation-test.yml
name: Mutation Testing

on:
  pull_request:
    branches: [main]

jobs:
  mutate:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
        with:
          fetch-depth: 0  # Need full history for 'since'

      - name: Setup .NET
        uses: actions/setup-dotnet@v3

      - name: Install Stryker
        run: dotnet tool install -g dotnet-stryker

      - name: Incremental Mutation Test
        run: dotnet stryker --config-file stryker-config.json

      - name: Check Threshold
        run: |
          SCORE=$(jq '.thresholds.break' StrykerOutput/reports/mutation-report.json)
          if [ $SCORE -lt 70 ]; then
            echo "Mutation score too low: $SCORE%"
            exit 1
          fi
```

## Troubleshooting

### "No mutations found"

Stryker's `since` feature found no changes:

```bash
# Force a full run to reset
rm -rf StrykerOutput/
./stryker-ramdisk.sh stryker-config-full.json
```

### "since and baseline are mutually exclusive"

You're trying to use both features. Pick one:
- Use `stryker-config-full.json` for baseline comparison
- Use `stryker-config.json` for git-based incremental

### Results not updating

Clear the cache:

```bash
rm -rf StrykerOutput/
# Then re-run your test
```

### Too slow even with `since`

Check what Stryker is testing:

```bash
# Run with verbose output
dotnet stryker --config-file stryker-config.json --verbosity debug | grep "Mutating"
```

If it's mutating too many files, check:
1. Is your git repo clean? (`git status`)
2. Did you commit your changes? (Stryker compares to last commit)
3. Are you on the right branch?

## Quick Commands Reference

```bash
# Full baseline run (RAM disk)
./stryker-ramdisk.sh stryker-config-full.json

# Full baseline run (no RAM disk)
dotnet stryker --config-file stryker-config-full.json

# Incremental git-based (RAM disk)
./stryker-diff.sh

# Incremental git-based (no RAM disk)
dotnet stryker --config-file stryker-config.json

# View results
xdg-open StrykerOutput/reports/mutation-report.html

# Clear cache
rm -rf StrykerOutput/

# Check Stryker version
dotnet stryker --version

# Dry run to test config
dotnet stryker --config-file stryker-config.json --dry-run
```

## Performance Expectations

**Your System76 (96GB RAM, 12 cores):**

| Test Type | Config | Time | Mutations |
|-----------|--------|------|-----------|
| Full (RAM) | stryker-config-full.json | 2-4 hours | ~15,000 |
| Full (SSD) | stryker-config-full.json | 6-8 hours | ~15,000 |
| Incremental (RAM) | stryker-config.json | 5-15 min | ~500-2000 |
| Incremental (SSD) | stryker-config.json | 15-45 min | ~500-2000 |

*Incremental times assume you changed 5-10 files*

## Understanding the Results

After running Stryker, check the HTML report:

```bash
xdg-open StrykerOutput/reports/mutation-report.html
```

Look for:
- 🟢 **Green (Killed)**: Tests caught the mutation ✓
- 🔴 **Red (Survived)**: Tests missed it - **AI slop detected!**
- 🟡 **Yellow (Timeout)**: Created infinite loop (usually good)
- ⚫ **Gray (No Coverage)**: Dead code

See `STRYKER_SLOP_DETECTION.md` for detailed analysis patterns.

## Next Steps

1. **Run initial baseline**: `./stryker-ramdisk.sh stryker-config-full.json`
2. **Let it run for 2-4 hours** (overnight is fine)
3. **Review results**: Check for red "survived" mutations
4. **Fix the tests**: Add proper assertions for boundaries, errors, etc.
5. **Use incremental testing daily**: `./stryker-diff.sh` after code changes

The goal: **85%+ mutation score** on new code = AI slop eliminated.
