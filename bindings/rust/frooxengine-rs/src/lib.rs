#![no_std]

mod externref_impl;
pub(crate) mod ffi;
mod math;

pub use frooxengine_macros::*;

pub use externref_impl::externref;
pub use math::FloatExt;

#[cfg(all(target_family = "wasm", not(test)))]
#[panic_handler]
fn panic(_info: &core::panic::PanicInfo) -> ! {
    core::arch::wasm32::unreachable()
}

mod private {
    #[doc(hidden)]
    pub trait Sealed {}
}

/// A WebAssembly value type.
pub trait ValType: private::Sealed {
    type Value;

    #[doc(hidden)]
    unsafe fn make_box(value: Self::Value) -> Self;

    #[doc(hidden)]
    unsafe fn unbox(self) -> Self::Value;
}

macro_rules! val_identity {
    ($($T:ty)*) => {
        $(
            impl private::Sealed for $T {}
            impl ValType for $T {
                type Value = Self;
                #[inline(always)]
                unsafe fn make_box(value: Self) -> Self { value }
                #[inline(always)]
                unsafe fn unbox(self) -> Self { self }
            }
        )*
    };
}

val_identity!(u8 i8 u16 i16 u32 i32 u64 i64 f32 f64 bool);

#[cfg(target_family = "wasm")]
val_identity!(core::arch::wasm32::v128);
