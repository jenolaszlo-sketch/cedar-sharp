//! Versioned C ABI for the official Cedar policy engine.
//!
//! Inputs and outputs are UTF-8 JSON byte strings. Returned buffers are owned
//! by this library and must be released with [`cedarsharp_free_v1`].

use serde::Deserialize;
use serde_json::{json, Value};
use std::panic::{catch_unwind, AssertUnwindSafe};
use std::ptr;
use std::slice;

const ABI_VERSION: u32 = 1;
const MAX_INPUT_BYTES: usize = 16 * 1024 * 1024;
const MAX_OUTPUT_BYTES: usize = 64 * 1024 * 1024;

/// Owned byte buffer returned across the C ABI.
#[repr(C)]
#[derive(Debug)]
pub struct Buffer {
    pub data: *mut u8,
    pub len: usize,
}

impl Buffer {
    const EMPTY: Self = Self {
        data: ptr::null_mut(),
        len: 0,
    };
}

/// Returns the supported ABI version.
#[no_mangle]
pub extern "C" fn cedarsharp_abi_version() -> u32 {
    ABI_VERSION
}

/// Executes a Cedar JSON FFI operation.
///
/// # Safety
/// `input` must address `input_len` readable bytes unless it is null and the
/// length is zero. `out` must point to writable `Buffer` storage. The output
/// buffer must be released exactly once with `cedarsharp_free_v1`.
#[no_mangle]
pub unsafe extern "C" fn cedarsharp_call_v1(
    operation: u32,
    input: *const u8,
    input_len: usize,
    out: *mut Buffer,
) -> u32 {
    if out.is_null() {
        return 1;
    }

    // Initialize the caller's output before handling any other boundary case.
    unsafe { out.write(Buffer::EMPTY) };

    let result = catch_unwind(AssertUnwindSafe(|| {
        if input_len > MAX_INPUT_BYTES {
            return failure(out, 1, "input exceeds the 16 MiB limit");
        }
        if input.is_null() && input_len != 0 {
            return failure(out, 1, "input pointer is null for a non-empty input");
        }
        let bytes: &[u8] = if input_len == 0 {
            &[]
        } else {
            // The caller guarantees readable storage for the declared length.
            unsafe { slice::from_raw_parts(input, input_len) }
        };
        let input_text = match std::str::from_utf8(bytes) {
            Ok(text) => text,
            Err(error) => return failure(out, 1, &format!("input is not valid UTF-8: {error}")),
        };

        if operation == 0 {
            if input_len != 0 {
                match serde_json::from_str::<Value>(input_text) {
                    Ok(Value::Object(object)) if object.is_empty() => {}
                    Ok(_) => return failure(out, 1, "version operation expects an empty object"),
                    Err(error) => return failure(out, 1, &error.to_string()),
                }
            }
            return write_json(
                out,
                json!({
                    "abiVersion": ABI_VERSION,
                    "sdkVersion": cedar_policy::ffi::get_sdk_version(),
                    "languageVersion": cedar_policy::ffi::get_lang_version(),
                    "bridgeVersion": env!("CARGO_PKG_VERSION"),
                    "rustVersion": env!("CEDARSHARP_RUST_VERSION"),
                    "target": env!("CEDARSHARP_TARGET"),
                    "features": ["ipaddr", "decimal", "datetime"]
                }),
                0,
            );
        }

        let answer = match operation {
            1 => cedar_policy::ffi::is_authorized_json_str(input_text),
            2 => cedar_policy::ffi::validate_json_str(input_text),
            3 => cedar_policy::ffi::check_parse_policy_set_json_str(input_text),
            4 => cedar_policy::ffi::check_parse_schema_json_str(input_text),
            5 => cedar_policy::ffi::check_parse_entities_json_str(input_text),
            6 => cedar_policy::ffi::check_parse_context_json_str(input_text),
            7 => scope_variables(input_text),
            _ => return failure(out, 2, "unsupported operation"),
        };

        match answer {
            Ok(answer) => write_bytes(out, answer.as_bytes(), 0),
            Err(error) => failure(out, 1, &error.to_string()),
        }
    }));

    match result {
        Ok(status) => status,
        Err(_) => failure(out, 3, "recoverable panic in native bridge"),
    }
}

/// Frees a buffer returned by [`cedarsharp_call_v1`].
///
/// # Safety
/// `buffer` must be a buffer returned by this library and not previously freed.
#[no_mangle]
pub unsafe extern "C" fn cedarsharp_free_v1(buffer: Buffer) {
    if buffer.data.is_null() {
        return;
    }
    let raw = ptr::slice_from_raw_parts_mut(buffer.data, buffer.len);
    // SAFETY: The matching call allocated this exact boxed slice.
    drop(unsafe { Box::from_raw(raw) });
}

fn scope_variables(input: &str) -> Result<String, serde_json::Error> {
    // Upstream's ScopeVariablesParsingCall currently does not deny extra
    // fields. Deserialize this strict envelope first, preserving duplicate and
    // unknown-field checks, then delegate the actual Cedar work upstream.
    #[derive(Deserialize, serde::Serialize)]
    #[serde(deny_unknown_fields)]
    struct StrictScopeCall {
        principal: cedar_policy::ffi::EntityUid,
        action: cedar_policy::ffi::EntityUid,
        resource: cedar_policy::ffi::EntityUid,
        schema: cedar_policy::ffi::Schema,
    }

    let strict: StrictScopeCall = serde_json::from_str(input)?;
    let value = serde_json::to_value(strict)?;
    cedar_policy::ffi::check_parse_scope_variables_json(value)
        .and_then(|answer| serde_json::to_string(&answer))
}

fn failure(out: *mut Buffer, status: u32, message: &str) -> u32 {
    let value = json!({ "message": message });
    // The error envelope is tiny and bounded. Fall back to a static message if
    // serialization unexpectedly fails, without unwinding across the ABI.
    let bytes =
        serde_json::to_vec(&value).unwrap_or_else(|_| b"{\"message\":\"bridge error\"}".to_vec());
    let mut output = Buffer::EMPTY;
    if store_bytes(&bytes, &mut output) == 0 && !out.is_null() {
        // SAFETY: The exported entry point validates and initializes this pointer.
        unsafe { out.write(output) };
    }
    status
}

fn write_json(out: *mut Buffer, value: Value, status: u32) -> u32 {
    match serde_json::to_vec(&value) {
        Ok(bytes) => write_bytes(out, &bytes, status),
        Err(error) => failure(
            out,
            3,
            &format!("could not serialize bridge response: {error}"),
        ),
    }
}

fn write_bytes(out: *mut Buffer, bytes: &[u8], status: u32) -> u32 {
    if bytes.len() > MAX_OUTPUT_BYTES {
        return failure(out, 4, "output exceeds the 64 MiB limit");
    }
    let mut output = Buffer::EMPTY;
    let stored_status = store_bytes(bytes, &mut output);
    if stored_status != 0 {
        return failure(out, stored_status, "could not allocate output buffer");
    }
    // `out` was checked and initialized by the exported entry point.
    unsafe { out.write(output) };
    status
}

fn store_bytes(bytes: &[u8], out: &mut Buffer) -> u32 {
    if bytes.is_empty() {
        return 0;
    }
    let boxed = bytes.to_vec().into_boxed_slice();
    let len = boxed.len();
    out.data = Box::into_raw(boxed).cast::<u8>();
    out.len = len;
    0
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::{json, Value};
    use std::sync::Arc;

    unsafe fn call(operation: u32, input: &[u8]) -> (u32, Value) {
        let mut out = Buffer::EMPTY;
        let status =
            unsafe { cedarsharp_call_v1(operation, input.as_ptr(), input.len(), &mut out) };
        let value = if out.data.is_null() {
            Value::Null
        } else {
            let bytes = unsafe { slice::from_raw_parts(out.data, out.len) };
            let value = serde_json::from_slice(bytes).unwrap_or(Value::Null);
            unsafe { cedarsharp_free_v1(out) };
            value
        };
        (status, value)
    }

    fn auth_input(policy: &str) -> Value {
        json!({
            "principal": {"type":"User", "id":"alice"},
            "action": {"type":"Action", "id":"view"},
            "resource": {"type":"Photo", "id":"door"},
            "context": {},
            "policies": {"staticPolicies": policy},
            "entities": []
        })
    }

    #[test]
    fn versions_and_owned_buffer_round_trip() {
        assert_eq!(cedarsharp_abi_version(), 1);
        let (status, version) = unsafe { call(0, b"{}") };
        assert_eq!(status, 0);
        assert_eq!(version["abiVersion"], 1);
        assert_eq!(version["sdkVersion"], "4.13.0");
        assert_eq!(version["languageVersion"], "4.5");
        assert_eq!(version["rustVersion"], "1.94.0");
    }

    #[test]
    fn authorization_matches_upstream_for_permit_deny_and_forbid() {
        for (policy, decision) in [
            ("permit(principal == User::\"alice\", action, resource);", "allow"),
            ("permit(principal == User::\"bob\", action, resource);", "deny"),
            (
                "permit(principal, action, resource); forbid(principal == User::\"alice\", action, resource);",
                "deny",
            ),
        ] {
            let value = auth_input(policy);
            let input = serde_json::to_string(&value).unwrap();
            let (status, actual) = unsafe { call(1, input.as_bytes()) };
            let expected: Value = serde_json::from_str(
                &cedar_policy::ffi::is_authorized_json_str(&input).unwrap(),
            )
            .unwrap();
            assert_eq!(status, 0);
            assert_eq!(actual, expected);
            assert_eq!(actual["type"], "success");
            assert_eq!(
                actual["response"]["decision"]
                    .as_str()
                    .unwrap()
                    .to_lowercase(),
                decision
            );
        }
    }

    #[test]
    fn authorization_preserves_hierarchy_and_allow_with_policy_errors() {
        let hierarchy = json!({
            "principal":{"type":"User","id":"alice"},
            "action":{"type":"Action","id":"view"},
            "resource":{"type":"Photo","id":"door"}, "context":{},
            "policies":{"staticPolicies":"permit(principal, action, resource in Folder::\"house\");"},
            "entities":[{"uid":{"type":"Folder","id":"house"},"attrs":{},"parents":[]},
                        {"uid":{"type":"Photo","id":"door"},"attrs":{},"parents":[{"type":"Folder","id":"house"}]}]
        });
        let input = serde_json::to_string(&hierarchy).unwrap();
        let (status, result) = unsafe { call(1, input.as_bytes()) };
        assert_eq!(status, 0);
        assert_eq!(
            result["response"]["decision"]
                .as_str()
                .unwrap()
                .to_lowercase(),
            "allow"
        );
        assert_eq!(
            result,
            serde_json::from_str::<Value>(&cedar_policy::ffi::is_authorized_json_str(&input).unwrap())
                .unwrap()
        );

        let allow_with_error = auth_input(
            "permit(principal == User::\"alice\", action, resource); permit(principal, action, resource) when { context.missing };",
        );
        let input = serde_json::to_string(&allow_with_error).unwrap();
        let (status, result) = unsafe { call(1, input.as_bytes()) };
        assert_eq!(status, 0);
        assert_eq!(
            result,
            serde_json::from_str::<Value>(&cedar_policy::ffi::is_authorized_json_str(&input).unwrap())
                .unwrap()
        );
        assert_eq!(
            result["response"]["decision"]
                .as_str()
                .unwrap()
                .to_lowercase(),
            "allow"
        );
        assert!(!result["response"]["diagnostics"]["errors"]
            .as_array()
            .unwrap()
            .is_empty());
    }

    #[test]
    fn parse_and_validation_operations_preserve_upstream_payloads() {
        let cases = [
            (
                3,
                json!({"staticPolicies":"permit(principal, action, resource);"}),
            ),
            (4, json!({"" : {"entityTypes":{},"actions":{}}})),
            (5, json!({"entities":[],"schema":null})),
            (6, json!({"context":{},"schema":null,"action":null})),
            (
                7,
                json!({"principal":{"type":"User","id":"alice"},"action":{"type":"Action","id":"view"},"resource":{"type":"Photo","id":"door"},"schema":{ "": {"entityTypes":{},"actions":{}} }}),
            ),
        ];
        for (operation, value) in cases {
            let input = serde_json::to_string(&value).unwrap();
            let (status, actual) = unsafe { call(operation, input.as_bytes()) };
            assert_eq!(status, 0, "op {operation}: {actual}");
            let expected = match operation {
                3 => cedar_policy::ffi::check_parse_policy_set_json_str(&input).unwrap(),
                4 => cedar_policy::ffi::check_parse_schema_json_str(&input).unwrap(),
                5 => cedar_policy::ffi::check_parse_entities_json_str(&input).unwrap(),
                6 => cedar_policy::ffi::check_parse_context_json_str(&input).unwrap(),
                7 => serde_json::to_string(
                    &cedar_policy::ffi::check_parse_scope_variables_json(
                        serde_json::from_str(&input).unwrap(),
                    )
                    .unwrap(),
                )
                .unwrap(),
                _ => unreachable!(),
            };
            assert_eq!(actual, serde_json::from_str::<Value>(&expected).unwrap());
        }

        let validation = json!({
            "schema":{"" : {"entityTypes":{},"actions":{}}},
            "policies":{"staticPolicies":"permit(principal, action, resource);"}
        });
        let input = serde_json::to_string(&validation).unwrap();
        let (status, actual) = unsafe { call(2, input.as_bytes()) };
        assert_eq!(status, 0);
        assert_eq!(
            actual,
            serde_json::from_str::<Value>(&cedar_policy::ffi::validate_json_str(&input).unwrap()).unwrap()
        );
    }

    #[test]
    fn invalid_boundaries_and_unknown_fields_are_structured() {
        for (input, expected_message) in [
            (b"{bad".as_slice(), "key must be a string"),
            (b"\xff".as_slice(), "UTF-8"),
            (
                b"{\"staticPolicies\":\"permit(principal, action, resource);\",\"extra\":1}"
                    .as_slice(),
                "unknown field",
            ),
        ] {
            let (status, result) = unsafe { call(3, input) };
            assert_eq!(status, 1);
            assert!(result["message"]
                .as_str()
                .unwrap()
                .to_lowercase()
                .contains(&expected_message.to_lowercase()), "actual native error: {result}");
        }
        let (status, result) = unsafe { call(999, b"{}") };
        assert_eq!(status, 2);
        assert!(result["message"].as_str().unwrap().contains("unsupported"));

        let mut out = Buffer {
            data: 1usize as *mut u8,
            len: 42,
        };
        let status = unsafe { cedarsharp_call_v1(3, ptr::null(), 1, &mut out) };
        assert_eq!(status, 1);
        assert!(!out.data.is_null());
        let error = unsafe { slice::from_raw_parts(out.data, out.len) };
        assert!(serde_json::from_slice::<Value>(error).unwrap()["message"]
            .as_str()
            .unwrap()
            .contains("null"));
        unsafe { cedarsharp_free_v1(out) };

        let (status, result) = unsafe {
            call(
                7,
                br#"{"principal":{},"action":{},"resource":{},"schema":{},"extra":true}"#,
            )
        };
        assert_eq!(status, 1);
        assert!(result["message"]
            .as_str()
            .unwrap()
            .contains("unknown field"));

        let duplicate = br#"{"principal":{"type":"User","id":"a"},"principal":{"type":"User","id":"b"},"action":{},"resource":{},"schema":{}}"#;
        let (status, result) = unsafe { call(7, duplicate) };
        assert_eq!(status, 1);
        assert!(result["message"]
            .as_str()
            .unwrap()
            .contains("duplicate field"));

        let too_large = vec![b' '; MAX_INPUT_BYTES + 1];
        let (status, result) = unsafe { call(3, &too_large) };
        assert_eq!(status, 1);
        assert!(result["message"].as_str().unwrap().contains("16 MiB"));
    }

    #[test]
    fn concurrent_calls_do_not_share_authorization_state() {
        let cases: Vec<_> = (0..16)
            .map(|index| {
                let policy = if index % 2 == 0 {
                    "permit(principal == User::\"alice\", action, resource);"
                } else {
                    "permit(principal == User::\"bob\", action, resource);"
                };
                serde_json::to_vec(&auth_input(policy)).unwrap()
            })
            .collect();
        let cases = Arc::new(cases);
        let threads: Vec<_> = (0..8)
            .map(|thread_id| {
                let cases = Arc::clone(&cases);
                std::thread::spawn(move || {
                    for round in 0..30 {
                        let index = (thread_id + round) % cases.len();
                        let (status, result) = unsafe { call(1, &cases[index]) };
                        assert_eq!(status, 0);
                        let decision = if index % 2 == 0 { "allow" } else { "deny" };
                        assert_eq!(
                            result["response"]["decision"]
                                .as_str()
                                .unwrap()
                                .to_lowercase(),
                            decision
                        );
                    }
                })
            })
            .collect();
        for thread in threads {
            thread.join().unwrap();
        }
    }
}
