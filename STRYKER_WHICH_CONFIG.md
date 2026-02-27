# Which Stryker Config Should I Use?

## TL;DR - Quick Decision Tree

```
Are you running Stryker for the first time?
├─ YES → Use stryker-config-simple.json
└─ NO → Do you want incremental testing?
    ├─ YES → Use stryker-config.json (git-based)
    └─ NO → Do you need trend tracking?
        ├─ YES → Use stryker-config-full.json (baseline)
        └─ NO → Use stryker-config-simple.json
```

## The Three Config Files

### 1. `stryker-config-simple.json` ⭐ RECOMMENDED FOR MOST USERS

**What it does:**
- Tests all code every time
- No git comparison
- No baseline tracking
- Simple and reliable

**When to use:**
- ✅ First time running Stryker
- ✅ After major refactors
- ✅ When you want a "clean slate" test
- ✅ When incremental testing isn't working

**How to run:**
```bash
./stryker-ramdisk.sh stryker-config-simple.json
```

**Pros:**
- ✅ Simple, no surprises
- ✅ Tests everything
- ✅ No git or version dependencies

**Cons:**
- ❌ Always slow (2-4 hours)
- ❌ No incremental benefits

---

### 2. `stryker-config.json` 🚀 FASTEST FOR DAILY USE

**What it does:**
- Uses git to detect changed files
- Only mutates files that changed
- Fast incremental testing

**When to use:**
- ✅ Daily development workflow
- ✅ PR checks
- ✅ After you've run a full test once

**How to run:**
```bash
./stryker-diff.sh
```

**Pros:**
- ✅ Very fast (5-15 minutes)
- ✅ Only tests what changed
- ✅ Perfect for TDD workflow

**Cons:**
- ❌ Requires clean git state
- ❌ First run needs full test
- ❌ Can't compare trends over time

---

### 3. `stryker-config-full.json` 📊 FOR QUALITY TRACKING

**What it does:**
- Tests all code
- Stores baseline results
- Compares to previous runs
- Tracks quality trends

**When to use:**
- ✅ Monthly quality reports
- ✅ Tracking mutation score over time
- ✅ Before/after refactor comparisons

**How to run:**
```bash
./stryker-ramdisk.sh stryker-config-full.json
```

**Pros:**
- ✅ Trend tracking
- ✅ Quality metrics
- ✅ Baseline comparison

**Cons:**
- ❌ Requires project version
- ❌ Always slow (2-4 hours)
- ❌ Can't use with `since`

---

## Recommended Workflow

### Week 1: Initial Setup

**Day 1: Full baseline**
```bash
# Run full test with simple config (2-4 hours)
./stryker-ramdisk.sh stryker-config-simple.json
```

**Result:** You now have a complete mutation report showing all your code quality.

---

### Week 2+: Daily Development

**Option A: Fast incremental (recommended)**
```bash
# Quick test of only changed files (5-15 minutes)
./stryker-diff.sh
```

**Option B: Full retest**
```bash
# If incremental isn't working or you want to be thorough
./stryker-ramdisk.sh stryker-config-simple.json
```

---

### Monthly: Quality Check

```bash
# Run with baseline to track trends
./stryker-ramdisk.sh stryker-config-full.json
```

Compare this month's mutation score to last month's.

---

## Common Scenarios

### Scenario: "I just want to see if my tests are good"

**Use:** `stryker-config-simple.json`

```bash
./stryker-ramdisk.sh stryker-config-simple.json
xdg-open StrykerOutput/reports/mutation-report.html
```

Simple, straightforward, no complications.

---

### Scenario: "I'm doing TDD and want fast feedback"

**Use:** `stryker-config.json` (incremental)

```bash
# 1. Run full test once
./stryker-ramdisk.sh stryker-config-simple.json

# 2. Then use incremental for fast iterations
./stryker-diff.sh  # 5-15 minutes per change
```

---

### Scenario: "I need to report quality metrics to management"

**Use:** `stryker-config-full.json` (baseline tracking)

```bash
# Run monthly
./stryker-ramdisk.sh stryker-config-full.json

# Compare mutation scores month-over-month
# Show improvement trends
```

---

### Scenario: "Incremental testing isn't working"

**Solution:** Fall back to simple config

```bash
# Clear any cached state
rm -rf StrykerOutput/

# Run full test
./stryker-ramdisk.sh stryker-config-simple.json
```

---

## Config Comparison Table

| Feature | simple | incremental (since) | full (baseline) |
|---------|--------|---------------------|-----------------|
| Speed | ❌ Slow | ✅ Fast | ❌ Slow |
| Setup complexity | ✅ None | ⚠️ Needs git | ⚠️ Needs version |
| Trend tracking | ❌ No | ❌ No | ✅ Yes |
| Daily workflow | ⚠️ Too slow | ✅ Perfect | ❌ Too slow |
| First time use | ✅ Perfect | ❌ Need baseline first | ⚠️ Complex |
| Reliability | ✅ Always works | ⚠️ Git dependent | ⚠️ Version dependent |

---

## My Recommendation

### For 90% of users:

1. **First run**: Use `stryker-config-simple.json`
2. **Daily work**: Use `stryker-config.json` (incremental)
3. **Fall back**: If incremental breaks, use `simple` again

### Skip `stryker-config-full.json` unless:
- You need monthly reports for management
- You're tracking quality metrics over time
- You're comparing before/after major refactors

---

## Quick Commands

```bash
# Simple (full test, no tricks)
./stryker-ramdisk.sh stryker-config-simple.json

# Incremental (fast, git-based)
./stryker-diff.sh

# Baseline (trend tracking)
./stryker-ramdisk.sh stryker-config-full.json

# Without RAM disk (slower but works everywhere)
dotnet stryker --config-file stryker-config-simple.json
```

---

## Troubleshooting

### Error: "since and baseline are mutually exclusive"
**Cause:** Trying to use both features together
**Fix:** Pick ONE config file and stick with it

### Error: "Project version cannot be empty when baseline is enabled"
**Cause:** Using `stryker-config-full.json` without project version
**Fix:** Use `stryker-config-simple.json` instead, or add version to config

### Error: "No mutations found"
**Cause:** Incremental testing found no changes
**Fix:** Run full test with `stryker-config-simple.json`

### Tests timing out
**Cause:** Additional timeout too low
**Fix:** Increase timeout in whichever config you're using

---

## Summary

**Start simple. Stay simple unless you have a specific need.**

```bash
# This is all most people need:
./stryker-ramdisk.sh stryker-config-simple.json
```

The "fancy" incremental and baseline features are nice-to-haves, not must-haves.
