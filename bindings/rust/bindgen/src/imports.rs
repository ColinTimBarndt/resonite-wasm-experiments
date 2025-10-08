use std::{collections::BTreeMap, fmt::Display};

use serde::Deserialize;

pub type Namespaces = BTreeMap<String, Items>;

pub type Items = BTreeMap<String, ImportItem>;

#[derive(Debug, Deserialize)]
#[serde(tag = "type", rename_all = "snake_case")]
pub enum ImportItem {
    Function {
        #[serde(default)]
        doc: Vec<String>,
        parameters: Vec<WasmType>,
        results: Vec<WasmType>,
    },
    Global {
        #[serde(default)]
        doc: Vec<String>,
        value_type: WasmType,
    },
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum WasmType {
    I8 = 1,
    I16,
    I32,
    I64,
    F32,
    F64,
    V128,
    Externref,
    #[serde(rename = "$f")]
    TemplateFloat,
    #[serde(rename = "$ptr")]
    Pointer,
    #[serde(rename = "$mutptr")]
    PointerMut,
}

impl WasmType {
    pub fn make_instance<'a>(&'a self, instance: &'a WasmType) -> &'a Self {
        match self {
            Self::TemplateFloat => instance,
            other => other,
        }
    }

    pub const fn typ(&self) -> Option<&'static str> {
        Some(match self {
            Self::I8 => "i8",
            Self::I16 => "i16",
            Self::I32 => "i32",
            Self::I64 => "i64",
            Self::F32 => "f32",
            Self::F64 => "f64",
            Self::V128 => "core::arch::wasm32::v128",
            Self::Externref => "core::ffi::c_void", // TODO
            Self::Pointer => "*const core::ffi::c_void",
            Self::PointerMut => "*mut core::ffi::c_void",
            Self::TemplateFloat => return None,
        })
    }
}

impl Display for WasmType {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(match self.typ() {
            Some(typ) => typ,
            None => return Err(std::fmt::Error),
        })
    }
}
