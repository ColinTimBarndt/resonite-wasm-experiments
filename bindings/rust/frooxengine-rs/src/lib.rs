#![no_std]

pub(crate) mod ffi;

pub use frooxengine_macros::*;

mod math;

pub use math::FloatExt;

#[cfg(all(target_family = "wasm", not(test)))]
#[panic_handler]
fn panic(_info: &core::panic::PanicInfo) -> ! {
    core::arch::wasm32::unreachable()
}
