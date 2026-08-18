from pathlib import Path
import sys

root = Path(__file__).resolve().parents[1]
cmd = root / "BUILD_PREVIEW.cmd"
ps1 = root / "scripts" / "Build-Preview.ps1"
failures = []

cmd_bytes = cmd.read_bytes()
if not cmd_bytes:
    failures.append("BUILD_PREVIEW.cmd is empty")
if cmd_bytes.startswith((b"\xef\xbb\xbf", b"\xff\xfe", b"\xfe\xff")):
    failures.append("BUILD_PREVIEW.cmd must not contain a BOM")
if any(byte >= 128 for byte in cmd_bytes):
    failures.append("BUILD_PREVIEW.cmd must be ASCII-only")
if b"\n" in cmd_bytes.replace(b"\r\n", b""):
    failures.append("BUILD_PREVIEW.cmd contains bare LF line endings")
required_cmd = [
    b"@echo off\r\n",
    b"%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe",
    b'-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File',
    b'"%~dp0scripts\\Build-Preview.ps1"',
]
for token in required_cmd:
    if token not in cmd_bytes:
        failures.append(f"BUILD_PREVIEW.cmd is missing token: {token!r}")

ps1_bytes = ps1.read_bytes()
if not ps1_bytes.startswith(b"\xef\xbb\xbf"):
    failures.append("Build-Preview.ps1 must use a UTF-8 BOM for Windows PowerShell 5.1")
if b"\n" in ps1_bytes.replace(b"\r\n", b""):
    failures.append("Build-Preview.ps1 contains bare LF line endings")

if failures:
    for item in failures:
        print("FAIL:", item)
    sys.exit(1)

print("PASS: BUILD_PREVIEW.cmd is ASCII-only, BOM-free, and CRLF-normalized")
print("PASS: Build-Preview.ps1 is UTF-8 BOM and CRLF-normalized")
print("PASS: launcher uses an explicit Windows PowerShell path and quoted script path")
