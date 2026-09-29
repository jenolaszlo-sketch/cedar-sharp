# Native bridge

`Cargo.toml` pins the official cedar-policy crate; `Cargo.lock` fixes its dependency
graph. The root rust-toolchain.toml pins Rust. `src/lib.rs` implements ABI 1 and
native differential/boundary tests; see [the contract](../docs/native-boundary.md).

Use `eng/Build-Native.ps1` and `eng/Test-Native.ps1` from the repository root.
Windows requires the MSVC C++ linker/SDK. Linux uses its native C compiler/glibc,
and macOS ARM64 uses Xcode command-line tools on a native ARM64 runner. Rust is
required only when building the bridge, not for consuming a finished package.

Generated `target/` and `staging/` assets are ignored. Staging includes per-RID
native binaries, manifests, and dependency licenses/notices. Do not check in
local tool downloads or assume generated files have passed release qualification.
