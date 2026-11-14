//! Boucle d'énumération `FSCTL_ENUM_USN_DATA`, indépendante de l'appel système.
//!
//! `fetch(start, buffer)` effectue un appel (dans le helper : `DeviceIoControl`) et renvoie
//! le nombre d'octets écrits ou le code d'erreur Win32. La fin normale est signalée par
//! `ERROR_HANDLE_EOF`. La boucle refuse de tourner sur place et s'arrête à la demande.

#![forbid(unsafe_code)]

use std::fmt;

use super::records::{parse_enum_output, ParseError, UsnRecord};

pub const ERROR_ACCESS_DENIED: i32 = 5;
pub const ERROR_HANDLE_EOF: i32 = 38;
pub const ERROR_JOURNAL_NOT_ACTIVE: i32 = 1179;

#[derive(Debug, PartialEq, Eq)]
pub enum EnumError {
    Os(i32),
    Parse(ParseError),
    NoProgress { start: u64 },
    Cancelled,
}

impl EnumError {
    /// Code machine transmis à l'interface (choix du message et du repli).
    pub fn code(&self) -> &'static str {
        match self {
            Self::Os(ERROR_ACCESS_DENIED) => "access-denied",
            Self::Os(ERROR_JOURNAL_NOT_ACTIVE) => "journal-inactive",
            Self::Os(_) | Self::NoProgress { .. } => "enumeration-failed",
            Self::Parse(_) => "parse-failed",
            Self::Cancelled => "cancelled",
        }
    }
}

impl fmt::Display for EnumError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Os(code) => write!(f, "FSCTL_ENUM_USN_DATA a échoué (erreur Win32 {code})"),
            Self::Parse(error) => write!(f, "réponse MFT illisible : {error}"),
            Self::NoProgress { start } => write!(f, "l'énumération MFT n'avance plus (référence {start})"),
            Self::Cancelled => write!(f, "énumération MFT annulée"),
        }
    }
}

/// `keep_going(records_so_far)` est appelé après chaque buffer ; `false` annule.
pub fn enumerate_with(
    buffer_size: usize,
    fetch: impl FnMut(u64, &mut [u8]) -> Result<usize, i32>,
    keep_going: impl FnMut(usize) -> bool,
) -> Result<Vec<UsnRecord>, EnumError> {
    let (mut fetch, mut keep_going) = (fetch, keep_going);
    let mut buffer = vec![0u8; buffer_size];
    let mut records = Vec::new();
    let mut start = 0u64;
    loop {
        let written = match fetch(start, &mut buffer) {
            Ok(written) => written.min(buffer.len()),
            Err(ERROR_HANDLE_EOF) => return Ok(records),
            Err(code) => return Err(EnumError::Os(code)),
        };
        let output = parse_enum_output(&buffer[..written]).map_err(EnumError::Parse)?;
        if output.next_start == start {
            return Err(EnumError::NoProgress { start });
        }
        records.extend(output.records);
        if !keep_going(records.len()) {
            return Err(EnumError::Cancelled);
        }
        start = output.next_start;
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::mft::records::test_support::{enum_buffer, v2_record};

    /// Simule une suite de réponses ; enregistre les références de départ reçues.
    fn scripted(responses: Vec<Result<Vec<u8>, i32>>, starts: &mut Vec<u64>) -> impl FnMut(u64, &mut [u8]) -> Result<usize, i32> + '_ {
        let mut responses = responses.into_iter();
        move |start, buffer| {
            starts.push(start);
            let bytes = responses.next().unwrap_or(Err(ERROR_HANDLE_EOF))?;
            buffer[..bytes.len()].copy_from_slice(&bytes);
            Ok(bytes.len())
        }
    }

    #[test]
    fn chains_calls_with_next_start_until_handle_eof() {
        let mut starts = Vec::new();
        let responses = vec![
            Ok(enum_buffer(100, &[v2_record(10, 5, 0, "a"), v2_record(11, 5, 0, "b")])),
            Ok(enum_buffer(200, &[v2_record(12, 5, 0, "c")])),
        ];
        let records = enumerate_with(4096, scripted(responses, &mut starts), |_| true).unwrap();
        let names: Vec<_> = records.into_iter().map(|r| r.name).collect();
        assert_eq!(names, ["a", "b", "c"]);
        assert_eq!(starts, [0, 100, 200]);
    }

    #[test]
    fn reports_win32_error_with_machine_code() {
        let mut starts = Vec::new();
        let error = enumerate_with(4096, scripted(vec![Err(ERROR_ACCESS_DENIED)], &mut starts), |_| true).unwrap_err();
        assert_eq!(error, EnumError::Os(ERROR_ACCESS_DENIED));
        assert_eq!(error.code(), "access-denied");
        assert_eq!(EnumError::Os(ERROR_JOURNAL_NOT_ACTIVE).code(), "journal-inactive");
    }

    #[test]
    fn refuses_to_loop_when_next_start_does_not_move() {
        let mut starts = Vec::new();
        let responses = vec![Ok(enum_buffer(100, &[v2_record(10, 5, 0, "a")])), Ok(enum_buffer(100, &[v2_record(10, 5, 0, "a")]))];
        let error = enumerate_with(4096, scripted(responses, &mut starts), |_| true).unwrap_err();
        assert_eq!(error, EnumError::NoProgress { start: 100 });
    }

    #[test]
    fn propagates_parse_errors() {
        let mut starts = Vec::new();
        let error = enumerate_with(4096, scripted(vec![Ok(vec![1, 2, 3])], &mut starts), |_| true).unwrap_err();
        assert_eq!(error, EnumError::Parse(ParseError::BufferTooShort { len: 3 }));
        assert_eq!(error.code(), "parse-failed");
    }

    #[test]
    fn stops_when_cancellation_is_requested() {
        let mut starts = Vec::new();
        let responses = vec![Ok(enum_buffer(100, &[v2_record(10, 5, 0, "a")])), Ok(enum_buffer(200, &[v2_record(11, 5, 0, "b")]))];
        let mut seen = Vec::new();
        let error = enumerate_with(4096, scripted(responses, &mut starts), |count| {
            seen.push(count);
            false
        })
        .unwrap_err();
        assert_eq!(error, EnumError::Cancelled);
        assert_eq!((seen, starts), (vec![1], vec![0]));
    }
}
