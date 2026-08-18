#!/usr/bin/env python3
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
node = shutil.which("node")
if not node:
    print("FAIL: Node.js is required for review-protocol fixtures")
    sys.exit(1)
result = subprocess.run([node, str(ROOT / "tests" / "review-protocol.test.js")], cwd=ROOT)
sys.exit(result.returncode)
