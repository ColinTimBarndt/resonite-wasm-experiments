use core::{fmt::Debug, marker::PhantomData, num::NonZeroU32};

use crate::{ParameterValType, ResultValType, ValType};

#[allow(non_camel_case_types)]
#[repr(transparent)]
#[derive(Debug)]
pub struct Extern(NonZeroU32);

impl Extern {
    #[inline(always)]
    pub fn as_ref<'r>(&'r self) -> ExternRef<'r> {
        ExternRef(self.0, PhantomData)
    }
}

#[cfg(target_family = "wasm")]
impl Drop for Extern {
    #[inline(always)]
    fn drop(&mut self) {
        unsafe {
            free(self.0.get());
        }

        #[link(wasm_import_module = "__table")]
        unsafe extern "C" {
            fn free(r: u32);
        }
    }
}

impl crate::private::Sealed for Extern {}
impl crate::private::SealedResult for Extern {}

impl ValType for Extern {
    type Value = u32;
    /// Marks this value for the postprocessor as an owned external reference
    const MARKER: [u8; 4] = *b"EXRo";
}

impl ResultValType for Extern {
    #[inline(always)]
    unsafe fn unbox(self) -> u32 {
        unsafe { core::mem::transmute(self) }
    }
}

impl crate::private::Sealed for Option<Extern> {}
impl crate::private::SealedParameter for Option<Extern> {}
impl crate::private::SealedResult for Option<Extern> {}

impl ValType for Option<Extern> {
    type Value = u32;
    /// Marks this value for the postprocessor as an owned external reference
    const MARKER: [u8; 4] = *b"EXRo";
}

impl ParameterValType for Option<Extern> {
    unsafe fn make_box(value: Self::Value) -> Self {
        NonZeroU32::new(value).map(Extern)
    }
}

impl ResultValType for Option<Extern> {
    #[inline(always)]
    unsafe fn unbox(self) -> u32 {
        let result = match &self {
            Some(v) => v.0.get(),
            None => 0,
        };
        core::mem::forget(self);
        result
    }
}

#[derive(Clone, Copy)]
pub struct ExternRef<'r>(NonZeroU32, PhantomData<&'r Extern>);

impl<'r> ExternRef<'r> {}

impl Debug for ExternRef<'_> {
    fn fmt(&self, f: &mut core::fmt::Formatter<'_>) -> core::fmt::Result {
        f.debug_tuple("ExternRef").field(&self.0).finish()
    }
}

impl crate::private::Sealed for ExternRef<'_> {}
impl crate::private::SealedResult for ExternRef<'_> {}

impl ValType for ExternRef<'_> {
    type Value = u32;
    /// Marks this value for the postprocessor as an unowned external reference
    const MARKER: [u8; 4] = *b"EXRr";
}

impl ResultValType for ExternRef<'_> {
    #[inline(always)]
    unsafe fn unbox(self) -> Self::Value {
        self.0.get()
    }
}

impl crate::private::Sealed for Option<ExternRef<'_>> {}
impl crate::private::SealedResult for Option<ExternRef<'_>> {}

impl ValType for Option<ExternRef<'_>> {
    type Value = u32;
    /// Marks this value for the postprocessor as an unowned external reference
    const MARKER: [u8; 4] = *b"EXRr";
}

impl ResultValType for Option<ExternRef<'_>> {
    #[inline(always)]
    unsafe fn unbox(self) -> Self::Value {
        match self {
            Some(v) => v.0.get(),
            None => 0,
        }
    }
}
