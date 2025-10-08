#[cfg(target_family = "wasm")]
pub mod math {
    #[link(wasm_import_module = "math")]
    unsafe extern "C" {
        pub fn acos_f32(arg0: f32) -> f32;
        pub fn acos_f64(arg0: f64) -> f64;
        pub fn asin_f32(arg0: f32) -> f32;
        pub fn asin_f64(arg0: f64) -> f64;
        pub fn atan2_f32(arg0: f32, arg1: f32) -> f32;
        pub fn atan2_f64(arg0: f64, arg1: f64) -> f64;
        pub fn atan_f32(arg0: f32) -> f32;
        pub fn atan_f64(arg0: f64) -> f64;
        pub fn cos_f32(arg0: f32) -> f32;
        pub fn cos_f64(arg0: f64) -> f64;
        pub fn cosh_f32(arg0: f32) -> f32;
        pub fn cosh_f64(arg0: f64) -> f64;
        pub fn exp_f32(arg0: f32) -> f32;
        pub fn exp_f64(arg0: f64) -> f64;
        pub fn log10_f32(arg0: f32) -> f32;
        pub fn log10_f64(arg0: f64) -> f64;
        pub fn log_f32(arg0: f32) -> f32;
        pub fn log_f64(arg0: f64) -> f64;
        pub fn pow_f32(arg0: f32, arg1: f32) -> f32;
        pub fn pow_f64(arg0: f64, arg1: f64) -> f64;
        pub fn sin_f32(arg0: f32) -> f32;
        pub fn sin_f64(arg0: f64) -> f64;
        pub fn sinh_f32(arg0: f32) -> f32;
        pub fn sinh_f64(arg0: f64) -> f64;
        pub fn sqrt_f32(arg0: f32) -> f32;
        pub fn sqrt_f64(arg0: f64) -> f64;
        pub fn tan_f32(arg0: f32) -> f32;
        pub fn tan_f64(arg0: f64) -> f64;
        pub fn tanh_f32(arg0: f32) -> f32;
        pub fn tanh_f64(arg0: f64) -> f64;
    }
}
#[cfg(target_family = "wasm")]
pub mod string {
    #[link(wasm_import_module = "string")]
    unsafe extern "C" {
        pub fn len_wtf16(arg0: core::ffi::c_void) -> i32;
        pub fn new_wtf16(arg0: *const core::ffi::c_void, arg1: i32) -> core::ffi::c_void;
        pub fn read_wtf16(arg0: core::ffi::c_void, arg1: i32, arg2: *mut core::ffi::c_void, arg3: i32) -> i32;
    }
}
