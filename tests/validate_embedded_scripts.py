#!/usr/bin/env python3
from __future__ import annotations

import json
from pathlib import Path
import re
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
FAILURES: list[str] = []

REPLACEMENTS = {
    "__STATUS__": json.dumps("collect"),
    "__EXISTING_CAST_JSON__": "[]",
    "__PROFILE_ID__": json.dumps("123456"),
    "__SUBJECT_ID__": json.dumps("1292052"),
    "__PAYLOAD__": json.dumps({"subjectId": "1292052", "status": "collect", "rating": 5, "comment": "test"}, ensure_ascii=False),
}


def check_script(name: str, script: str) -> None:
    for token, value in REPLACEMENTS.items():
        script = script.replace(token, value)
    if re.search(r"__[A-Z0-9_]+__", script):
        FAILURES.append(f"{name}: unresolved placeholder")
        print(f"FAIL: {name} unresolved placeholder")
        return
    with tempfile.NamedTemporaryFile("w", suffix=".js", encoding="utf-8", delete=False) as handle:
        handle.write(script)
        temp_path = Path(handle.name)
    try:
        result = subprocess.run(["node", "--check", str(temp_path)], text=True, capture_output=True)
        if result.returncode:
            detail = (result.stderr or result.stdout).strip().replace("\n", " | ")
            FAILURES.append(f"{name}: {detail}")
            print(f"FAIL: {name} | {detail}")
        else:
            print(f"PASS: {name}")
    finally:
        temp_path.unlink(missing_ok=True)


def raw_constants(relative: str) -> list[tuple[str, str]]:
    source = (ROOT / relative).read_text(encoding="utf-8-sig")
    pattern = re.compile(
        r"(?:internal|private)\s+(?:static\s+)?(?:readonly\s+)?const\s+string\s+(?P<name>\w+)\s*=\s*\"\"\"\r?\n(?P<body>.*?)\r?\n\"\"\";",
        re.S,
    )
    return [(match.group("name"), match.group("body")) for match in pattern.finditer(source)]


for relative in ("BrowserCdpService.cs", "DoubanWebView2Connector.cs", "DoubanWebView2Connector.DeleteV2.cs"):
    scripts = raw_constants(relative)
    if not scripts:
        FAILURES.append(f"{relative}: no raw JavaScript constants found")
        print(f"FAIL: {relative} no raw JavaScript constants found")
    for name, body in scripts:
        check_script(f"{relative}:{name}", body)

# BuildSubmitScript uses a C# interpolated raw string. Only {{payload}} is a C#
# interpolation site; JavaScript template literals such as ${error} remain literal.
submit_source = (ROOT / "DoubanOfficialFormScripts.cs").read_text(encoding="utf-8-sig")
match = re.search(r"return \$\$\"\"\"\r?\n(?P<body>.*?)\r?\n\"\"\";", submit_source, re.S)
if not match:
    FAILURES.append("DoubanOfficialFormScripts.cs: BuildSubmitScript raw body not found")
    print("FAIL: DoubanOfficialFormScripts.cs BuildSubmitScript raw body not found")
else:
    payload = json.dumps({"SubjectId": "1292052", "Status": "collect", "Rating": 5, "Comment": "test"}, ensure_ascii=False)
    check_script("DoubanOfficialFormScripts.cs:BuildSubmitScript", match.group("body").replace("{{payload}}", payload))

print(f"SUMMARY: {0 if FAILURES else 'all'} embedded scripts valid; failures={len(FAILURES)}")
sys.exit(1 if FAILURES else 0)
