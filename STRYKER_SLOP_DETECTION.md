# AI Slop Detection Guide for Stryker Mutation Testing

This guide helps you identify "syntactically correct but logically hollow" code that AI generates.

## The Problem: AI Theater Tests

AI often writes tests that:
- Execute code without verifying results
- Mirror implementation logic instead of testing it
- Skip edge cases and boundary conditions
- Create functions that do nothing useful

Stryker exposes this by mutating your code and seeing if tests catch the mutations.

---

## Critical Slop Patterns to Watch

### 1. **Logic Mirroring** (The "Copy-Paste Test")

**What it is:** AI writes tests that duplicate the implementation logic instead of verifying behavior.

**Mutation Signal:**
```
🔴 Arithmetic: Survived
   - Changed + to -
   - Changed * to /
```

**What it means:** Your tests are just running the code, not checking if it's correct.

**Example of Slop:**
```csharp
// Implementation
public int Calculate(int a, int b) => a + b;

// AI-generated "test" that mirrors logic
[Fact]
public void TestCalculate() {
    var result = Calculate(5, 3);
    Assert.Equal(5 + 3, result);  // ❌ This is just repeating the implementation!
}
```

**Proper test:**
```csharp
[Fact]
public void TestCalculate() {
    Assert.Equal(8, Calculate(5, 3));  // ✓ Tests actual behavior
}
```

---

### 2. **Boundary Laziness** (Off-by-One Blindness)

**What it is:** AI skips testing edge cases and boundary conditions.

**Mutation Signal:**
```
🔴 Equality: Survived
   - Changed > to >=
   - Changed < to <=
   - Changed == to !=
```

**What it means:** Your tests only check "happy path" scenarios, not boundaries.

**Example of Slop:**
```csharp
// Implementation
public bool IsAdult(int age) => age >= 18;

// AI test only checks middle values
[Fact]
public void TestIsAdult() {
    Assert.True(IsAdult(25));   // ❌ Doesn't test the boundary!
    Assert.False(IsAdult(10));
}
```

**If Stryker changes `>=` to `>`, the test still passes!**

**Proper test:**
```csharp
[Theory]
[InlineData(17, false)]  // Just below boundary
[InlineData(18, true)]   // Exactly at boundary
[InlineData(19, true)]   // Just above boundary
public void TestIsAdult(int age, bool expected) {
    Assert.Equal(expected, IsAdult(age));
}
```

---

### 3. **Dead Code Slop** (The Empty Function)

**What it is:** AI generates functions that do absolutely nothing useful.

**Mutation Signal:**
```
🔴 Block: Survived
   - Removed entire function body
   - Removed if/else block
   - Removed loop body
```

**What it means:** The function can be deleted and nothing breaks.

**Example of Slop:**
```csharp
// Implementation
public void LogUserAction(string action) {
    // AI generated this but it does nothing
    Console.WriteLine($"Action: {action}");
}

// AI test
[Fact]
public void TestLogUserAction() {
    LogUserAction("click");  // ❌ Doesn't verify anything happened!
    Assert.True(true);       // ❌ Meaningless assertion
}
```

**If Stryker empties the function, test still passes!**

---

### 4. **Exception Hallucination** (AI Assumes Safety)

**What it is:** AI assumes certain states are impossible or dependencies always succeed.

**Mutation Signal:**
```
🔴 Unary/Logical: Survived
   - Changed if (x != null) to if (x == null)
   - Changed if (success) to if (!success)
```

**What it means:** Your code has untested error paths.

**Example of Slop:**
```csharp
// Implementation
public User GetUser(int id) {
    var user = _database.FindUser(id);
    if (user != null) {
        return user;
    }
    return null;
}

// AI test only checks success case
[Fact]
public void TestGetUser() {
    var user = GetUser(1);
    Assert.NotNull(user);  // ❌ Never tests null case!
}
```

**If Stryker flips the null check, test never fails!**

---

### 5. **String Slop** (Wasted Mutation Time)

**What it is:** AI generates tons of UI strings, log messages, comments.

**Why we ignore it:** Mutating `"Error"` to `"Stryker was here"` wastes time.

**Config fix:**
```json
"ignore-mutations": [
  "String",
  "Regex"
]
```

**Speed improvement:** 30-50% faster test runs.

---

## How to Read Stryker's HTML Report

Open `StrykerOutput/reports/mutation-report.html` and look for:

### 🟢 Green (Killed) = Good
```
Mutation killed by test: CalculateTests.TestBoundary
```
Your tests caught the mutation. This is what you want.

### 🔴 Red (Survived) = AI Slop
```
Mutation survived
   - No test failed when we changed > to >=
```
AI didn't test this logic path. **THIS IS THE SLOP.**

### 🟡 Yellow (Timeout) = Probably OK
```
Mutation caused timeout
```
Usually means you created an infinite loop, which is good detection.

### ⚫ Gray (No Coverage) = Dead Code
```
No tests cover this code
```
Either delete it or write tests.

---

## The Slop Scoring System

**Mutation Score = (Killed / Total Mutations) × 100**

- **90-100%**: Excellent. AI wrote real tests.
- **75-89%**: Good. Some gaps but mostly solid.
- **60-74%**: Mediocre. AI wrote theater tests.
- **Below 60%**: Slop Alert. AI just made code run, not work correctly.

---

## Quick Reference: Mutation → Slop Translation

| Mutation Type | What It Tests | If It Survives, AI Skipped |
|--------------|---------------|---------------------------|
| `Arithmetic` | Math logic | Actual calculation verification |
| `Equality` | Comparisons | Edge cases and boundaries |
| `Logical` | Boolean logic | AND/OR condition combinations |
| `Unary` | Negation/increment | Error paths and sign checks |
| `Block` | Function bodies | Whether code does anything useful |
| `Assignment` | Variable assignments | State changes and side effects |
| `Linq` | LINQ queries | Query correctness and results |
| `Boolean` | True/false values | Configuration and flag logic |

---

## Workflow for Your 96GB System

### 1. Initial Baseline (One-time, slow)
```bash
./stryker-ramdisk.sh
```
This creates a baseline. Takes ~2 hours on RAM disk (vs 2 days on SSD).

### 2. Test New AI Code (Fast, 5-15 minutes)
```bash
./stryker-diff.sh main
```
Only tests code changed since `main` branch.

### 3. Analyze Results
```bash
xdg-open StrykerOutput/reports/mutation-report.html
```
Look for red "Survived" mutations in files you just added.

### 4. Fix the Slop
- Add boundary tests for `Equality` survivors
- Add error case tests for `Unary/Logical` survivors
- Delete dead code for `Block` survivors
- Add real assertions for `Arithmetic` survivors

### 5. Re-run Diff Test
```bash
./stryker-diff.sh main
```
Should be green now.

---

## Advanced: Coverage-Per-Test Analysis

The config uses `"coverage-analysis": "perTest"`, which maps each mutation to specific tests.

**What to look for:**
```
Mutation survived in Calculate()
   Covered by:
   - GeneralTest.TestEverything()
   - IntegrationTest.TestAll()
```

This is **AI slop**! Generic tests that run code but don't verify it.

**Good coverage:**
```
Mutation killed in Calculate()
   Killed by:
   - CalculateTests.AdditionTest()
```

Specific test for specific behavior.

---

## Key Performance Numbers

**Your System76 (96GB RAM, 12 cores):**

| Method | Time Estimate | Use Case |
|--------|--------------|----------|
| Full test (SSD) | ~48 hours | Never do this |
| Full test (RAM) | ~2-4 hours | Initial baseline |
| Diff test (RAM) | ~5-15 min | Daily AI code review |
| Diff test (SSD) | ~15-45 min | If RAM disk fails |

**Expected mutation count for NinePSharp:** ~15,000-25,000 mutations
**With string exclusion:** ~10,000-15,000 mutations (40% reduction)

---

## Troubleshooting

### RAM disk out of space?
```bash
# Increase size in stryker-ramdisk.sh
RAMDISK_SIZE="48G"  # You have 96GB, use it
```

### Stryker too slow?
```bash
# Reduce concurrency if system is thrashing
# Edit stryker-config.json
"concurrency": 8  # Instead of 12
```

### Too many mutations?
```bash
# Add more exclusions
"ignore-mutations": [
  "StringLiteral",
  "InterpolatedString",
  "RegexChange",
  "ArrayDeclaration"  # If you have lots of arrays
]
```

---

## Success Criteria

You've eliminated AI slop when:

1. **Mutation score > 85%** on new code
2. **No survived `Equality` mutations** (all boundaries tested)
3. **No survived `Block` mutations** (no dead code)
4. **Specific test kills each mutation** (no generic "TestEverything" tests)

---

## Remember

**Stryker doesn't find bugs. It finds lies.**

AI writes tests that lie about correctness. Stryker proves they're lying by breaking the code and watching the tests not notice.

Your job: Make the tests stop lying.
