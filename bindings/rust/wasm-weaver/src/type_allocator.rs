use std::hash::Hash;

use wasm_encoder::*;

#[repr(transparent)]
#[derive(Debug)]
pub struct HashableType(SubType);

impl AsRef<HashableType> for SubType {
    fn as_ref(&self) -> &HashableType {
        // SAFETY: repr(transparent) guarantees same layout
        unsafe {
            (self as *const SubType)
                .cast::<HashableType>()
                .as_ref()
                .unwrap()
        }
    }
}

impl Hash for HashableType {
    fn hash<H: std::hash::Hasher>(&self, state: &mut H) {
        let sub_type = &self.0;
        let comp = &sub_type.composite_type;
        state.write_u8(
            sub_type.is_final as u8
                | (sub_type.supertype_idx.is_some() as (u8) << 1)
                | (comp.shared as (u8) << 2),
        );
        state.write_u32(sub_type.supertype_idx.unwrap_or_default());
        let inner = &comp.inner;
        match inner {
            CompositeInnerType::Func(func_type) => func_type.hash(state),
            CompositeInnerType::Array(array_type) => array_type.hash(state),
            CompositeInnerType::Struct(struct_type) => struct_type.hash(state),
            CompositeInnerType::Cont(cont_type) => cont_type.hash(state),
        }
    }
}

impl Eq for HashableType {}

impl PartialEq for HashableType {
    fn eq(&self, other: &Self) -> bool {
        let sub_type_a = &self.0;
        let sub_type_b = &other.0;
        let comp_a = &sub_type_a.composite_type;
        let comp_b = &sub_type_b.composite_type;
        if sub_type_a.is_final != sub_type_b.is_final
            || sub_type_a.supertype_idx != sub_type_b.supertype_idx
            || comp_a.shared != comp_b.shared
        {
            return false;
        }
        let inner_a = &comp_a.inner;
        let inner_b = &comp_b.inner;
        match (inner_a, inner_b) {
            (CompositeInnerType::Func(a), CompositeInnerType::Func(b)) => a == b,
            (CompositeInnerType::Array(a), CompositeInnerType::Array(b)) => a == b,
            (CompositeInnerType::Struct(a), CompositeInnerType::Struct(b)) => a == b,
            (CompositeInnerType::Cont(a), CompositeInnerType::Cont(b)) => a == b,
            _ => false,
        }
    }
}

impl From<SubType> for HashableType {
    fn from(value: SubType) -> Self {
        Self(value)
    }
}
