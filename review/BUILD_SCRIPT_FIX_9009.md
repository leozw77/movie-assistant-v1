# Build Script Fix 1: Python exit code 9009

## Symptom

The build stopped at step 1 with:

```text
Python review-pipeline validation failed with exit code 9009.
```

## Cause

Windows App Execution Aliases may expose `python.exe` in the Microsoft Store
`WindowsApps` directory even when a usable Python runtime is not installed.
`Get-Command python` therefore succeeds, but launching the placeholder returns
exit code 9009.

## Fix

`scripts/Build-Preview.ps1` now:

1. Tries the Python launcher as `py -3`.
2. Ignores Microsoft Store `WindowsApps\\python.exe` placeholders.
3. Tries usable `python` and `python3` applications.
4. Probes `--version` before running validation.
5. Skips optional Python validation when no usable runtime exists.
6. Continues to the required .NET restore/build/publish steps.

Python and Node.js are optional validation tools. The .NET 8 SDK remains a
required build dependency.
