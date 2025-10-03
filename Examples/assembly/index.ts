// The entry file of your WebAssembly module.

export function add(a: i32, b: i32): i32 {
  return a + b;
}

export function pick(a: externref, b: externref, index: usize): externref {
  return index % 2 ? b : a;
}

let global_int: i32 = 0;

export function store_int(value: i32): void {
  global_int = value;
}

export function load_int(): i32 {
  return global_int;
}

// Source: https://github.com/YuriyVorobyov96/asm-script-fast-inv-sqrt
// See: https://en.wikipedia.org/wiki/Fast_inverse_square_root
export function fastInvSqrt(number: f32): f32 {
  const halfNumber: f32 = number * 0.5;
  const threeHalfs: f32 = 1.5;

  let i = reinterpret<u32>(number);

  i = 0x5f3759df - (i >> 1); // what the fuck? :)

  number = reinterpret<f32>(i);

  number = number * (threeHalfs - halfNumber * number * number);

  return number;
}
