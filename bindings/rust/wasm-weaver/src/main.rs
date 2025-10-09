use std::{error::Error, io::Read, path::PathBuf};

use clap::Parser as _;
use wasm_encoder::Module;
use wasmparser::Parser;

use crate::{args::BuildArgs, parse::ParsedModule, weaver::Weaver};

mod args;
mod cargo;
mod parse;
mod type_allocator;
mod weaver;

fn main() -> Result<(), Box<dyn Error>> {
    let command = args::RootArgs::parse();

    match command {
        args::RootArgs::Build(args) => build(args),
        args::RootArgs::Weave(args::WeaveArgs { input, output }) => weave(input, output),
    }
}

fn build(args: BuildArgs) -> Result<(), Box<dyn Error>> {
    match cargo::build(&args)? {
        None => {
            eprintln!("No artifacts");
            std::process::exit(1);
        }
        Some(artifact) => weave(artifact, args.output),
    }
}

fn weave(input: PathBuf, output: PathBuf) -> Result<(), Box<dyn Error>> {
    let mut file = std::fs::OpenOptions::new().read(true).open(input)?;
    let file_size: usize = file.metadata()?.len().try_into()?;
    let mut wasm_buf = Vec::with_capacity(file_size);
    file.read_to_end(&mut wasm_buf)?;
    drop(file);
    // Since this program is only invoked once per binary, it is fine to leak
    let wasm_buf: &'static [u8] = wasm_buf.leak();

    let parsed = ParsedModule::read(Parser::new(0), wasm_buf)?;

    let weaver = Weaver::new(&parsed);
    let mut module = Module::new();
    weaver.encode(&mut module)?;
    let module_buf = module.finish();

    wasmparser::validate(&module_buf)?;

    std::fs::write(output, &module_buf)?;

    Ok(())
}
