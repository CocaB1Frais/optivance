//! Arguments et commandes du helper élevé.
//!
//! Lancement : `--pipe-name <nom>` et, au choix, `--volume <X>` (une analyse puis fin) ou
//! `--session` (composant de session : reste connecté et attend des commandes ; décision du
//! propriétaire du 13/11/2026, une seule autorisation UAC par session d'OPTIVANCE).
//! Seules trois commandes existent, au format exact : analyser un volume, annuler, quitter.

#![forbid(unsafe_code)]

use super::volume::{is_valid_pipe_name, parse_volume_letter};

#[derive(Debug, PartialEq, Eq)]
pub enum HelperMode {
    /// Une analyse du volume indiqué, puis fin.
    Single(char),
    /// Reste connecté et exécute les commandes de l'interface jusqu'à sa fermeture.
    Session,
}

#[derive(Debug, PartialEq, Eq)]
pub struct HelperArgs {
    pub pipe_name: String,
    pub mode: HelperMode,
}

pub fn parse_helper_args(arguments: impl IntoIterator<Item = String>) -> Result<HelperArgs, String> {
    let mut pipe_name = None;
    let mut volume = None;
    let mut session = false;
    let mut arguments = arguments.into_iter();
    while let Some(flag) = arguments.next() {
        match flag.as_str() {
            "--session" if !session => session = true,
            "--pipe-name" | "--volume" => {
                let slot = if flag == "--pipe-name" { &mut pipe_name } else { &mut volume };
                if slot.is_some() {
                    return Err(format!("argument répété : {flag}"));
                }
                *slot = Some(arguments.next().ok_or_else(|| format!("valeur manquante pour {flag}"))?);
            }
            _ => return Err(format!("argument inconnu ou répété : {flag}")),
        }
    }
    let pipe_name = pipe_name.ok_or("argument --pipe-name manquant")?;
    if !is_valid_pipe_name(&pipe_name) {
        return Err("nom de pipe refusé".into());
    }
    let mode = match (volume, session) {
        (Some(volume), false) => HelperMode::Single(parse_volume_letter(&volume).ok_or_else(|| format!("volume refusé : {volume}"))?),
        (None, true) => HelperMode::Session,
        (Some(_), true) => return Err("--volume et --session sont exclusifs".into()),
        (None, false) => return Err("argument --volume ou --session manquant".into()),
    };
    Ok(HelperArgs { pipe_name, mode })
}

#[derive(Debug, PartialEq, Eq)]
pub enum SessionCommand {
    Scan(char),
    /// Nettoyage approfondi d'une catégorie du catalogue : aperçu (`run == false`) ou suppression.
    Clean { category: &'static str, run: bool },
    Quit,
    /// Ligne non reconnue : ignorée (aucune autre commande n'est exécutable).
    Unknown,
}

pub fn parse_session_command(line: &str) -> SessionCommand {
    let line = line.trim_end_matches(['\r', '\n']);
    if line == "{\"v\":3,\"type\":\"quit\"}" {
        return SessionCommand::Quit;
    }
    for (prefix, run) in [("{\"v\":3,\"type\":\"clean-analyze\",\"category\":\"", false), ("{\"v\":3,\"type\":\"clean-run\",\"category\":\"", true)] {
        if let Some(category) = line.strip_prefix(prefix).and_then(|rest| rest.strip_suffix("\"}")).and_then(crate::clean::category_id) {
            return SessionCommand::Clean { category, run };
        }
    }
    line.strip_prefix("{\"v\":3,\"type\":\"scan\",\"volume\":\"")
        .and_then(|rest| rest.strip_suffix("\"}"))
        .and_then(parse_volume_letter)
        .map_or(SessionCommand::Unknown, SessionCommand::Scan)
}

#[cfg(test)]
mod tests {
    use super::*;

    const PIPE: &str = "optivance-mft-0123456789abcdef0123456789abcdef";

    fn parse(arguments: &[&str]) -> Result<HelperArgs, String> {
        parse_helper_args(arguments.iter().map(|argument| argument.to_string()))
    }

    #[test]
    fn accepts_single_volume_mode_in_any_order() {
        assert_eq!(parse(&["--pipe-name", PIPE, "--volume", "C"]), Ok(HelperArgs { pipe_name: PIPE.into(), mode: HelperMode::Single('C') }));
        assert_eq!(parse(&["--volume", "c:", "--pipe-name", PIPE]).unwrap().mode, HelperMode::Single('C'));
    }

    #[test]
    fn accepts_session_mode() {
        assert_eq!(parse(&["--pipe-name", PIPE, "--session"]), Ok(HelperArgs { pipe_name: PIPE.into(), mode: HelperMode::Session }));
        assert_eq!(parse(&["--session", "--pipe-name", PIPE]).unwrap().mode, HelperMode::Session);
    }

    #[test]
    fn rejects_missing_repeated_unknown_or_invalid_arguments() {
        for arguments in [
            vec![],
            vec!["--pipe-name", PIPE],
            vec!["--volume", "C"],
            vec!["--session"],
            vec!["--pipe-name", PIPE, "--volume", "C", "--session"],
            vec!["--pipe-name", PIPE, "--session", "--session"],
            vec!["--pipe-name", PIPE, "--volume", "C", "--volume", "D"],
            vec!["--pipe-name", PIPE, "--volume", "C", "--target", "C:\\"],
            vec!["--pipe-name", PIPE, "--volume"],
            vec!["--pipe-name", "..\\..\\C:\\x", "--session"],
            vec!["--pipe-name", PIPE, "--volume", "C:\\Windows"],
        ] {
            assert!(parse(&arguments).is_err(), "{arguments:?}");
        }
    }

    #[test]
    fn recognizes_only_exact_session_commands() {
        assert_eq!(parse_session_command("{\"v\":3,\"type\":\"scan\",\"volume\":\"E\"}"), SessionCommand::Scan('E'));
        assert_eq!(parse_session_command("{\"v\":3,\"type\":\"scan\",\"volume\":\"e\"}\r"), SessionCommand::Scan('E'));
        assert_eq!(parse_session_command("{\"v\":3,\"type\":\"quit\"}"), SessionCommand::Quit);
        assert_eq!(
            parse_session_command("{\"v\":3,\"type\":\"clean-analyze\",\"category\":\"windows-temp\"}"),
            SessionCommand::Clean { category: "windows-temp", run: false }
        );
        assert_eq!(
            parse_session_command("{\"v\":3,\"type\":\"clean-run\",\"category\":\"windows-update\"}\r\n"),
            SessionCommand::Clean { category: "windows-update", run: true }
        );
        for line in [
            "{\"v\":3,\"type\":\"clean-run\",\"category\":\"user-temp\"}",
            "{\"v\":3,\"type\":\"clean-run\",\"category\":\"C:\\\\Users\"}",
            "{\"v\":3,\"type\":\"clean-run\",\"category\":\"windows-temp\",\"path\":\"C:\\\\\"}",
        ] {
            assert_eq!(parse_session_command(line), SessionCommand::Unknown, "{line:?}");
        }
        for line in [
            "",
            "{\"v\":3,\"type\":\"scan\",\"volume\":\"C:\\\\Windows\"}",
            "{\"v\":3,\"type\":\"scan\",\"volume\":\"1\"}",
            "{\"v\":2,\"type\":\"scan\",\"volume\":\"C\"}",
            "{\"v\":3,\"type\":\"delete\",\"path\":\"C:\\\\x\"}",
            "{\"v\":3,\"type\":\"cancel\"}",
        ] {
            assert_eq!(parse_session_command(line), SessionCommand::Unknown, "{line:?}");
        }
    }
}
