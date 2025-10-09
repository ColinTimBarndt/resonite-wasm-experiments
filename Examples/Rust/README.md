# Rust Example

This example uses the bindings of this workspace. For a project, the `Cargo.toml` dependency will need to be updated to point to get git repository.

The `.cargo` folder can be copied into the root of a project to get proper linting for the WebAssembly target.

```toml
[dependencies]
frooxengine-rs = { version = "*", git = "https://github.com/ColinTimBarndt/resonite-wasm-experiments.git" }
```

## Building with wasm-weaver

`wasm-weaver` is a build tool which post-processes WebAssembly files which have
metadata imports to add support for multi-value return functions.
The `[export_function]` macro generates this metadata. Without postprocessing,
the WebAssembly file can't be used.

You can either use the supplied binary or compile and install the weaver using:

```sh
cargo install --path ./bindings/rust/wasm-weaver
```

```sh
# If you're in a project
wasm-weaver build -o example.wasm
# If you're in a workspace
wasm-weaver build -p example -o example.wasm
```

## Building manually

```sh
cargo build --target wasm32-unknown-unknown --release
wasm-weaver weave -o example.wasm ../../target/wasm32-unknown-unknown/release/example.wasm
```

I don't recommend building the debug target since it only inflates the binary size.

You can find the `.wasm` file at `target/wasm32-unknown-unknown/release`.
