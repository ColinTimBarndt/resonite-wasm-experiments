use wasm_encoder::{Instruction, reencode::Reencode};
use wasmparser::{ExternalKind, Parser, Payload, TypeRef};

use crate::weaver::{Sections, WeaveError, Weaver, indices::Indices};

/// https://github.com/ColinTimBarndt/wasm-table-slab
static TABLE_SLAB: &[u8] = include_bytes!("table-slab.wasm");

pub struct TableSlabApi {
    alloc_fn: u32,
    free_fn: u32,
    take_fn: u32,
    items_table: u32,
}

impl TableSlabApi {
    pub fn alloc_extern(&self) -> Instruction<'static> {
        Instruction::Call(self.alloc_fn)
    }

    pub fn free(&self) -> Instruction<'static> {
        Instruction::Call(self.free_fn)
    }

    pub fn take_extern(&self) -> Instruction<'static> {
        Instruction::Call(self.take_fn)
    }

    pub fn get_extern(&self) -> [Instruction<'static>; 2] {
        [
            Instruction::TableGet(self.items_table),
            Instruction::ExternConvertAny,
        ]
    }

    pub fn map_import(
        &self,
        indices: &mut Indices,
        index: u32,
        kind: TypeRef,
        name: &str,
    ) -> super::Result<()> {
        match (kind, name) {
            (TypeRef::Func(_), "free") => {
                indices.funcs.add_mapping(index, self.free_fn);
                Ok(())
            }
            _ => Err(WeaveError::ImportNotFound(name.to_string()).into()),
        }
    }

    pub fn include(weaver: &mut Weaver, sections: &mut Sections) -> super::Result<Self> {
        let mut encoder = TableSlabEncoder::new(weaver, sections);
        encoder.reencode()?;
        Ok(Self {
            alloc_fn: encoder.alloc_fn.unwrap(),
            free_fn: encoder.free_fn.unwrap(),
            take_fn: encoder.take_fn.unwrap(),
            items_table: encoder.items_table.unwrap(),
        })
    }
}

struct TableSlabEncoder<'w, 'm: 'a, 'a> {
    weaver: &'w mut Weaver<'m, 'a>,
    sections: Option<&'w mut Sections>,
    types: Vec<wasmparser::SubType>,
    type_indices: Vec<u32>,
    function_indices: Vec<u32>,
    table_indices: Vec<u32>,
    global_indices: Vec<u32>,

    alloc_fn: Option<u32>,
    free_fn: Option<u32>,
    take_fn: Option<u32>,
    items_table: Option<u32>,
}

impl<'w, 'm: 'a, 'a> TableSlabEncoder<'w, 'm, 'a> {
    fn new(weaver: &'w mut Weaver<'m, 'a>, sections: &'w mut Sections) -> Self {
        Self {
            weaver,
            sections: Some(sections),
            types: Vec::new(),
            type_indices: Vec::new(),
            function_indices: Vec::new(),
            table_indices: Vec::new(),
            global_indices: Vec::new(),
            alloc_fn: None,
            free_fn: None,
            take_fn: None,
            items_table: None,
        }
    }

    fn reencode(&mut self) -> super::Result<()> {
        let sections = self.sections.take().unwrap();

        for payload in Parser::new(0).parse_all(TABLE_SLAB) {
            match payload? {
                Payload::Version { .. } => continue,
                Payload::TypeSection(section) => {
                    for result in section {
                        let rec_group = result?;
                        let base_idx = self.weaver.new_parser_rec_group(&rec_group)?;
                        self.type_indices
                            .extend(base_idx..(base_idx + rec_group.types().len() as u32));
                        self.types.extend(rec_group.into_types());
                    }
                }
                Payload::ImportSection(_) => panic!("unexpected import section"),
                Payload::FunctionSection(section) => {
                    let functions = sections.functions.get_or_insert_default();
                    for result in section {
                        let ty = result?;
                        self.function_indices
                            .push(self.weaver.indices.funcs.reserve());
                        functions.function(self.weaver.new_parser_ty(&self.types[ty as usize])?);
                    }
                }
                Payload::TableSection(section) => {
                    let tables = sections.tables.get_or_insert_default();
                    for result in section {
                        let table = result?;
                        self.table_indices
                            .push(self.weaver.indices.tables.reserve());
                        self.weaver.parse_table(tables, table)?;
                    }
                }
                Payload::MemorySection(_) => panic!("unexpected memory section"),
                Payload::TagSection(_) => panic!("unexpected tag section"),
                Payload::GlobalSection(section) => {
                    let globals = sections.globals.get_or_insert_default();
                    for result in section {
                        let global = result?;
                        self.global_indices
                            .push(self.weaver.indices.globals.reserve());
                        self.weaver.parse_global(globals, global)?;
                    }
                }
                Payload::ExportSection(section) => {
                    for result in section {
                        let export = result?;
                        match (export.kind, export.name) {
                            (ExternalKind::Func, "alloc") => {
                                self.alloc_fn = Some(self.function_indices[export.index as usize])
                            }
                            (ExternalKind::Func, "free") => {
                                self.free_fn = Some(self.function_indices[export.index as usize])
                            }
                            (ExternalKind::Func, "take") => {
                                self.take_fn = Some(self.function_indices[export.index as usize])
                            }
                            (ExternalKind::Table, "items") => {
                                self.items_table = Some(self.table_indices[export.index as usize])
                            }
                            _ => continue,
                        }
                    }
                }
                Payload::StartSection { .. } => panic!("unexpected start section"),
                Payload::ElementSection(_) => panic!("unexpected element section"),
                Payload::DataCountSection { count: 0, .. } => continue,
                Payload::DataCountSection { .. } => panic!("expected data count to be zero"),
                Payload::DataSection(_) => panic!("unexpected data section"),
                Payload::CodeSectionStart { .. } => continue,
                Payload::CodeSectionEntry(function_body) => {
                    self.parse_function_body(sections.code.get_or_insert_default(), function_body)?;
                }
                Payload::UnknownSection { .. } => continue,
                Payload::CustomSection(_) => continue,
                Payload::End(_) => break,
                _ => unimplemented!(),
            }
        }
        Ok(())
    }
}

impl<'w, 'm: 'a, 'a> Reencode for TableSlabEncoder<'w, 'm, 'a> {
    type Error = WeaveError;

    fn type_index(&mut self, ty: u32) -> super::Result<u32> {
        Ok(self.type_indices[ty as usize])
    }

    fn function_index(
        &mut self,
        func: u32,
    ) -> Result<u32, wasm_encoder::reencode::Error<Self::Error>> {
        Ok(self.function_indices[func as usize])
    }

    fn table_index(
        &mut self,
        table: u32,
    ) -> Result<u32, wasm_encoder::reencode::Error<Self::Error>> {
        Ok(self.table_indices[table as usize])
    }

    fn global_index(
        &mut self,
        global: u32,
    ) -> Result<u32, wasm_encoder::reencode::Error<Self::Error>> {
        Ok(self.global_indices[global as usize])
    }
}
