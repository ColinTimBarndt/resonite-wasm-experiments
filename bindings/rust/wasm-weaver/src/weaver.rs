use std::{borrow::Cow, collections::HashMap};

use wasm_encoder::{
    reencode::{Reencode, utils},
    *,
};

use crate::{
    parse::{FunctionLookup, ParsedModule, TypeLookup},
    type_allocator::HashableType,
};

#[derive(Default)]
struct Sections {
    imports: Option<ImportSection>,
    functions: Option<FunctionSection>,
    tables: Option<TableSection>,
    memories: Option<MemorySection>,
    tags: Option<TagSection>,
    globals: Option<GlobalSection>,
    exports: Option<ExportSection>,
    start: Option<StartSection>,
    elements: Option<ElementSection>,
    data_count: Option<DataCountSection>,
    data: Option<DataSection>,
    code: Option<CodeSection>,
}

impl Sections {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn encode(self, module: &mut Module, types: TypeSection) -> Result<()> {
        module.section(&types);
        Self::encode_section(module, &self.imports);
        Self::encode_section(module, &self.functions);
        Self::encode_section(module, &self.tables);
        Self::encode_section(module, &self.memories);
        Self::encode_section(module, &self.tags);
        Self::encode_section(module, &self.globals);
        Self::encode_section(module, &self.exports);
        Self::encode_section(module, &self.start);
        Self::encode_section(module, &self.elements);
        Self::encode_section(module, &self.data_count);
        Self::encode_section(module, &self.code);
        Self::encode_section(module, &self.data);
        Ok(())
    }

    fn encode_section(module: &mut Module, section: &Option<impl Section>) {
        if let Some(section) = section {
            module.section(section);
        }
    }
}

#[derive(thiserror::Error, Debug)]
pub enum WeaveError {
    #[error("type index out of bounds: {0}")]
    TypeIndexOutOfBounds(u32),
    #[error("function index out of bounds: {0}")]
    FunctionIndexOutOfBounds(u32),
    #[error("function type is not function: {0}")]
    FunctionTypeIsNotFunction(u32),
    #[error("returns marker is not of type function: {0}")]
    ReturnsMarkerIsNotFunction(String),
    #[error("unexpected marker function call at function {0:?}")]
    UnexpectedMarkerFunctionCall(Option<u32>),
}

impl From<WeaveError> for Error {
    fn from(value: WeaveError) -> Self {
        Self::UserError(value)
    }
}

type Error = reencode::Error<WeaveError>;
type Result<T, E = Error> = std::result::Result<T, E>;

pub struct Weaver<'m: 'a, 'a> {
    parsed: &'m ParsedModule<'a>,
    type_map: HashMap<u32, u32>,
    fn_map: HashMap<u32, u32>,
    type_indices: HashMap<HashableType, u32>,
    type_section: TypeSection,
    current_type_index: u32,
    current_fn_index: u32,
    ty_lookup: TypeLookup<'a>,
    fn_lookup: FunctionLookup<'m, 'a>,
    returns_lookup: HashMap<&'a str, (&'m [wasmparser::ValType], u32)>,
    callable_functions: Box<[bool]>,
    /// Repalce this function index with a return instruction
    replace_return: Option<u32>,
}

impl<'m: 'a, 'a> Weaver<'m, 'a> {
    pub fn new(parsed: &'m ParsedModule<'a>) -> Self {
        let fn_lookup = FunctionLookup::new(&parsed);
        let callable_functions = vec![true; fn_lookup.count()].into_boxed_slice();
        Self {
            ty_lookup: TypeLookup::new(&parsed.types),
            fn_lookup,
            returns_lookup: HashMap::new(),
            callable_functions,
            type_map: HashMap::new(),
            fn_map: HashMap::new(),
            type_indices: HashMap::new(),
            type_section: TypeSection::new(),
            current_type_index: 0,
            current_fn_index: 0,
            replace_return: None,
            parsed,
        }
    }

    pub fn encode(mut self, module: &mut Module) -> Result<()> {
        let mut sections = Sections::new();

        // Skip type section, encoded on demand

        // Get and remove marker imports
        let mut fn_import_index: u32 = 0;
        for (index, import) in self.parsed.imports.iter().enumerate() {
            let index32: u32 = index.try_into().unwrap();
            let imports = sections.imports.get_or_insert_default();
            // Filter for namespace "__export_returns"
            if import.module != "__export_returns" {
                // re-encode import
                self.parse_import(imports, *import)?;
                if matches!(import.ty, wasmparser::TypeRef::Func(_)) {
                    self.fn_map.insert(fn_import_index, self.current_fn_index);
                    self.current_fn_index += 1;
                    fn_import_index += 1;
                }
                continue;
            }
            let wasmparser::TypeRef::Func(ty_idx) = import.ty else {
                return Err(WeaveError::ReturnsMarkerIsNotFunction(import.name.to_string()).into());
            };
            let func_ty = &self.ty_lookup.try_get(ty_idx)?.try_fn_ty()?;
            self.returns_lookup
                .insert(import.name, (func_ty.params(), index32));
            self.callable_functions[index] = false; // marker functions are stripped
            fn_import_index += 1;
        }

        debug_assert_eq!(fn_import_index, self.fn_lookup.index_of_body(0));

        // Modify function types
        for (i, func_ty_idx) in self.parsed.functions.iter().enumerate() {
            let functions = sections.functions.get_or_insert_default();
            let func = self
                .fn_lookup
                .try_get(self.fn_lookup.index_of_body(i as u32))?;
            let mut type_index = None;
            'new_returns: {
                let Some(name) = func.export_name() else {
                    break 'new_returns;
                };
                let Some(&(results, _)) = self.returns_lookup.get(name) else {
                    break 'new_returns;
                };
                let params = self.ty_lookup.try_get(func.ty())?.try_fn_ty()?.params();
                type_index = Some(self.new_parser_fn_ty(params, results)?);
            }
            functions.function(match type_index {
                Some(index) => index,
                None => self.type_index(*func_ty_idx)?,
            });
            self.fn_map
                .insert(fn_import_index + i as u32, self.current_fn_index);
            self.current_fn_index += 1;
        }

        // Weave functions
        for (i, func_body) in self.parsed.code.iter().enumerate() {
            let code = sections.code.get_or_insert_default();
            let func = self
                .fn_lookup
                .try_get(self.fn_lookup.index_of_body(i as u32))?;
            'new_returns: {
                let Some(name) = func.export_name() else {
                    break 'new_returns;
                };
                let Some(&(_, replace_return)) = self.returns_lookup.get(name) else {
                    break 'new_returns;
                };

                // Weave function body
                self.replace_return = Some(replace_return);
            }

            self.parse_function_body(code, func_body.clone())?;
            self.replace_return = None;
        }

        for export in &self.parsed.exports {
            self.parse_export(sections.exports.get_or_insert_default(), *export)?;

            if export.kind != wasmparser::ExternalKind::Func {
                continue;
            }
        }

        for table in &self.parsed.tables {
            // Can have initializer constexpr
            self.parse_table(sections.tables.get_or_insert_default(), table.clone())?;
        }

        for memory in &self.parsed.memories {
            sections
                .memories
                .get_or_insert_default()
                .memory(self.memory_type(*memory)?);
        }

        for tag in &self.parsed.tags {
            sections
                .tags
                .get_or_insert_default()
                .tag(self.tag_type(*tag)?);
        }

        for global in &self.parsed.globals {
            // Can have initializer constexpr
            self.parse_global(sections.globals.get_or_insert_default(), global.clone())?;
        }

        sections.start = self
            .parsed
            .start
            .map(|function_index| StartSection { function_index });

        for elem in &self.parsed.elements {
            self.parse_element(sections.elements.get_or_insert_default(), elem.clone())?;
        }

        sections.data_count = self.parsed.start.map(|count| DataCountSection { count });

        for datum in &self.parsed.data {
            self.parse_data(sections.data.get_or_insert_default(), datum.clone())?;
        }

        sections.encode(module, self.type_section)
    }

    fn new_parser_fn_ty(
        &mut self,
        params: &[wasmparser::ValType],
        results: &[wasmparser::ValType],
    ) -> Result<u32> {
        let mut params_results = Vec::with_capacity(params.len() + results.len());

        for ty in params.iter().chain(results) {
            params_results.push(self.val_type(*ty)?);
        }

        let (params, results) = params_results.split_at(params.len());

        let sub_type = SubType {
            is_final: true,
            supertype_idx: None,
            composite_type: CompositeType {
                inner: CompositeInnerType::Func(FuncType::new(
                    params.iter().cloned(),
                    results.iter().cloned(),
                )),
                shared: false,
            },
        };

        Ok(self.new_ty(Cow::Owned(sub_type)))
    }

    /// Gets or creates a type index
    fn new_ty(&mut self, sub_type: Cow<SubType>) -> u32 {
        if let Some(idx) = self.type_indices.get(sub_type.as_ref().as_ref()) {
            return *idx;
        }
        let ty_idx = self.current_type_index;
        self.current_type_index += 1;

        self.type_section.ty().subtype(&sub_type);
        self.type_indices
            .insert(sub_type.into_owned().into(), ty_idx);

        return ty_idx;
    }
}

impl<'m: 'a, 'a> Reencode for Weaver<'m, 'a> {
    type Error = WeaveError;

    /// Dynamically re-encodes a type on demand
    fn type_index(&mut self, ty: u32) -> Result<u32> {
        if let Some(new_idx) = self.type_map.get(&ty) {
            return Ok(*new_idx);
        }
        let entry = self.ty_lookup.try_get(ty)?;

        // Base index for the newly re-encoded type(s)
        let base_index: u32 = self.current_type_index;

        if entry.group.is_explicit_rec_group() {
            let group_size: u32 = entry.group.types().len().try_into().unwrap();
            self.current_type_index += group_size;
            for i in 0u32..group_size {
                self.type_map.insert(entry.group_base + i, base_index + i);
            }
            let mut sub_types = Vec::with_capacity(entry.group.types().len());
            for sub_type in entry.group.types() {
                sub_types.push(self.sub_type(sub_type.clone())?);
            }
            for (i, sub_type) in sub_types.iter().enumerate() {
                let i: u32 = i.try_into().unwrap();
                self.type_indices
                    .insert(sub_type.clone().into(), base_index + i);
            }
            self.type_section.ty().rec(sub_types);
            Ok(base_index + entry.group_offset)
        } else {
            self.current_type_index += 1;
            self.type_map.insert(ty, base_index);
            let sub_type = self.sub_type(entry.sub_type.clone())?;
            self.type_section.ty().subtype(&sub_type);
            self.type_indices.insert(sub_type.into(), base_index);
            Ok(base_index)
        }
    }

    fn instruction<'o>(
        &mut self,
        arg: wasmparser::Operator<'o>,
    ) -> Result<wasm_encoder::Instruction<'o>> {
        if let Some(func) = self.replace_return {
            match arg {
                wasmparser::Operator::Call { function_index } if function_index == func => {
                    // Replace call with return
                    return Ok(Instruction::Return);
                }
                _ => (),
            }
        }
        utils::instruction(self, arg)
    }

    /// Prevents calls to marker functions
    fn function_index(&mut self, func: u32) -> Result<u32> {
        let index: usize = func.try_into().unwrap();
        match self.callable_functions.get(index) {
            Some(true) => match self.fn_map.get(&func) {
                Some(idx) => Ok(*idx),
                None => Err(WeaveError::FunctionIndexOutOfBounds(func).into()),
            },
            Some(false) => Err(WeaveError::UnexpectedMarkerFunctionCall(None).into()),
            None => Err(WeaveError::FunctionIndexOutOfBounds(func).into()),
        }
    }

    /// Weaver needs to know the whole module ahead of time,
    /// a parser is not sufficient to reencode.
    fn parse_core_module(
        &mut self,
        _module: &mut wasm_encoder::Module,
        _parser: wasmparser::Parser,
        _data: &[u8],
    ) -> Result<()> {
        panic!("not supported")
    }

    fn parse_type_section(
        &mut self,
        _types: &mut wasm_encoder::TypeSection,
        _section: wasmparser::TypeSectionReader<'_>,
    ) -> Result<()> {
        panic!("not supported")
    }

    fn parse_import_section(
        &mut self,
        _imports: &mut wasm_encoder::ImportSection,
        _section: wasmparser::ImportSectionReader<'_>,
    ) -> Result<()> {
        panic!("not supported")
    }

    fn parse_function_section(
        &mut self,
        _functions: &mut wasm_encoder::FunctionSection,
        _section: wasmparser::FunctionSectionReader<'_>,
    ) -> Result<()> {
        panic!("not supported")
    }

    fn parse_table_section(
        &mut self,
        _tables: &mut wasm_encoder::TableSection,
        _section: wasmparser::TableSectionReader<'_>,
    ) -> Result<()> {
        panic!("not supported")
    }

    fn parse_memory_section(
        &mut self,
        _memories: &mut wasm_encoder::MemorySection,
        _section: wasmparser::MemorySectionReader<'_>,
    ) -> Result<()> {
        panic!("not supported")
    }

    fn parse_tag_section(
        &mut self,
        _tags: &mut wasm_encoder::TagSection,
        _section: wasmparser::TagSectionReader<'_>,
    ) -> Result<()> {
        panic!("not supported")
    }

    fn parse_global_section(
        &mut self,
        _globals: &mut wasm_encoder::GlobalSection,
        _section: wasmparser::GlobalSectionReader<'_>,
    ) -> Result<()> {
        panic!("not supported")
    }

    fn parse_export_section(
        &mut self,
        _exports: &mut wasm_encoder::ExportSection,
        _section: wasmparser::ExportSectionReader<'_>,
    ) -> Result<()> {
        panic!("not supported")
    }
}
