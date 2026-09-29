use std::{env, process::Command};

fn main() {
    let target = env::var("TARGET").expect("Cargo must provide TARGET");
    println!("cargo:rustc-env=CEDARSHARP_TARGET={target}");
    let compiler = env::var("RUSTC").expect("Cargo must provide RUSTC");
    let output = Command::new(compiler).arg("--version").output().expect("run rustc --version");
    assert!(output.status.success(), "rustc --version failed");
    let version = String::from_utf8(output.stdout).expect("rustc version is UTF-8");
    let version = version.split_whitespace().nth(1).expect("rustc version token");
    println!("cargo:rustc-env=CEDARSHARP_RUST_VERSION={version}");
}
