# Stryker.NET Configuration Reference

> **CRITICAL**: The `since` and `baseline` features are **mutually exclusive**. You cannot enable both in the same config file.

## Valid Configuration Keys

These are the **only** valid keys for Stryker.NET (case-sensitive):

```json
{
  "stryker-config": {
    "additional-timeout": 10000,           // Extra time (ms) on top of baseline
    "baseline": { },                       // Baseline comparison settings
    "break-on-initial-test-failure": false, // Stop if initial tests fail
    "concurrency": 12,                     // Parallel test runs
    "configuration": "Release",            // Build configuration
    "coverage-analysis": "perTest",        // "perTest", "all", or "off"
    "dashboard-url": "",                   // Stryker Dashboard URL
    "disable-bail": false,                 // Don't stop on first kill
    "disable-mix-mutants": false,          // Don't group mutations
    "ignore-methods": [],                  // Methods to skip
    "ignore-mutations": [],                // Mutation types to skip
    "language-version": "latest",          // C# language version
    "mutate": [],                          // Files to mutate
    "mutation-level": "Complete",          // "Basic", "Standard", "Complete"
    "project": "",                         // Target project path
    "project-info": { },                   // Project metadata
    "report-file-name": "mutation-report", // Output filename
    "reporters": [],                       // Report formats
    "since": { },                          // Incremental settings
    "solution": "",                        // Solution file path
    "target-framework": "",                // .NET target framework
    "test-case-filter": "",                // MSTest filter expression
    "test-projects": [],                   // Test project paths
    "testrunner": "dotnet",                // Test runner to use
    "thresholds": { },                     // Pass/fail thresholds
    "verbosity": "info"                    // Logging level
  }
}
```

## Common Mistakes (from JavaScript/TypeScript Stryker)

| ❌ Wrong (JS) | ✅ Correct (.NET) | Notes |
|--------------|------------------|-------|
| `timeout-ms` | `additional-timeout` | Time is added to baseline |
| `test-runner` | `testrunner` (no dash) | Or omit entirely |
| `timeoutMS` | `additional-timeout` | .NET uses kebab-case |
| `coverageAnalysis` | `coverage-analysis` | .NET uses kebab-case |
| `mutationLevel` | `mutation-level` | .NET uses kebab-case |

## Anti-Slop Configuration Explained

### Mutation Level: "Complete"
```json
"mutation-level": "Complete"
```
- **Basic**: Only obvious mutations (AND→OR, +→-)
- **Standard**: Most common mutations
- **Complete**: All mutations including boundaries (>=→>, <=→<)

**Why Complete?** AI often skips boundary testing. Complete mode forces testing of `>=` vs `>`, which is where AI theater fails.

### Ignore String Mutations
```json
"ignore-mutations": [
  "String",    // "Hello" → "Stryker was here", $"{x}" mutations
  "Regex"      // Regex pattern mutations
]
```

**Why skip these?** They rarely find logic bugs and waste 30-50% of testing time. Focus on logic, not text.

### Coverage Analysis: perTest
```json
"coverage-analysis": "perTest"
```
- **off**: No coverage analysis (slow)
- **all**: Track which tests cover code (faster)
- **perTest**: Map each mutation to specific tests (best)

**Why perTest?** Exposes "generic" tests like `TestEverything()` that run code but don't verify it.

### High Thresholds
```json
"thresholds": {
  "break": 70,   // CI fails below this
  "low": 75,     // Warning threshold
  "high": 90     // Good threshold
}
```

**Why 70% break?** Forces AI-generated tests to actually verify logic, not just execute code.

### Incremental Mode (Git-Based)
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

**Why since?** Only test changed code via git comparison. Turns 2-day runs into 15-minute runs.

**⚠️ CANNOT be used with `baseline`**

### Baseline Storage (Comparison)
```json
"baseline": {
  "enabled": true,
  "provider": "disk"
}
```

**Why baseline?** Compare mutation scores between runs. Track quality over time.

**⚠️ CANNOT be used with `since`**

### Mutual Exclusivity

**You MUST choose ONE approach:**

| Approach | Config | Use Case |
|----------|--------|----------|
| **Git-based incremental** | `"since": { "enabled": true }` | Daily development, PR checks |
| **Baseline comparison** | `"baseline": { "enabled": true }` | Monthly quality reports, trend tracking |

**Recommended workflow:**
1. Initial run: Use `baseline` to establish quality baseline
2. Daily development: Use `since` for fast incremental testing
3. Monthly: Re-run with `baseline` to update quality trends

See `STRYKER_WORKFLOW.md` for complete details.

## Available Mutation Types to Ignore

Valid mutation types in Stryker.NET 4.12.0:

```json
"ignore-mutations": [
  "Statement",            // Statement-level mutations
  "Arithmetic",           // +, -, *, /, %
  "Block",                // { } removal
  "Equality",             // ==, !=, <, >, <=, >=
  "Boolean",              // true ↔ false
  "Logical",              // &&, ||, !
  "Assignment",           // +=, -=, *=, etc.
  "Unary",                // ++, --, +, -, !
  "Update",               // ++ → --
  "Checked",              // checked/unchecked
  "Linq",                 // LINQ method mutations
  "String",               // String literals and interpolation
  "Bitwise",              // &, |, ^, ~, <<, >>
  "Initializer",          // Array/object initializers
  "Regex",                // Regex patterns
  "NullCoalescing",       // ?? operator
  "Math",                 // Math.* method mutations
  "StringMethod",         // String method mutations
  "Conditional",          // ?: operator
  "CollectionExpression"  // Collection expressions
]
```

**Recommended to ignore for anti-slop testing:**
- `String` - String literal mutations (40% time save)
- `Regex` - Regex pattern mutations (rarely find bugs)

## Reporters

Available values for `reporters`:

```json
"reporters": [
  "html",        // Interactive HTML report
  "json",        // JSON for CI/CD
  "progress",    // Console progress bar
  "cleartext",   // Plain text summary
  "baseline",    // Compare to baseline
  "dashboard"    // Upload to Stryker Dashboard
]
```

## Quick Config Snippets

### Fast Development Testing
```json
{
  "stryker-config": {
    "mutation-level": "Standard",
    "concurrency": 8,
    "ignore-mutations": ["StringLiteral", "InterpolatedString"],
    "coverage-analysis": "all"
  }
}
```

### Full CI/CD Quality Check
```json
{
  "stryker-config": {
    "mutation-level": "Complete",
    "concurrency": 12,
    "ignore-mutations": ["StringLiteral", "InterpolatedString", "RegexChange"],
    "coverage-analysis": "perTest",
    "thresholds": { "break": 80 }
  }
}
```

### Differential Testing (Branch Changes Only)
```json
{
  "stryker-config": {
    "mutation-level": "Complete",
    "since": { "enabled": true },
    "baseline": { "enabled": true, "provider": "disk" }
  }
}
```

## Troubleshooting

### Error: Unknown configuration key
- Check the key name exactly matches the allowed list above
- Use kebab-case (dashes), not camelCase
- Remove any JS/TS Stryker keys

### Error: Initial test run failed
- Set `"break-on-initial-test-failure": false` to see the failure
- Run `dotnet test` manually to verify tests pass

### Tests timing out
- Increase `"additional-timeout": 20000` (20 seconds)
- Check for tests with network calls or long-running operations

### Too many mutations
- Add more patterns to `"ignore-mutations"`
- Use `"mutate"` to target specific files only

### Stryker eating all RAM
- Reduce `"concurrency"` to 6-8
- Enable `"coverage-analysis": "all"` instead of "perTest"

## Example: Full Anti-Slop Config

```json
{
  "stryker-config": {
    "project": "NinePSharp.Server/NinePSharp.Server.csproj",
    "test-projects": ["NinePSharp.Tests/NinePSharp.Tests.csproj"],
    "mutation-level": "Complete",
    "concurrency": 12,
    "additional-timeout": 10000,
    "coverage-analysis": "perTest",
    "ignore-mutations": [
      "StringLiteral",
      "InterpolatedString",
      "RegexChange"
    ],
    "mutate": [
      "**/*.cs",
      "!**/*Designer.cs",
      "!**/AssemblyInfo.cs",
      "!**/Program.cs"
    ],
    "since": { "enabled": true },
    "baseline": { "enabled": true, "provider": "disk" },
    "thresholds": { "break": 70, "low": 75, "high": 90 },
    "reporters": ["html", "progress", "json", "cleartext"],
    "disable-bail": false,
    "disable-mix-mutants": false
  }
}
```

This configuration:
- ✅ Tests all logic mutations (Complete)
- ✅ Skips string slop (40% time saved)
- ✅ Tracks which tests kill which mutations (perTest)
- ✅ Only tests changed code (since + baseline)
- ✅ Enforces 70% quality threshold
- ✅ Uses 96GB RAM efficiently (12 cores)
