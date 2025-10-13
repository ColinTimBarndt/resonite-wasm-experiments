use std::{collections::HashMap, fmt::Debug, iter::repeat};

use wasmparser::*;

use crate::weaver::WeaveError;

#[derive(Default)]
pub struct ParsedModule<'a> {
    pub types: Box<[RecGroup]>,
    pub imports: Box<[Import<'a>]>,
    pub functions: Box<[u32]>,
    pub tables: Box<[Table<'a>]>,
    pub memories: Box<[MemoryType]>,
    pub tags: Box<[TagType]>,
    pub globals: Box<[Global<'a>]>,
    pub exports: Box<[Export<'a>]>,
    pub start: Option<u32>,
    pub elements: Box<[Element<'a>]>,
    pub data_count: Option<u32>,
    pub data: Box<[Data<'a>]>,
    pub code: Box<[FunctionBody<'a>]>,
    pub signatures: HashMap<SignatureKey<'a>, &'a [ValueTypeMeta]>,
}

#[derive(Debug, Hash, PartialEq, Eq)]
pub enum SignatureKey<'a> {
    Export(&'a str),
    Import(&'a str),
}

#[derive(PartialEq, Eq, Clone, Copy)]
#[repr(transparent)]
pub struct ValueTypeMeta {
    pub tag: [u8; 4],
}

impl ValueTypeMeta {
    pub const NONE: Self = Self { tag: [0; 4] };
    pub const EXTERNREF_OWNED: Self = Self { tag: *b"EXRo" };
    pub const EXTERNREF_REF: Self = Self { tag: *b"EXRr" };

    pub const fn cast_from_slice(slice: &[[u8; 4]]) -> &[Self] {
        // SAFETY: ValueTypeMeta is repr(transparent) of [u8; 4]
        unsafe { std::mem::transmute(slice) }
    }
}

impl Debug for ValueTypeMeta {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        if *self == Self::NONE {
            return f.write_str("None");
        }
        match std::str::from_utf8(&self.tag) {
            Ok(s) => Debug::fmt(s, f),
            Err(_) => Debug::fmt(&self.tag, f),
        }
    }
}

impl<'a> ParsedModule<'a> {
    pub fn read(parser: Parser, data: &'a [u8]) -> Result<Self, BinaryReaderError> {
        let mut module = Self::default();

        let mut functions = Vec::new();

        for payload in parser.parse_all(data) {
            use Payload::*;
            match payload? {
                Version { .. } => continue,
                TypeSection(section) => {
                    module.types = collect_section(section)?;
                }
                ImportSection(section) => {
                    module.imports = collect_section(section)?;
                }
                FunctionSection(section) => {
                    module.functions = collect_section(section)?;
                }
                TableSection(section) => {
                    module.tables = collect_section(section)?;
                }
                MemorySection(section) => {
                    module.memories = collect_section(section)?;
                }
                TagSection(section) => {
                    module.tags = collect_section(section)?;
                }
                GlobalSection(section) => {
                    module.globals = collect_section(section)?;
                }
                ExportSection(section) => {
                    module.exports = collect_section(section)?;
                }
                StartSection { func, .. } => {
                    module.start = Some(func);
                }
                ElementSection(section) => {
                    module.elements = collect_section(section)?;
                }
                DataCountSection { count, .. } => {
                    module.data_count = Some(count);
                }
                DataSection(section) => {
                    module.data = collect_section(section)?;
                }
                CodeSectionStart { count, .. } => {
                    functions.reserve_exact(count.try_into().unwrap());
                }
                CodeSectionEntry(function_body) => {
                    if functions.capacity() == 0 {
                        continue;
                    }
                    functions.push(function_body);
                    if functions.len() == functions.capacity() {
                        module.code = std::mem::take(&mut functions).into_boxed_slice();
                    }
                }
                ModuleSection {
                    parser: _,
                    unchecked_range: _,
                } => unimplemented!("Nested Module"),
                // Components
                InstanceSection(_section_limited) => unimplemented!("Component Model"),
                CoreTypeSection(_section_limited) => unimplemented!("Component Model"),
                ComponentSection {
                    parser: _,
                    unchecked_range: _,
                } => unimplemented!("Component Model"),
                ComponentInstanceSection(_section) => unimplemented!("Component Model"),
                ComponentAliasSection(_section) => unimplemented!("Component Model"),
                ComponentTypeSection(_section) => unimplemented!("Component Model"),
                ComponentCanonicalSection(_section) => unimplemented!("Component Model"),
                ComponentStartSection { .. } => unimplemented!("Component Model"),
                ComponentImportSection(_section) => unimplemented!("Component Model"),
                ComponentExportSection(_section) => unimplemented!("Component Model"),
                CustomSection(section) => {
                    let Some(name) = section.name().strip_prefix("__signature.") else {
                        continue;
                    };
                    let Some((typ, name)) = name.split_once('.') else {
                        eprintln!("Invalid signature section '{}'", section.name());
                        continue;
                    };
                    let key = match typ {
                        "import" => SignatureKey::Import(name),
                        "export" => SignatureKey::Export(name),
                        _ => {
                            eprintln!(
                                "Unknown signature type '{typ}' in section '{}'",
                                section.name()
                            );
                            continue;
                        }
                    };
                    let (metas, []) = section.data().as_chunks::<4>() else {
                        eprintln!("Section '{}' data is not a multiple of 4", section.name());
                        continue;
                    };
                    module
                        .signatures
                        .insert(key, ValueTypeMeta::cast_from_slice(metas));
                }
                UnknownSection { .. } => unimplemented!("Unknown Section"),
                End(_) => break,
                pl => unimplemented!("{pl:?}"),
            }
        }

        Ok(module)
    }
}

fn collect_section<'a, T>(section: SectionLimited<'a, T>) -> Result<Box<[T]>, BinaryReaderError>
where
    T: FromReader<'a>,
{
    let mut items = Vec::with_capacity(section.count().try_into().unwrap());
    for item in section {
        items.push(item?);
    }
    Ok(items.into_boxed_slice())
}

#[derive(Debug)]
pub struct TypeLookup<'a> {
    table: Box<[TypeLookupEntry<'a>]>,
}

#[derive(Debug, Clone, Copy)]
pub struct TypeLookupEntry<'a> {
    pub group: &'a RecGroup,
    /// The type index of the first [`SubType`] in this group.
    pub group_base: u32,
    pub group_offset: u32,
    pub sub_type: &'a SubType,
}

impl<'a> TypeLookupEntry<'a> {
    pub fn try_fn_ty(&self) -> Result<&'a FuncType, WeaveError> {
        match &self.sub_type.composite_type.inner {
            CompositeInnerType::Func(func) => Ok(func),
            _ => Err(WeaveError::FunctionTypeIsNotFunction(
                self.group_base + self.group_offset,
            )),
        }
    }
}

impl<'a> TypeLookup<'a> {
    pub fn new(groups: &'a [RecGroup]) -> Self {
        let size = groups.iter().map(|ty| ty.types().len()).sum();
        let mut table = Vec::with_capacity(size);

        let mut group_base: u32 = 0;
        table.extend(groups.iter().flat_map(|g| {
            let current_base = group_base;
            let group_size: u32 = g.types().len().try_into().unwrap();
            group_base += group_size;
            repeat((g, current_base)).zip(g.types().enumerate()).map(
                |((group, group_base), (group_offset, sub_type))| TypeLookupEntry {
                    group,
                    group_base,
                    group_offset: group_offset.try_into().unwrap(),
                    sub_type,
                },
            )
        }));

        Self {
            table: table.into_boxed_slice(),
        }
    }

    pub fn get(&self, index: u32) -> Option<TypeLookupEntry<'a>> {
        let index: usize = index.try_into().unwrap();
        self.table.get(index).copied()
    }

    pub fn try_get(&self, index: u32) -> Result<TypeLookupEntry<'a>, WeaveError> {
        self.get(index)
            .ok_or_else(|| WeaveError::TypeIndexOutOfBounds(index))
    }
}

#[derive(Debug)]
pub struct FunctionLookup<'m, 'a> {
    table: Vec<FunctionLookupEntry<'m, 'a>>,
    num_imports: u32,
}

#[derive(Debug, Clone, Copy)]
pub enum FunctionLookupEntry<'m, 'a> {
    Import {
        index: u32,
        ty: u32,
        import: &'m Import<'a>,
    },
    Body {
        index: u32,
        ty: u32,
        body: &'m FunctionBody<'a>,
        export_name: Option<&'a str>,
    },
}

impl<'m, 'a> FunctionLookupEntry<'m, 'a> {
    pub fn ty(&self) -> u32 {
        match self {
            Self::Import { ty, .. } | Self::Body { ty, .. } => *ty,
        }
    }

    pub fn export_name(&self) -> Option<&'a str> {
        match self {
            Self::Body { export_name, .. } => *export_name,
            _ => None,
        }
    }
}

impl<'m, 'a> FunctionLookup<'m, 'a> {
    pub fn new(module: &'m ParsedModule<'a>) -> Self {
        let mut num_imports = 0;
        let imports =
            module
                .imports
                .iter()
                .enumerate()
                .filter_map(|(index, import)| match import.ty {
                    TypeRef::Func(ty) => {
                        num_imports += 1;
                        Some(FunctionLookupEntry::Import {
                            index: index.try_into().unwrap(),
                            ty,
                            import,
                        })
                    }
                    _ => None,
                });
        let functions =
            module
                .code
                .iter()
                .enumerate()
                .map(|(index, code)| FunctionLookupEntry::Body {
                    index: index.try_into().unwrap(),
                    ty: module.functions[index],
                    body: code,
                    export_name: None,
                });
        let mut table = imports.chain(functions).collect::<Vec<_>>();

        for export in &module.exports {
            if export.kind == ExternalKind::Func {
                let index: usize = export.index.try_into().unwrap();
                match table.get_mut(index) {
                    Some(FunctionLookupEntry::Body { export_name, .. }) => {
                        *export_name = Some(export.name);
                    }
                    _ => (),
                }
            }
        }

        Self { table, num_imports }
    }

    pub fn count(&self) -> usize {
        self.table.len()
    }

    pub fn index_of_body(&self, body_index: u32) -> u32 {
        self.num_imports + body_index
    }

    pub fn get(&self, index: u32) -> Option<FunctionLookupEntry<'m, 'a>> {
        let index: usize = index.try_into().unwrap();
        self.table.get(index).cloned()
    }

    pub fn try_get(&self, index: u32) -> Result<FunctionLookupEntry<'m, 'a>, WeaveError> {
        self.get(index)
            .ok_or_else(|| WeaveError::FunctionIndexOutOfBounds(index))
    }
}
