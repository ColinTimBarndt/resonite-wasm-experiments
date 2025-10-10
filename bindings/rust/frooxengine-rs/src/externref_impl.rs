use core::{fmt::Debug, marker::PhantomData};

use crate::ValType;

#[allow(non_camel_case_types)]
#[repr(transparent)]
#[derive(Debug)]
pub struct externref {
    index: u32,
    _phantom: PhantomData<*mut ()>,
}

impl externref {
    pub const NULL: externref = externref {
        index: 0,
        _phantom: PhantomData,
    };

    #[inline(always)]
    pub fn is_null(&self) -> bool {
        self.index == 0
    }
}

#[cfg(target_family = "wasm")]
impl Drop for externref {
    #[inline(always)]
    fn drop(&mut self) {
        if self.is_null() {
            return;
        }
        unsafe {
            free(self.index);
        }

        #[link(wasm_import_module = "__table")]
        unsafe extern "C" {
            fn free(r: u32);
        }
    }
}

impl crate::private::Sealed for externref {}

impl ValType for externref {
    type Value = u32;

    #[inline(always)]
    unsafe fn make_box(value: u32) -> Self {
        return Self {
            index: unsafe { alloc(value) },
            _phantom: PhantomData,
        };

        #[link(wasm_import_module = "__table")]
        unsafe extern "C" {
            fn alloc(r: u32) -> u32;
        }
    }

    #[inline(always)]
    unsafe fn unbox(self) -> u32 {
        return unsafe { get(self.index) };

        #[link(wasm_import_module = "__table")]
        unsafe extern "C" {
            fn get(r: u32) -> u32;
        }
    }
}
