use core::{f32, f64};

pub trait FloatExt {
    fn sin(self) -> Self;
    fn cos(self) -> Self;
    fn tan(self) -> Self;
    fn asin(self) -> Self;
    fn acos(self) -> Self;
    fn atan(self) -> Self;
    fn atan2(x: Self, y: Self) -> Self;
    fn sinh(self) -> Self;
    fn cosh(self) -> Self;
    fn tanh(self) -> Self;
    fn sqrt(self) -> Self;
    fn log(self) -> Self;
    fn log10(self) -> Self;
    fn exp(self) -> Self;
    fn pow(self, exp: Self) -> Self;
}

#[cfg(not(target_family = "wasm"))]
mod no_wasm {
    extern crate std;
    use super::*;

    macro_rules! stub {
        ($float:ty) => {
            impl FloatExt for $float {
                fn sin(self) -> Self {
                    <$float>::sin(self)
                }
                fn cos(self) -> Self {
                    <$float>::cos(self)
                }
                fn tan(self) -> Self {
                    <$float>::tan(self)
                }
                fn asin(self) -> Self {
                    <$float>::asin(self)
                }
                fn acos(self) -> Self {
                    <$float>::acos(self)
                }
                fn atan(self) -> Self {
                    <$float>::atan(self)
                }
                fn atan2(x: Self, y: Self) -> Self {
                    <$float>::atan2(x, y)
                }
                fn sinh(self) -> Self {
                    <$float>::sinh(self)
                }
                fn cosh(self) -> Self {
                    <$float>::cosh(self)
                }
                fn tanh(self) -> Self {
                    <$float>::tanh(self)
                }
                fn sqrt(self) -> Self {
                    <$float>::sqrt(self)
                }
                fn log(self) -> Self {
                    <$float>::ln(self)
                }
                fn log10(self) -> Self {
                    <$float>::log10(self)
                }
                fn exp(self) -> Self {
                    <$float>::exp(self)
                }
                fn pow(self, exp: Self) -> Self {
                    <$float>::powf(self, exp)
                }
            }
        };
    }

    #[cfg(not(target_family = "wasm"))]
    stub!(f32);

    #[cfg(not(target_family = "wasm"))]
    stub!(f64);
}

#[cfg(target_family = "wasm")]
mod wasm {
    use super::*;
    use crate::ffi;

    impl FloatExt for f32 {
        #[inline(always)]
        fn sin(self) -> Self {
            unsafe { ffi::math::sin_f32(self) }
        }
        #[inline(always)]
        fn cos(self) -> Self {
            unsafe { ffi::math::cos_f32(self) }
        }
        #[inline(always)]
        fn tan(self) -> Self {
            unsafe { ffi::math::tan_f32(self) }
        }
        #[inline(always)]
        fn asin(self) -> Self {
            unsafe { ffi::math::asin_f32(self) }
        }
        #[inline(always)]
        fn acos(self) -> Self {
            unsafe { ffi::math::acos_f32(self) }
        }
        #[inline(always)]
        fn atan(self) -> Self {
            unsafe { ffi::math::atan_f32(self) }
        }
        #[inline(always)]
        fn atan2(x: Self, y: Self) -> Self {
            unsafe { ffi::math::atan2_f32(x, y) }
        }
        #[inline(always)]
        fn sinh(self) -> Self {
            unsafe { ffi::math::sinh_f32(self) }
        }
        #[inline(always)]
        fn cosh(self) -> Self {
            unsafe { ffi::math::cosh_f32(self) }
        }
        #[inline(always)]
        fn tanh(self) -> Self {
            unsafe { ffi::math::tanh_f32(self) }
        }
        #[inline(always)]
        fn sqrt(self) -> Self {
            unsafe { ffi::math::sqrt_f32(self) }
        }
        #[inline(always)]
        fn log(self) -> Self {
            unsafe { ffi::math::log_f32(self) }
        }
        #[inline(always)]
        fn log10(self) -> Self {
            unsafe { ffi::math::log10_f32(self) }
        }
        #[inline(always)]
        fn exp(self) -> Self {
            unsafe { ffi::math::exp_f32(self) }
        }
        #[inline(always)]
        fn pow(self, exp: Self) -> Self {
            unsafe { ffi::math::pow_f32(self, exp) }
        }
    }

    impl FloatExt for f64 {
        #[inline(always)]
        fn sin(self) -> Self {
            unsafe { ffi::math::sin_f64(self) }
        }
        #[inline(always)]
        fn cos(self) -> Self {
            unsafe { ffi::math::cos_f64(self) }
        }
        #[inline(always)]
        fn tan(self) -> Self {
            unsafe { ffi::math::tan_f64(self) }
        }
        #[inline(always)]
        fn asin(self) -> Self {
            unsafe { ffi::math::asin_f64(self) }
        }
        #[inline(always)]
        fn acos(self) -> Self {
            unsafe { ffi::math::acos_f64(self) }
        }
        #[inline(always)]
        fn atan(self) -> Self {
            unsafe { ffi::math::atan_f64(self) }
        }
        #[inline(always)]
        fn atan2(x: Self, y: Self) -> Self {
            unsafe { ffi::math::atan2_f64(x, y) }
        }
        #[inline(always)]
        fn sinh(self) -> Self {
            unsafe { ffi::math::sinh_f64(self) }
        }
        #[inline(always)]
        fn cosh(self) -> Self {
            unsafe { ffi::math::cosh_f64(self) }
        }
        #[inline(always)]
        fn tanh(self) -> Self {
            unsafe { ffi::math::tanh_f64(self) }
        }
        #[inline(always)]
        fn sqrt(self) -> Self {
            unsafe { ffi::math::sqrt_f64(self) }
        }
        #[inline(always)]
        fn log(self) -> Self {
            unsafe { ffi::math::log_f64(self) }
        }
        #[inline(always)]
        fn log10(self) -> Self {
            unsafe { ffi::math::log10_f64(self) }
        }
        #[inline(always)]
        fn exp(self) -> Self {
            unsafe { ffi::math::exp_f64(self) }
        }
        #[inline(always)]
        fn pow(self, exp: Self) -> Self {
            unsafe { ffi::math::pow_f64(self, exp) }
        }
    }
}
