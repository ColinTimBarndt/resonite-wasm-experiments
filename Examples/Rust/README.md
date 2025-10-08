# Rust Example

This example uses the bindings of this workspace. For a project, the `Cargo.toml` dependency will need to be updated to point to get git repository.

The `.cargo` folder can be copied into the root of a project to get proper linting for the WebAssembly target.

```toml
[dependencies]
frooxengine-rs = { version = "*", git = "https://github.com/ColinTimBarndt/resonite-wasm-experiments.git" }
```

## Building

```sh
cargo build --target wasm32-unknown-unknown --release
```

I don't recommend building the debug target since it only inflates the binary size.

You can find the `.wasm` file at `target/wasm32-unknown-unknown/release`.
