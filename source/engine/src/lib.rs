//! Bibliothèque partagée du moteur OPTIVANCE.
//!
//! Le binaire `optivance-engine` (scan classique) reste dans `main.rs`. Le module `mft`
//! contient le backend MFT **expérimental** : décodage des enregistrements renvoyés par
//! `FSCTL_ENUM_USN_DATA` et reconstruction de l'index. Il ne fait aucun appel système.

pub mod clean;
pub mod fasthash;
pub mod fps;
pub mod mft;
pub mod protocol;
pub mod tune;
