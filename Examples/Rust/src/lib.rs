#![no_std]

use frooxengine_rs::*;

#[export_function]
fn sin_square(n: f32) -> f32 {
    let x = n.sin();
    x * x
}

#[export_function]
fn foo(a: i32) -> (i32, i32) {
    (a * 2, a * a)
}

#[export_function]
fn alot(x: i32) -> (i64, i64, i64) {
    let (a, b) = foo(x);
    let a = a as i64;
    let b = b as i64;
    (a * a, b * b, a * b)
}

#[export_function]
fn external(object: Option<Extern>, null: bool) -> Option<Extern> {
    if null { None } else { object }
}
