//! Décodage des buffers de sortie de `FSCTL_ENUM_USN_DATA`.
//!
//! Format (Microsoft Learn, FSCTL_ENUM_USN_DATA / USN_RECORD_V2 / USN_RECORD_V3) :
//! les 8 premiers octets contiennent la référence de fichier à passer à l'appel suivant,
//! suivis d'enregistrements USN de longueur variable (`RecordLength`).
//!
//! Le décodage est strict (fail-closed) : toute longueur, version ou position de nom
//! incohérente rejette le buffer entier ; rien n'est deviné.

#![forbid(unsafe_code)]

use std::fmt;

/// Taille de l'en-tête fixe d'un USN_RECORD_V2, nom exclu.
const V2_HEADER_LEN: usize = 60;
/// Taille de l'en-tête fixe d'un USN_RECORD_V3, nom exclu.
const V3_HEADER_LEN: usize = 76;
/// En-tête commun : RecordLength (u32), MajorVersion (u16), MinorVersion (u16).
const COMMON_HEADER_LEN: usize = 8;

/// Enregistrement décodé (champs utiles à la reconstruction de l'index).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct UsnRecord {
    /// Référence de fichier (V2 élargie à 128 bits ; V3 = FILE_ID_128).
    pub file_id: u128,
    /// Référence du dossier parent.
    pub parent_id: u128,
    /// FILE_ATTRIBUTE_* bruts.
    pub attributes: u32,
    /// Nom (UTF-16 décodé ; une séquence invalide est remplacée par U+FFFD).
    pub name: String,
    /// Version majeure de l'enregistrement source (2 ou 3).
    pub major_version: u16,
}

/// Résultat du décodage d'un buffer.
#[derive(Debug, PartialEq, Eq)]
pub struct EnumOutput {
    /// Valeur à placer dans `StartFileReferenceNumber` pour l'appel suivant.
    pub next_start: u64,
    pub records: Vec<UsnRecord>,
}

#[derive(Debug, PartialEq, Eq)]
pub enum ParseError {
    BufferTooShort { len: usize },
    TruncatedHeader { offset: usize },
    InvalidRecordLength { offset: usize, length: u32 },
    UnsupportedVersion { offset: usize, major: u16, minor: u16 },
    NameOutOfBounds { offset: usize },
    OddNameLength { offset: usize },
}

impl fmt::Display for ParseError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::BufferTooShort { len } => write!(f, "buffer FSCTL_ENUM_USN_DATA trop court ({len} octets)"),
            Self::TruncatedHeader { offset } => write!(f, "en-tête USN tronqué à l'offset {offset}"),
            Self::InvalidRecordLength { offset, length } => {
                write!(f, "RecordLength invalide ({length}) à l'offset {offset}")
            }
            Self::UnsupportedVersion { offset, major, minor } => {
                write!(f, "version USN non prise en charge {major}.{minor} à l'offset {offset}")
            }
            Self::NameOutOfBounds { offset } => write!(f, "nom hors de l'enregistrement à l'offset {offset}"),
            Self::OddNameLength { offset } => write!(f, "longueur de nom impaire à l'offset {offset}"),
        }
    }
}

impl std::error::Error for ParseError {}

/// Décode un buffer de sortie complet (`bytes_returned` octets).
pub fn parse_enum_output(buffer: &[u8]) -> Result<EnumOutput, ParseError> {
    if buffer.len() < 8 {
        return Err(ParseError::BufferTooShort { len: buffer.len() });
    }
    let next_start = read_u64(buffer, 0);
    let mut records = Vec::new();
    let mut offset = 8;
    while offset < buffer.len() {
        let remaining = buffer.len() - offset;
        if remaining < COMMON_HEADER_LEN {
            return Err(ParseError::TruncatedHeader { offset });
        }
        let length = read_u32(buffer, offset);
        let major = read_u16(buffer, offset + 4);
        let minor = read_u16(buffer, offset + 6);
        let record_len = length as usize;
        if record_len < COMMON_HEADER_LEN || record_len > remaining {
            return Err(ParseError::InvalidRecordLength { offset, length });
        }
        let header_len = match (major, minor) {
            (2, 0) => V2_HEADER_LEN,
            (3, 0) => V3_HEADER_LEN,
            _ => return Err(ParseError::UnsupportedVersion { offset, major, minor }),
        };
        if record_len < header_len {
            return Err(ParseError::InvalidRecordLength { offset, length });
        }
        records.push(decode_record(&buffer[offset..offset + record_len], major, offset)?);
        offset += record_len;
    }
    Ok(EnumOutput { next_start, records })
}

/// `record` est exactement l'enregistrement (RecordLength octets), version déjà validée.
fn decode_record(record: &[u8], major: u16, offset: usize) -> Result<UsnRecord, ParseError> {
    let (file_id, parent_id, attributes_at, name_len_at, header_len) = if major == 2 {
        (u128::from(read_u64(record, 8)), u128::from(read_u64(record, 16)), 52, 56, V2_HEADER_LEN)
    } else {
        (read_u128(record, 8), read_u128(record, 24), 68, 72, V3_HEADER_LEN)
    };
    let name_len = usize::from(read_u16(record, name_len_at));
    let name_offset = usize::from(read_u16(record, name_len_at + 2));
    if name_offset < header_len || name_offset + name_len > record.len() {
        return Err(ParseError::NameOutOfBounds { offset });
    }
    if name_len % 2 != 0 {
        return Err(ParseError::OddNameLength { offset });
    }
    let units: Vec<u16> = record[name_offset..name_offset + name_len]
        .chunks_exact(2)
        .map(|pair| u16::from_le_bytes([pair[0], pair[1]]))
        .collect();
    Ok(UsnRecord {
        file_id,
        parent_id,
        attributes: read_u32(record, attributes_at),
        name: String::from_utf16_lossy(&units),
        major_version: major,
    })
}

// Les lectures ci-dessous sont toujours précédées d'un contrôle de longueur.
fn read_u16(bytes: &[u8], at: usize) -> u16 {
    u16::from_le_bytes(bytes[at..at + 2].try_into().expect("longueur contrôlée"))
}

fn read_u32(bytes: &[u8], at: usize) -> u32 {
    u32::from_le_bytes(bytes[at..at + 4].try_into().expect("longueur contrôlée"))
}

fn read_u64(bytes: &[u8], at: usize) -> u64 {
    u64::from_le_bytes(bytes[at..at + 8].try_into().expect("longueur contrôlée"))
}

fn read_u128(bytes: &[u8], at: usize) -> u128 {
    u128::from_le_bytes(bytes[at..at + 16].try_into().expect("longueur contrôlée"))
}

#[cfg(test)]
pub(crate) mod test_support {
    /// Construit un USN_RECORD_V2 synthétique, aligné sur 8 octets.
    pub fn v2_record(file: u64, parent: u64, attributes: u32, name: &str) -> Vec<u8> {
        let name: Vec<u8> = name.encode_utf16().flat_map(u16::to_le_bytes).collect();
        let mut record = vec![0u8; 60];
        record[4..6].copy_from_slice(&2u16.to_le_bytes());
        record[8..16].copy_from_slice(&file.to_le_bytes());
        record[16..24].copy_from_slice(&parent.to_le_bytes());
        record[52..56].copy_from_slice(&attributes.to_le_bytes());
        record[56..58].copy_from_slice(&(name.len() as u16).to_le_bytes());
        record[58..60].copy_from_slice(&60u16.to_le_bytes());
        record.extend_from_slice(&name);
        finish(record)
    }

    /// Construit un USN_RECORD_V3 synthétique, aligné sur 8 octets.
    pub fn v3_record(file: u128, parent: u128, attributes: u32, name: &str) -> Vec<u8> {
        let name: Vec<u8> = name.encode_utf16().flat_map(u16::to_le_bytes).collect();
        let mut record = vec![0u8; 76];
        record[4..6].copy_from_slice(&3u16.to_le_bytes());
        record[8..24].copy_from_slice(&file.to_le_bytes());
        record[24..40].copy_from_slice(&parent.to_le_bytes());
        record[68..72].copy_from_slice(&attributes.to_le_bytes());
        record[72..74].copy_from_slice(&(name.len() as u16).to_le_bytes());
        record[74..76].copy_from_slice(&76u16.to_le_bytes());
        record.extend_from_slice(&name);
        finish(record)
    }

    fn finish(mut record: Vec<u8>) -> Vec<u8> {
        while record.len() % 8 != 0 {
            record.push(0);
        }
        let length = record.len() as u32;
        record[0..4].copy_from_slice(&length.to_le_bytes());
        record
    }

    /// Buffer de sortie : référence suivante + enregistrements.
    pub fn enum_buffer(next_start: u64, records: &[Vec<u8>]) -> Vec<u8> {
        let mut buffer = next_start.to_le_bytes().to_vec();
        for record in records {
            buffer.extend_from_slice(record);
        }
        buffer
    }
}

#[cfg(test)]
mod tests {
    use super::test_support::{enum_buffer, v2_record, v3_record};
    use super::*;

    const DIR: u32 = 0x10;

    #[test]
    fn decodes_v2_record_fields_and_next_start() {
        let buffer = enum_buffer(0x1234, &[v2_record(0x0001_0000_0000_0040, 0x0005_0000_0000_0005, DIR, "Users")]);
        let output = parse_enum_output(&buffer).unwrap();
        assert_eq!(output.next_start, 0x1234);
        assert_eq!(
            output.records,
            vec![UsnRecord {
                file_id: 0x0001_0000_0000_0040,
                parent_id: 0x0005_0000_0000_0005,
                attributes: DIR,
                name: "Users".into(),
                major_version: 2,
            }]
        );
    }

    #[test]
    fn decodes_v3_record_with_128_bit_ids() {
        let file = (7u128 << 64) | 0x40;
        let buffer = enum_buffer(1, &[v3_record(file, 5, 0x20, "a.txt")]);
        let record = &parse_enum_output(&buffer).unwrap().records[0];
        assert_eq!((record.file_id, record.parent_id, record.attributes), (file, 5, 0x20));
        assert_eq!((record.name.as_str(), record.major_version), ("a.txt", 3));
    }

    #[test]
    fn decodes_consecutive_records_using_record_length() {
        let buffer = enum_buffer(9, &[v2_record(10, 5, 0, "un"), v3_record(11, 5, 0, "deux"), v2_record(12, 5, 0, "trois")]);
        let names: Vec<_> = parse_enum_output(&buffer).unwrap().records.into_iter().map(|r| r.name).collect();
        assert_eq!(names, ["un", "deux", "trois"]);
    }

    #[test]
    fn decodes_surrogate_pairs_in_names() {
        let buffer = enum_buffer(0, &[v2_record(10, 5, 0, "photo-📷.jpg")]);
        assert_eq!(parse_enum_output(&buffer).unwrap().records[0].name, "photo-📷.jpg");
    }

    #[test]
    fn accepts_buffer_holding_only_next_reference() {
        let output = parse_enum_output(&enum_buffer(42, &[])).unwrap();
        assert_eq!((output.next_start, output.records.len()), (42, 0));
    }

    #[test]
    fn rejects_buffer_shorter_than_next_reference() {
        assert_eq!(parse_enum_output(&[0u8; 7]), Err(ParseError::BufferTooShort { len: 7 }));
    }

    #[test]
    fn rejects_trailing_bytes_shorter_than_common_header() {
        let mut buffer = enum_buffer(0, &[v2_record(10, 5, 0, "a")]);
        let second = buffer.len();
        buffer.extend_from_slice(&[0u8; 4]);
        assert_eq!(parse_enum_output(&buffer), Err(ParseError::TruncatedHeader { offset: second }));
    }

    #[test]
    fn rejects_zero_record_length_instead_of_looping() {
        let mut record = v2_record(10, 5, 0, "a");
        record[0..4].copy_from_slice(&0u32.to_le_bytes());
        assert_eq!(
            parse_enum_output(&enum_buffer(0, &[record])),
            Err(ParseError::InvalidRecordLength { offset: 8, length: 0 })
        );
    }

    #[test]
    fn rejects_record_length_smaller_than_version_header() {
        let mut record = v3_record(10, 5, 0, "");
        record.truncate(72);
        record[0..4].copy_from_slice(&72u32.to_le_bytes());
        assert_eq!(
            parse_enum_output(&enum_buffer(0, &[record])),
            Err(ParseError::InvalidRecordLength { offset: 8, length: 72 })
        );
    }

    #[test]
    fn rejects_record_length_past_buffer_end() {
        let mut record = v2_record(10, 5, 0, "a");
        let declared = record.len() as u32 + 8;
        record[0..4].copy_from_slice(&declared.to_le_bytes());
        assert_eq!(
            parse_enum_output(&enum_buffer(0, &[record])),
            Err(ParseError::InvalidRecordLength { offset: 8, length: declared })
        );
    }

    #[test]
    fn rejects_unknown_major_version() {
        let mut record = v2_record(10, 5, 0, "a");
        record[4..6].copy_from_slice(&4u16.to_le_bytes());
        assert_eq!(
            parse_enum_output(&enum_buffer(0, &[record])),
            Err(ParseError::UnsupportedVersion { offset: 8, major: 4, minor: 0 })
        );
    }

    #[test]
    fn rejects_unknown_minor_version() {
        let mut record = v2_record(10, 5, 0, "a");
        record[6..8].copy_from_slice(&1u16.to_le_bytes());
        assert_eq!(
            parse_enum_output(&enum_buffer(0, &[record])),
            Err(ParseError::UnsupportedVersion { offset: 8, major: 2, minor: 1 })
        );
    }

    #[test]
    fn rejects_name_extending_past_record() {
        let mut record = v2_record(10, 5, 0, "abc");
        let too_long = record.len() as u16;
        record[56..58].copy_from_slice(&too_long.to_le_bytes());
        assert_eq!(parse_enum_output(&enum_buffer(0, &[record])), Err(ParseError::NameOutOfBounds { offset: 8 }));
    }

    #[test]
    fn rejects_name_offset_inside_fixed_header() {
        let mut record = v2_record(10, 5, 0, "abc");
        record[58..60].copy_from_slice(&40u16.to_le_bytes());
        assert_eq!(parse_enum_output(&enum_buffer(0, &[record])), Err(ParseError::NameOutOfBounds { offset: 8 }));
    }

    #[test]
    fn rejects_odd_name_length() {
        let mut record = v2_record(10, 5, 0, "abc");
        record[56..58].copy_from_slice(&5u16.to_le_bytes());
        assert_eq!(parse_enum_output(&enum_buffer(0, &[record])), Err(ParseError::OddNameLength { offset: 8 }));
    }
}
