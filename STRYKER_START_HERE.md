# 🎯 START HERE - Stryker Anti-Slop Setup Complete

## What Just Happened?

You now have a complete mutation testing system that will catch AI-generated "slop" code - tests that execute but don't actually verify anything.

## 30-Second Start

```bash
# Install Stryker (if needed)
dotnet tool install -g dotnet-stryker

# Run the test (takes 2-4 hours)
./stryker-ramdisk.sh stryker-config-simple.json

# View results
xdg-open StrykerOutput/reports/mutation-report.html
```

That's it. Really.

## What Will You See?

After the test completes, the HTML report will show:

- 🟢 **Green (Killed)** - Your tests caught the mutation ✓
- 🔴 **Red (Survived)** - **AI slop detected!** Test missed it
- 🟡 **Yellow (Timeout)** - Usually OK (infinite loop created)
- ⚫ **Gray (No Coverage)** - Dead code

### Your Goal: 85%+ Mutation Score

| Score | Meaning |
|-------|---------|
| 90-100% | Excellent - Real tests |
| 75-89% | Good - Minor gaps |
| 60-74% | Mediocre - Theater tests |
| **<60%** | **AI Slop Alert** |

## The 5 Types of AI Slop This Catches

### 1. Logic Mirroring
```csharp
// Bad test: mirrors implementation
Assert.Equal(5 + 3, Calculate(5, 3));  // ❌

// Good test: verifies behavior
Assert.Equal(8, Calculate(5, 3));      // ✓
```

### 2. Boundary Laziness
```csharp
// Bad: Only tests middle values
Assert.True(IsAdult(25));   // ❌

// Good: Tests the boundary
Assert.False(IsAdult(17));  // ✓
Assert.True(IsAdult(18));   // ✓
Assert.True(IsAdult(19));   // ✓
```

### 3. Dead Code
```csharp
// If Stryker can delete this and tests still pass, it's useless:
public void LogUserAction(string action) {
    Console.WriteLine(action);  // Nobody verifies this
}
```

### 4. Exception Hallucination
```csharp
// Bad: Only tests success case
[Fact]
public void TestGetUser() {
    var user = GetUser(1);
    Assert.NotNull(user);  // ❌ Never tests null case
}
```

### 5. Generic Tests
```csharp
// Bad: Tests everything, verifies nothing
[Fact]
public void TestEverything() {
    var result = ComplexOperation();
    Assert.True(true);  // ❌ Always passes
}
```

## Which Config File Do I Use?

**New to Stryker?** → Use `stryker-config-simple.json`

**Want fast iterations?** → Use `stryker-config.json` (incremental)

**Need trend tracking?** → Use `stryker-config-full.json` (baseline)

👉 **Read `STRYKER_WHICH_CONFIG.md` for detailed decision tree.**

## Three Config Files Explained

### stryker-config-simple.json ⭐ RECOMMENDED
- Tests everything, every time
- No git tricks, no baseline
- Simple and reliable
- **Use this first!**

### stryker-config.json 🚀 FASTEST
- Git-based incremental testing
- Only tests changed files
- 5-15 minutes instead of 2-4 hours
- Use after you've run simple once

### stryker-config-full.json 📊 TRACKING
- Stores baseline for comparison
- Tracks mutation score over time
- Good for monthly reports
- Requires project version

## Commands You'll Use

```bash
# Simple full test (recommended first time)
./stryker-ramdisk.sh stryker-config-simple.json

# Fast incremental test (daily work)
./stryker-diff.sh

# View results
xdg-open StrykerOutput/reports/mutation-report.html

# Without RAM disk (slower)
dotnet stryker --config-file stryker-config-simple.json
```

## Expected Timeline (Your 96GB System)

- **Simple full test (RAM)**: 2-4 hours
- **Simple full test (SSD)**: 6-8 hours
- **Incremental test (RAM)**: 5-15 minutes
- **Incremental test (SSD)**: 15-45 minutes

## What's Optimized?

- ✅ **String mutations ignored** - 40% time saved
- ✅ **12 cores** - Full parallelization
- ✅ **RAM disk** - 10-20x faster I/O
- ✅ **Complete mutation level** - Catches boundary bugs
- ✅ **perTest coverage** - Finds generic tests
- ✅ **70% threshold** - Enforces quality

## Common Issues

### "since and baseline are mutually exclusive"
**Fix**: Use one config file at a time. Don't mix features.

### "Project version cannot be empty"
**Fix**: Use `stryker-config-simple.json` instead of full.

### "No mutations found"
**Fix**:
```bash
rm -rf StrykerOutput/
./stryker-ramdisk.sh stryker-config-simple.json
```

### Tests timing out
**Fix**: Edit your config file, increase timeout:
```json
"additional-timeout": 20000
```

## Reading the Results

When you open the HTML report, look for:

### 🔴 Red "Survived" Mutations = AI Slop

| What Survived | What to Fix |
|--------------|-------------|
| `Equality` (>=, <=) | Add boundary tests |
| `Arithmetic` (+, -, *) | Verify actual results, not logic |
| `Block` (removed code) | Delete dead code or add real tests |
| `Unary/Logical` (!, flipped conditions) | Test error paths and null cases |

**See `STRYKER_SLOP_DETECTION.md` for detailed patterns.**

## Your Next Steps

### Right Now
1. **Install Stryker**: `dotnet tool install -g dotnet-stryker`
2. **Start the test**: `./stryker-ramdisk.sh stryker-config-simple.json`
3. **Go do something else** for 2-4 hours (or run overnight)

### After It Finishes
1. **Open the report**: `xdg-open StrykerOutput/reports/mutation-report.html`
2. **Look for red survivors**: These are your AI slop areas
3. **Read the slop guide**: `STRYKER_SLOP_DETECTION.md`
4. **Fix the tests**: Add proper assertions

### Daily Workflow
1. **Make code changes**
2. **Quick test**: `./stryker-diff.sh` (5-15 min)
3. **Fix survivors immediately**
4. **Commit only when green**

## Documentation Roadmap

Read these in order:

1. **STRYKER_START_HERE.md** ← You are here
2. **STRYKER_WHICH_CONFIG.md** - Which config to use
3. **STRYKER_SLOP_DETECTION.md** - How to read results
4. **STRYKER_QUICKSTART.md** - Command reference
5. **STRYKER_WORKFLOW.md** - Detailed workflows
6. **STRYKER_CONFIG_REFERENCE.md** - Config keys

## Remember

> "Stryker doesn't find bugs. It finds lies. AI writes tests that lie about correctness."

Your mutation score tells you: **Are my tests actually verifying logic, or just executing code?**

**Goal: 85%+ mutation score = No AI slop.**

## Ready?

```bash
./stryker-ramdisk.sh stryker-config-simple.json
```

Go grab coffee. This will take a while. But when it's done, you'll know exactly where AI cut corners on your tests.

---

**Questions?** Check the other docs:
- Confused about configs? → `STRYKER_WHICH_CONFIG.md`
- Want to understand results? → `STRYKER_SLOP_DETECTION.md`
- Need quick commands? → `STRYKER_QUICKSTART.md`
