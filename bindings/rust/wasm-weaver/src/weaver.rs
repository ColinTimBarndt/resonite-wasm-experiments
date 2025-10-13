mod indices;
mod table_slab;

use std::{borrow::Cow, collections::HashMap, iter::repeat_n};

use wasm_encoder::{
    reencode::{Reencode, utils},
    *,
};

use crate::{
    parse::{FunctionLookup, ParsedModule, SignatureKey, TypeLookup, ValueTypeMeta},
    type_allocator::HashableType,
    weaver::{indices::Indices, table_slab::TableSlabApi},
};

#[derive(Default)]
pub struct Sections {
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
    #[error("table index out of bounds: {0}")]
    TableIndexOutOfBounds(u32),
    #[error("global index out of bounds: {0}")]
    GlobalIndexOutOfBounds(u32),
    #[error("function type is not function: {0}")]
    FunctionTypeIsNotFunction(u32),
    #[error("returns marker is not of type function: {0}")]
    ReturnsMarkerIsNotFunction(String),
    #[error("import not found: '{0}'")]
    ImportNotFound(String),
    #[error("unexpected marker function call at function {0:?}")]
    UnexpectedMarkerFunctionCall(Option<u32>),
    #[error("unknown meta '{0:?}'")]
    UnknownMeta(ValueTypeMeta),
    #[error("meta '{0:?}' cannot be applied to parameter type '{1:?}'")]
    IncompatibleMetaType(ValueTypeMeta, wasmparser::ValType),
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
    type_indices: HashMap<HashableType, u32>,
    type_section: TypeSection,
    indices: Indices,
    ty_lookup: TypeLookup<'a>,
    fn_lookup: FunctionLookup<'m, 'a>,
    returns_lookup: HashMap<&'a str, (&'m [wasmparser::ValType], u32)>,
    /// Lookup table for callable functions from the source module.
    /// A function is not callable if it's a marker that will be removed
    /// by this weaver.
    callable_functions: Box<[bool]>,
    /// Repalce this function index with a return instruction
    replace_return: Option<u32>,
    block_depth: u32,
    current_function_meta: Option<FunctionMeta<'a>>,
    locals_map: HashMap<u32, u32>,
    locals_count: u32,
    table_api: Option<TableSlabApi>,
}

#[derive(Debug, Clone, Copy)]
struct FunctionMeta<'a> {
    signature_meta: &'a [ValueTypeMeta],
    parameters: &'a [wasmparser::ValType],
    results: &'a [wasmparser::ValType],
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
            type_indices: HashMap::new(),
            type_section: TypeSection::new(),
            indices: Indices::default(),
            replace_return: None,
            block_depth: 0,
            current_function_meta: None,
            locals_map: HashMap::new(),
            locals_count: 0,
            table_api: None,
            parsed,
        }
    }

    pub fn encode(mut self, module: &mut Module) -> Result<()> {
        let mut sections = Sections::new();

        let mut needs_table_api = self.parsed.signatures.values().any(|sig| {
            sig.iter().any(|meta| {
                matches!(
                    *meta,
                    ValueTypeMeta::EXTERNREF_OWNED | ValueTypeMeta::EXTERNREF_REF
                )
            })
        });

        // Skip type section, encoded on demand

        // Get and remove marker imports
        let mut fn_import_index: u32 = 0;
        let mut global_import_index: u32 = 0;
        let mut table_import_index: u32 = 0;
        let mut deferred_imports = Vec::new();
        for (index, import) in self.parsed.imports.iter().enumerate() {
            let index32: u32 = index.try_into().unwrap();
            let imports = sections.imports.get_or_insert_default();
            // Filter for namespace "__export_returns"
            let do_reserve = match import.module {
                "__export_returns" => {
                    let wasmparser::TypeRef::Func(ty_idx) = import.ty else {
                        return Err(WeaveError::ReturnsMarkerIsNotFunction(
                            import.name.to_string(),
                        )
                        .into());
                    };
                    let func_ty = &self.ty_lookup.try_get(ty_idx)?.try_fn_ty()?;
                    self.returns_lookup
                        .insert(import.name, (func_ty.params(), index32));
                    self.callable_functions[index] = false; // marker functions are stripped
                    fn_import_index += 1;
                    continue;
                }
                "__table" => {
                    deferred_imports.push((index as u32, import));
                    needs_table_api = true;
                    false
                }
                _ => {
                    // re-encode import
                    self.parse_import(imports, *import)?;
                    true
                }
            };
            if do_reserve {
                match import.ty {
                    wasmparser::TypeRef::Func(_) => {
                        self.indices.funcs.map_reserve(fn_import_index);
                    }
                    wasmparser::TypeRef::Global(_) => {
                        self.indices.globals.map_reserve(global_import_index);
                    }
                    wasmparser::TypeRef::Table(_) => {
                        self.indices.tables.map_reserve(table_import_index);
                    }
                    _ => (),
                }
            }
            match import.ty {
                wasmparser::TypeRef::Func(_) => {
                    fn_import_index += 1;
                }
                wasmparser::TypeRef::Global(_) => {
                    global_import_index += 1;
                }
                wasmparser::TypeRef::Table(_) => {
                    table_import_index += 1;
                }
                _ => (),
            }
        }

        debug_assert_eq!(fn_import_index, self.fn_lookup.index_of_body(0));

        if needs_table_api {
            self.table_api = Some(TableSlabApi::include(&mut self, &mut sections)?);
        }

        for (i, import) in deferred_imports {
            match import.module {
                "__table" => {
                    self.table_api.as_ref().unwrap().map_import(
                        &mut self.indices,
                        i,
                        import.ty,
                        import.name,
                    )?;
                }
                _ => continue,
            }
        }

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
                let mut combined: Vec<_> = params.iter().chain(results).copied().collect();

                if let Some(&meta) = self.parsed.signatures.get(&SignatureKey::Export(name)) {
                    for (ty, meta) in combined.iter_mut().zip(meta) {
                        *ty = match *meta {
                            ValueTypeMeta::EXTERNREF_OWNED | ValueTypeMeta::EXTERNREF_REF => {
                                wasmparser::ValType::EXTERNREF
                            }
                            ValueTypeMeta::NONE | _ => continue,
                        };
                    }
                }

                let (params, results) = combined.split_at(params.len());
                type_index = Some(self.new_parser_fn_ty(params, results)?);
            }
            functions.function(match type_index {
                Some(index) => index,
                None => self.type_index(*func_ty_idx)?,
            });
            self.indices.funcs.map_reserve(fn_import_index + i as u32);
        }

        for (i, table) in self.parsed.tables.iter().enumerate() {
            // Can have initializer constexpr
            self.indices
                .tables
                .map_reserve(table_import_index + i as u32);
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

        for (i, global) in self.parsed.globals.iter().enumerate() {
            // Can have initializer constexpr
            self.indices
                .globals
                .map_reserve(global_import_index + i as u32);
            self.parse_global(sections.globals.get_or_insert_default(), global.clone())?;
        }

        // Weave functions
        for (i, func_body) in self.parsed.code.iter().enumerate() {
            let code = sections.code.get_or_insert_default();
            let func = self
                .fn_lookup
                .try_get(self.fn_lookup.index_of_body(i as u32))?;

            if let Some(name) = func.export_name() {
                let mut results = None;
                if let Some(&(new_results, replace_return)) = self.returns_lookup.get(name) {
                    self.replace_return = Some(replace_return);
                    results = Some(new_results);
                }
                if let Some(&meta) = self.parsed.signatures.get(&SignatureKey::Export(name)) {
                    if meta.iter().any(|meta| *meta != ValueTypeMeta::NONE) {
                        // Needs processing
                        let func_ty = self.ty_lookup.try_get(func.ty())?.try_fn_ty()?;
                        self.current_function_meta = Some(FunctionMeta {
                            signature_meta: meta,
                            parameters: func_ty.params(),
                            results: results.unwrap_or(func_ty.results()),
                        });
                    }
                }
            }

            self.parse_function_body(code, func_body.clone())?;
            self.replace_return = None;
            self.current_function_meta = None;
        }

        for export in &self.parsed.exports {
            self.parse_export(sections.exports.get_or_insert_default(), *export)?;
        }

        if let Some(function_index) = self.parsed.start {
            sections.start = Some(StartSection {
                function_index: self
                    .indices
                    .funcs
                    .map(function_index)
                    .ok_or_else(|| WeaveError::FunctionIndexOutOfBounds(function_index))?,
            });
        }

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

        Ok(self.new_fn_ty(params, results))
    }

    fn new_fn_ty(&mut self, params: &[ValType], results: &[ValType]) -> u32 {
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

        self.new_ty(Cow::Owned(sub_type))
    }

    fn new_parser_ty(&mut self, sub_type: &wasmparser::SubType) -> Result<u32> {
        let idx = self.indices.types.reserve();
        let sub_type = self.sub_type(sub_type.clone())?;
        self.type_section.ty().subtype(&sub_type);
        self.type_indices.insert(sub_type.into(), idx);
        Ok(idx)
    }

    /// Gets or creates a type index
    fn new_ty(&mut self, sub_type: Cow<SubType>) -> u32 {
        if let Some(idx) = self.type_indices.get(sub_type.as_ref().as_ref()) {
            return *idx;
        }
        let ty_idx = self.indices.types.reserve();

        self.type_section.ty().subtype(&sub_type);
        self.type_indices
            .insert(sub_type.into_owned().into(), ty_idx);

        return ty_idx;
    }

    fn new_parser_rec_group(&mut self, rec_group: &wasmparser::RecGroup) -> Result<u32> {
        if rec_group.is_explicit_rec_group() {
            let group_size: u32 = rec_group.types().len().try_into().unwrap();
            let mut sub_types = Vec::with_capacity(rec_group.types().len());
            // re-encode dependencies
            for sub_type in rec_group.types() {
                sub_types.push(self.sub_type(sub_type.clone())?);
            }
            // reserve & encode
            let base_index = self.indices.types.reserve_many(group_size);
            for (i, sub_type) in sub_types.iter().enumerate() {
                let i: u32 = i.try_into().unwrap();
                self.type_indices
                    .insert(sub_type.clone().into(), base_index + i);
            }
            self.type_section.ty().rec(sub_types);
            Ok(base_index)
        } else {
            let sub_type = rec_group.types().next().unwrap();
            self.new_parser_ty(sub_type)
        }
    }

    fn local_index(&mut self, index: u32) -> u32 {
        self.locals_map.get(&index).copied().unwrap_or(index)
    }
}

impl<'m: 'a, 'a> Reencode for Weaver<'m, 'a> {
    type Error = WeaveError;

    /// Dynamically re-encodes a type on demand
    fn type_index(&mut self, ty: u32) -> Result<u32> {
        if let Some(new_idx) = self.indices.types.map(ty) {
            return Ok(new_idx);
        }
        let entry = self.ty_lookup.try_get(ty)?;

        // Base index for the newly re-encoded type(s)
        let base_index = self.new_parser_rec_group(entry.group)?;
        for i in 0u32..entry.group.types().len() as u32 {
            self.indices
                .types
                .add_mapping(entry.group_base + i, base_index + i);
        }

        Ok(base_index + entry.group_offset)
    }

    fn instruction<'o>(
        &mut self,
        arg: wasmparser::Operator<'o>,
    ) -> Result<wasm_encoder::Instruction<'o>> {
        if let Some(func) = self.replace_return {
            match arg {
                wasmparser::Operator::Call { function_index } if function_index == func => {
                    // Replace call with return
                    return if self.current_function_meta.is_some() {
                        Ok(Instruction::Br(self.block_depth))
                    } else {
                        Ok(Instruction::Return)
                    };
                }
                _ => (),
            }
        }
        match arg {
            wasmparser::Operator::End => {
                self.block_depth = self.block_depth.saturating_sub(1);
            }
            wasmparser::Operator::LocalGet { local_index } => {
                return Ok(Instruction::LocalGet(self.local_index(local_index)));
            }
            wasmparser::Operator::LocalSet { local_index } => {
                return Ok(Instruction::LocalSet(self.local_index(local_index)));
            }
            wasmparser::Operator::LocalTee { local_index } => {
                return Ok(Instruction::LocalTee(self.local_index(local_index)));
            }
            _ => (),
        }
        utils::instruction(self, arg)
    }

    /// Prevents calls to marker functions
    fn function_index(&mut self, func: u32) -> Result<u32> {
        let index: usize = func.try_into().unwrap();
        match self.callable_functions.get(index) {
            Some(true) => match self.indices.funcs.map(func) {
                Some(idx) => Ok(idx),
                None => Err(WeaveError::FunctionIndexOutOfBounds(func).into()),
            },
            Some(false) => Err(WeaveError::UnexpectedMarkerFunctionCall(None).into()),
            None => Err(WeaveError::FunctionIndexOutOfBounds(func).into()),
        }
    }

    fn table_index(&mut self, table: u32) -> Result<u32> {
        self.indices
            .tables
            .map(table)
            .ok_or_else(|| WeaveError::TableIndexOutOfBounds(table).into())
    }

    fn global_index(&mut self, global: u32) -> Result<u32> {
        self.indices
            .globals
            .map(global)
            .ok_or_else(|| WeaveError::GlobalIndexOutOfBounds(global).into())
    }

    fn new_function_with_parsed_locals(
        &mut self,
        func: &wasmparser::FunctionBody<'_>,
    ) -> Result<Function> {
        let Some(meta) = self.current_function_meta else {
            return utils::new_function_with_parsed_locals(self, func);
        };

        // Remap locals
        self.locals_map.clear();
        let mut locals = Vec::new();
        for pair in func.get_locals_reader()? {
            let (cnt, ty) = pair?;
            locals.extend(repeat_n(self.val_type(ty)?, cnt as usize));
        }
        for (i, (param_ty, param_meta)) in
            meta.parameters.iter().zip(meta.signature_meta).enumerate()
        {
            match *param_meta {
                ValueTypeMeta::NONE => continue,
                ValueTypeMeta::EXTERNREF_OWNED => {
                    // Owned externref to i32
                    if !matches!(param_ty, wasmparser::ValType::I32) {
                        return Err(WeaveError::IncompatibleMetaType(*param_meta, *param_ty).into());
                    }
                    let remapped = meta.parameters.len() + locals.len();
                    self.locals_map.insert(i as u32, remapped as u32);
                    locals.push(ValType::I32);
                }
                _ => return Err(WeaveError::UnknownMeta(*param_meta).into()),
            }
        }
        // Add enough locals to store all results
        for result_ty in meta.results {
            locals.push(self.val_type(*result_ty)?);
        }
        self.locals_count = locals.len() as u32;
        Ok(Function::new(locals.into_iter().map(|ty| (1, ty))))
    }

    fn parse_function_body(
        &mut self,
        code: &mut wasm_encoder::CodeSection,
        func: wasmparser::FunctionBody<'_>,
    ) -> Result<()> {
        let Some(meta) = self.current_function_meta else {
            return utils::parse_function_body(self, code, func);
        };

        self.block_depth = 0;

        let mut f = self.new_function_with_parsed_locals(&func)?;

        // == Function prologue ==

        // process parameters
        for (i, param_meta) in meta
            .signature_meta
            .iter()
            .take(meta.parameters.len())
            .enumerate()
        {
            let i = i as u32;
            match *param_meta {
                ValueTypeMeta::EXTERNREF_OWNED => {
                    f.instruction(&Instruction::LocalGet(i));
                    f.instruction(&self.table_api.as_ref().unwrap().alloc_extern());
                    f.instruction(&Instruction::LocalSet(self.locals_map[&i]));
                }
                _ => continue,
            }
        }

        // == Function body ==
        let mut reader = func.get_operators_reader()?;
        // Wrap function body in a block
        f.instruction(&Instruction::Block(BlockType::FunctionType(
            self.new_parser_fn_ty(&[], meta.results)?,
        )));
        while !reader.is_end_then_eof() {
            f.instruction(&self.parse_instruction(&mut reader)?);
        }
        f.instruction(&Instruction::End);

        // == Function epilogue ==

        // Save all results to locals (popping stack in reverse)
        let locals_offset: u32 =
            meta.parameters.len() as u32 + self.locals_count - meta.results.len() as u32;
        for i in (0..meta.results.len()).rev() {
            f.instruction(&Instruction::LocalSet(locals_offset + i as u32));
        }

        // process results
        for (i, result_ty) in meta.results.iter().enumerate() {
            f.instruction(&Instruction::LocalGet(locals_offset + i as u32));
            if let Some(result_meta) = meta.signature_meta.get(meta.parameters.len() + i) {
                match *result_meta {
                    ValueTypeMeta::NONE => continue,
                    ValueTypeMeta::EXTERNREF_OWNED => {
                        if !matches!(result_ty, wasmparser::ValType::I32) {
                            return Err(
                                WeaveError::IncompatibleMetaType(*result_meta, *result_ty).into()
                            );
                        }
                        f.instruction(&self.table_api.as_ref().unwrap().take_extern());
                    }
                    ValueTypeMeta::EXTERNREF_REF => {
                        for instr in self.table_api.as_ref().unwrap().get_extern() {
                            f.instruction(&instr);
                        }
                    }
                    _ => return Err(WeaveError::UnknownMeta(*result_meta).into()),
                }
            }
        }

        f.instruction(&Instruction::End);
        code.function(&f);
        Ok(())
    }

    fn block_type(&mut self, arg: wasmparser::BlockType) -> Result<BlockType> {
        self.block_depth += 1;
        utils::block_type(self, arg)
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
