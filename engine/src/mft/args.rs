//! Arguments du helper élevé : uniquement `--pipe-name` et `--volume`, chacun une fois.

#![forbid(unsafe_code)]

use super::volume::{is_valid_pipe_name, parse_volume_letter};

#[derive(Debug, PartialEq, Eq)]
pub struct HelperArgs {
    pub pipe_name: String,
    pub volume: char,
}

pub fn parse_helper_args(arguments: impl IntoIterator<Item = String>) -> Result<HelperArgs, String> {
    let mut pipe_name = None;
    let mut volume = None;
    let mut arguments = arguments.into_iter();
    while let Some(flag) = arguments.next() {
        let slot = match flag.as_str() {
            "--pipe-name" => &mut pipe_name,
            "--volume" => &mut volume,
            _ => return Err(format!("argument inconnu : {flag}")),
        };
        if slot.is_some() {
            return Err(format!("argument répété : {flag}"));
        }
        *slot = Some(arguments.next().ok_or_else(|| format!("valeur manquante pour {flag}"))?);
    }
    let pipe_name = pipe_name.ok_or("argument --pipe-name manquant")?;
    if !is_valid_pipe_name(&pipe_name) {
        return Err("nom de pipe refusé".into());
    }
    let volume = volume.ok_or("argument --volume manquant")?;
    let volume = parse_volume_letter(&volume).ok_or_else(|| format!("volume refusé : {volume}"))?;
    Ok(HelperArgs { pipe_name, volume })
}

#[cfg(test)]
mod tests {
    use super::*;

    const PIPE: &str = "optivance-mft-0123456789abcdef0123456789abcdef";

    fn parse(arguments: &[&str]) -> Result<HelperArgs, String> {
        parse_helper_args(arguments.iter().map(|argument| argument.to_string()))
    }

    #[test]
    fn accepts_both_arguments_in_any_order() {
        let expected = HelperArgs { pipe_name: PIPE.into(), volume: 'C' };
        assert_eq!(parse(&["--pipe-name", PIPE, "--volume", "C"]), Ok(expected));
        assert_eq!(parse(&["--volume", "c:", "--pipe-name", PIPE]).unwrap().volume, 'C');
    }

    #[test]
    fn rejects_missing_repeated_unknown_or_invalid_arguments() {
        for arguments in [
            vec![],
            vec!["--pipe-name", PIPE],
            vec!["--volume", "C"],
            vec!["--pipe-name", PIPE, "--volume", "C", "--volume", "D"],
            vec!["--pipe-name", PIPE, "--volume", "C", "--target", "C:\\"],
            vec!["--pipe-name", PIPE, "--volume"],
            vec!["--pipe-name", "..\\..\\C:\\x", "--volume", "C"],
            vec!["--pipe-name", PIPE, "--volume", "C:\\Windows"],
        ] {
            assert!(parse(&arguments).is_err(), "{arguments:?}");
        }
    }
}
