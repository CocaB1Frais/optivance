//! Backend MFT expérimental (non validé sur volume réel).
//!
//! - `records` : décodage strict des buffers de sortie de `FSCTL_ENUM_USN_DATA`
//!   (énumération MFT, à ne pas confondre avec la lecture du journal USN).
//! - `index` : reconstruction de l'arborescence au même format plat que le scan classique.

pub mod args;
pub mod assemble;
pub mod enumerate;
pub mod index;
pub mod links;
pub mod metadata;
pub mod raw;
pub mod records;
pub mod volume;
