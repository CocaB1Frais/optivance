//! Hachage rapide pour les clés entières (références de fichiers, numéros d'enregistrement).
//!
//! Le hachage par défaut de la bibliothèque standard (SipHash) protège contre des clés
//! choisies par un attaquant ; ici les clés viennent du système de fichiers local et le
//! volume de données (centaines de milliers d'entrées) rend ce coût mesurable. Algorithme
//! « Fx » (multiplication-rotation), sans dépendance.

use std::collections::{HashMap, HashSet};
use std::hash::{BuildHasherDefault, Hasher};

#[derive(Default, Clone, Copy)]
pub struct FxHasher(u64);

const SEED: u64 = 0x51_7c_c1_b7_27_22_0a_95;

impl FxHasher {
    #[inline]
    fn add(&mut self, word: u64) {
        self.0 = (self.0.rotate_left(5) ^ word).wrapping_mul(SEED);
    }
}

impl Hasher for FxHasher {
    #[inline]
    fn write(&mut self, bytes: &[u8]) {
        for chunk in bytes.chunks(8) {
            let mut word = [0u8; 8];
            word[..chunk.len()].copy_from_slice(chunk);
            self.add(u64::from_le_bytes(word));
        }
    }

    #[inline]
    fn write_u64(&mut self, value: u64) {
        self.add(value);
    }

    #[inline]
    fn write_u128(&mut self, value: u128) {
        self.add(value as u64);
        self.add((value >> 64) as u64);
    }

    #[inline]
    fn write_usize(&mut self, value: usize) {
        self.add(value as u64);
    }

    #[inline]
    fn finish(&self) -> u64 {
        self.0
    }
}

pub type FastMap<K, V> = HashMap<K, V, BuildHasherDefault<FxHasher>>;
pub type FastSet<K> = HashSet<K, BuildHasherDefault<FxHasher>>;

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn maps_distinct_keys_and_finds_them_again() {
        let mut map: FastMap<u128, u64> = FastMap::default();
        for key in 0..10_000u128 {
            map.insert((key << 48) | key, key as u64);
        }
        assert_eq!(map.len(), 10_000);
        assert_eq!(map.get(&((7u128 << 48) | 7)), Some(&7));
        assert!(!map.contains_key(&3));
    }
}
