#![no_std]

use frooxengine_rs::*;

#[unsafe(no_mangle)]
extern "C" fn sin_square(n: f32) -> f32 {
    let x = n.sin();
    x * x
}
