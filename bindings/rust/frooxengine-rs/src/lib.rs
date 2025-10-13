#![no_std]

mod externref_impl;
pub(crate) mod ffi;
mod math;

pub use frooxengine_macros::*;

pub use externref_impl::*;
pub use math::FloatExt;

#[cfg(all(target_family = "wasm", not(test)))]
#[panic_handler]
fn panic(_info: &core::panic::PanicInfo) -> ! {
    core::arch::wasm32::unreachable()
}

mod private {
    #[doc(hidden)]
    pub trait Sealed {}

    #[doc(hidden)]
    pub trait SealedParameter {}

    #[doc(hidden)]
    pub trait SealedResult {}
}

/// A WebAssembly value type.
pub trait ValType: private::Sealed {
    #[doc(hidden)]
    type Value;

    /// Used internally when generating function signature metadata.
    #[doc(hidden)]
    const MARKER: [u8; 4] = [0; 4];
}

/// A type which can be used as a parameter in exported functions.
pub trait ParameterValType: ValType + private::SealedParameter {
    #[doc(hidden)]
    unsafe fn make_box(value: Self::Value) -> Self;
}

/// A type which can be used as a result in exported functions.
pub trait ResultValType: ValType + private::SealedResult {
    #[doc(hidden)]
    unsafe fn unbox(self) -> Self::Value;
}

macro_rules! val_identity {
    ($($T:ty)*) => {
        $(
            impl private::Sealed for $T {}
            impl private::SealedParameter for $T {}
            impl private::SealedResult for $T {}
            impl ValType for $T {
                type Value = Self;
            }
            impl ParameterValType for $T {
                #[inline(always)]
                unsafe fn make_box(value: Self) -> Self { value }
            }
            impl ResultValType for $T {
                #[inline(always)]
                unsafe fn unbox(self) -> Self { self }
            }
        )*
    };
}

val_identity!(u8 i8 u16 i16 u32 i32 u64 i64 f32 f64 bool);

#[cfg(target_family = "wasm")]
val_identity!(core::arch::wasm32::v128);
