use std::ffi::c_char;

use binaryen_sys::*;

/// Optimizes a given binary WebAssembly module.
/// The returned slice is a leaked malloc allocation.
///
/// This function leaks memory on purpose because the
/// process is expected to terminate shortly after.
pub fn optimize(module: &[u8]) -> &'static mut [u8] {
    unsafe {
        let features = BinaryenFeatureGC()
            | BinaryenFeatureMultivalue()
            | BinaryenFeatureMutableGlobals()
            | BinaryenFeatureReferenceTypes();
        let module = BinaryenModuleReadWithFeatures(
            module.as_ptr().cast_mut().cast::<c_char>(),
            module.len(),
            features,
        );
        BinaryenSetOptimizeLevel(1);
        BinaryenSetShrinkLevel(2);
        BinaryenModuleOptimize(module);
        let result = BinaryenModuleAllocateAndWrite(module, std::ptr::null());
        std::slice::from_raw_parts_mut(result.binary.cast::<u8>(), result.binaryBytes)
    }
}
