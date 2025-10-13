use std::{error::Error, io::Write};

use clap::Parser;

use crate::imports::{FormatParameterType, FormatResultType, ImportItem, WasmType};

mod args;
mod imports;

fn main() -> Result<(), Box<dyn Error>> {
    let args = args::BuildArgs::parse();
    let imports_reader = std::fs::OpenOptions::new().read(true).open(args.imports)?;
    let imports: imports::Namespaces = serde_json::from_reader(imports_reader)?;
    let mut out = std::fs::OpenOptions::new()
        .write(true)
        .create(true)
        .truncate(true)
        .open(args.output)?;

    println!("Writing to: {out:?}");

    writeln!(out, "#![allow(unused)]")?;

    for (ns, items) in imports {
        let modname = ns.replace('.', "_");
        writeln!(out, r#"#[cfg(target_family = "wasm")]"#)?;
        writeln!(out, "pub mod {modname} {{")?;
        writeln!(
            out,
            r#"    #[link(wasm_import_module = "{}")]"#,
            ns.escape_default()
        )?;
        writeln!(out, r#"    unsafe extern "C" {{"#)?;

        for (name, item) in items {
            if name.contains("$f") {
                for instance in [WasmType::F32, WasmType::F64] {
                    let name = name.replace("$f", instance.typ(false).unwrap());
                    write_item(&mut out, &name, &item, Some(&instance))?;
                }
                continue;
            }

            write_item(&mut out, &name, &item, None)?;
        }

        writeln!(out, r#"    }}"#)?;
        writeln!(out, r#"}}"#)?;
    }

    Ok(())
}

fn write_item(
    mut out: impl Write,
    name: &str,
    item: &ImportItem,
    instance: Option<&WasmType>,
) -> std::io::Result<()> {
    match item {
        ImportItem::Function {
            doc,
            parameters,
            results,
        } => {
            if results.len() > 1 {
                return Ok(());
            }
            for doc_line in doc {
                writeln!(out, r#"        #[doc = "{}"]"#, doc_line.escape_default())?;
            }
            write!(out, "        pub fn {name}(")?;
            for (i, mut param) in parameters.iter().enumerate() {
                if let Some(instance) = instance {
                    param = param.make_instance(instance);
                }
                if i != 0 {
                    write!(out, ", ")?;
                }
                write!(out, "arg{i}: {}", FormatParameterType(param))?;
            }
            write!(out, ")")?;
            if let Some(mut res) = results.first() {
                if let Some(instance) = instance {
                    res = res.make_instance(instance);
                }
                write!(out, " -> {}", FormatResultType(res))?;
            }
            writeln!(out, ";")?;
        }
        ImportItem::Global { doc, value_type } => {
            return Ok(()); // TODO
        }
    }

    Ok(())
}
