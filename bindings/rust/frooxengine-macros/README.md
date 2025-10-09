# frooxengine-macros

This crate provides the `export_function` macro, which enables exporting
functions which return more than one value by returning a tuple.

## How it works

A function which returns multiple values is transformed into one
returning nothing and instead tail-calling another imported function.

```rs
#[export_function]
fn add_mul(a: i32, b: i32) -> (i32, i32) {
  (a + b, a * b)
}

// transformed into (simplified)
fn add_mul(a: i32, b: i32) {
  __return(a + b, a * b)
}
```

The imported return function is then detected by the `wasm-weaver` postprocessor
and removed. Since the correct values are on the stack as needed for a multi-value
return, it is only required to modify the function signature.
