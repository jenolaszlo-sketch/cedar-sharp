#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export CARGO_TARGET_DIR="${CARGO_TARGET_DIR:-$root/native/target}"

test_binary="$(cargo test --locked --manifest-path "$root/native/Cargo.toml" \
  --target x86_64-unknown-linux-gnu --lib --no-run --message-format=json | \
  python3 -c 'import json, sys
for line in sys.stdin:
    message = json.loads(line)
    if message.get("reason") == "compiler-artifact" and message.get("target", {}).get("name") == "cedarsharp_native" and message.get("executable"):
        print(message["executable"])')"

if [[ -z "$test_binary" ]]; then
  echo 'Could not locate the native test executable.' >&2
  exit 1
fi

# The shorter run keeps Valgrind practical in CI; the normal native suite runs
# the same deterministic mutations for 4096 cases on every supported platform.
CEDARSHARP_WIRE_CASES=256 valgrind --leak-check=full \
  --errors-for-leak-kinds=definite --error-exitcode=97 \
  "$test_binary" --exact tests::adversarial_wire_inputs_return_owned_json_without_panics
