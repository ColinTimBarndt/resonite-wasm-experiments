use std::collections::HashMap;

/// WebAssembly refers to things using indices. This struct
/// is used for allocating unique indices and mapping
/// from the source index (original module) to the new target index.
#[derive(Default)]
pub struct Indices {
    pub types: IndexMapper,
    pub funcs: IndexMapper,
    pub tables: IndexMapper,
    pub globals: IndexMapper,
}

/// Source for allocating unique indexes and mapping them from source to target.
#[derive(Default)]
pub struct IndexMapper {
    next_index: u32,
    map: HashMap<u32, u32>,
}

impl IndexMapper {
    pub fn reserve(&mut self) -> u32 {
        self.reserve_many(1)
    }

    pub fn reserve_many(&mut self, count: u32) -> u32 {
        let i = self.next_index;
        self.next_index += count;
        i
    }

    pub fn map_reserve(&mut self, source: u32) -> u32 {
        let i = self.reserve();
        self.add_mapping(source, i);
        i
    }

    pub fn add_mapping(&mut self, source: u32, target: u32) {
        assert!(target < self.next_index, "target {target} is not reserved");
        if self.map.insert(source, target).is_some() {
            panic!("registered mapping for index {source} twice");
        }
    }

    pub fn map(&self, source: u32) -> Option<u32> {
        self.map.get(&source).cloned()
    }
}
