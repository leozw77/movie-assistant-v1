# Project workflow

- Before behavior or architecture changes, present the approach, risks, exact file scope, validation plan, and release impact, then wait for the user's confirmation unless the user already gave explicit implementation approval.
- This directory is the canonical paired source for stable EXE `24054F951BB6621BAC762B1C840150FB269BBD44583DFEBD0FD38C6C8055E59E`. Read `..\DEVELOPMENT_BASELINE.json` and `..\DEVELOPMENT_DIRECTORY_INDEX.md` before editing.
- Stable package and rollback EXE are immutable references. Never edit or overwrite them.
- Before every preview build run `..\scripts\Verify-DevelopmentBaseline.ps1 -SourceRoot $PWD`; a failed baseline check blocks the build.
- Static checks and builds do not replace real signed-in WebView2 acceptance.
