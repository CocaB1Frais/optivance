//! Réglages système demandant les droits administrateur (module Optimisation), exécutés par
//! le composant de session élevé. Listes fermées : seuls les services, tâches planifiées et
//! valeurs de démarrage ci-dessous peuvent être modifiés ; les outils Windows sont appelés
//! par leur chemin absolu dans System32, avec des arguments fixes.

#![forbid(unsafe_code)]

use std::os::windows::process::CommandExt;
use std::process::Command;

/// Services modifiables (nom exact Windows).
pub const SERVICES: &[&str] = &[
    "DiagTrack",
    "dmwappushservice",
    "SysMain",
    "WSearch",
    "Spooler",
    "Fax",
    "MapsBroker",
    "WerSvc",
    "XblAuthManager",
    "XblGameSave",
    "XboxNetApiSvc",
    "XboxGipSvc",
    "RetailDemo",
    "lfsvc",
];

/// Tâches planifiées modifiables : identifiant → chemin complet.
pub const TASKS: &[(&str, &str)] = &[
    ("compat-appraiser", r"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser"),
    ("program-data-updater", r"\Microsoft\Windows\Application Experience\ProgramDataUpdater"),
    ("ceip-consolidator", r"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator"),
    ("ceip-usb", r"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip"),
    ("autochk-proxy", r"\Microsoft\Windows\Autochk\Proxy"),
    ("disk-diagnostic", r"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector"),
    ("feedback-dm", r"\Microsoft\Windows\Feedback\Siuf\DmClient"),
];

const START_MODES: &[&str] = &["auto", "delayed-auto", "demand", "disabled"];
const RUN_KEYS: &[&str] = &["Run", "Run32"];

#[derive(Debug, PartialEq, Eq)]
pub enum TuneCommand {
    /// Type de démarrage d'un service de la liste.
    Service { name: &'static str, start: &'static str },
    /// Activation d'une tâche planifiée de la liste.
    Task { path: &'static str, enabled: bool },
    /// Activation d'une application au démarrage de HKLM (valeur existante uniquement).
    Startup { key: &'static str, name: String, enabled: bool },
}

fn fixed(list: &[&'static str], value: &str) -> Option<&'static str> {
    list.iter().copied().find(|known| *known == value)
}

/// Lit `"clé":"valeur"` dans une commande au format exact produit par l'interface.
fn field<'a>(line: &'a str, key: &str) -> Option<&'a str> {
    let marker = format!("\"{key}\":\"");
    let start = line.find(&marker)? + marker.len();
    let rest = &line[start..];
    let end = rest.find('"')?;
    let value = &rest[..end];
    (!value.contains('\\')).then_some(value)
}

/// Commandes acceptées (aucune autre forme) :
/// `{"v":3,"type":"tune-service","name":"SysMain","start":"disabled"}`
/// `{"v":3,"type":"tune-task","id":"ceip-usb","enabled":"false"}`
/// `{"v":3,"type":"tune-startup","key":"Run","name":"<valeur>","enabled":"true"}`
pub fn parse_tune_command(line: &str) -> Option<TuneCommand> {
    let line = line.trim_end_matches(['\r', '\n']);
    let boolean = |value: &str| match value {
        "true" => Some(true),
        "false" => Some(false),
        _ => None,
    };
    if line.starts_with("{\"v\":3,\"type\":\"tune-service\",") {
        let name = fixed(SERVICES, field(line, "name")?)?;
        let start = fixed(START_MODES, field(line, "start")?)?;
        let expected = format!("{{\"v\":3,\"type\":\"tune-service\",\"name\":\"{name}\",\"start\":\"{start}\"}}");
        return (line == expected).then_some(TuneCommand::Service { name, start });
    }
    if line.starts_with("{\"v\":3,\"type\":\"tune-task\",") {
        let id = field(line, "id")?;
        let (id, path) = TASKS.iter().copied().find(|(known, _)| *known == id)?;
        let enabled = boolean(field(line, "enabled")?)?;
        let expected = format!("{{\"v\":3,\"type\":\"tune-task\",\"id\":\"{id}\",\"enabled\":\"{enabled}\"}}");
        return (line == expected).then_some(TuneCommand::Task { path, enabled });
    }
    if line.starts_with("{\"v\":3,\"type\":\"tune-startup\",") {
        let key = fixed(RUN_KEYS, field(line, "key")?)?;
        let name = field(line, "name")?;
        if name.is_empty() || name.len() > 260 || name.chars().any(|c| c.is_control() || c == '"') {
            return None;
        }
        let enabled = boolean(field(line, "enabled")?)?;
        let expected = format!("{{\"v\":3,\"type\":\"tune-startup\",\"key\":\"{key}\",\"name\":\"{name}\",\"enabled\":\"{enabled}\"}}");
        return (line == expected).then(|| TuneCommand::Startup { key, name: name.to_string(), enabled });
    }
    None
}

const CREATE_NO_WINDOW: u32 = 0x0800_0000;

fn system_tool(windows: &str, tool: &str) -> Command {
    let mut command = Command::new(format!(r"{windows}\System32\{tool}"));
    command.creation_flags(CREATE_NO_WINDOW);
    command
}

fn run(mut command: Command) -> i32 {
    command.output().map_or(-1, |output| output.status.code().unwrap_or(-1))
}

/// Exécute la commande ; renvoie le code de sortie de l'outil Windows (0 = succès).
pub fn execute(command: &TuneCommand) -> i32 {
    let Ok(windows) = crate::clean::sys::system_windows_directory() else {
        return -1;
    };
    match command {
        TuneCommand::Service { name, start } => {
            let mut sc = system_tool(&windows, "sc.exe");
            sc.args(["config", *name, "start=", *start]);
            run(sc)
        }
        TuneCommand::Task { path, enabled } => {
            let mut schtasks = system_tool(&windows, "schtasks.exe");
            schtasks.args(["/Change", "/TN", *path, if *enabled { "/Enable" } else { "/Disable" }]);
            run(schtasks)
        }
        TuneCommand::Startup { key, name, enabled } => {
            let (run_key, approved) = match *key {
                "Run32" => (r"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", r"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32"),
                _ => (r"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", r"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"),
            };
            // La valeur doit exister dans la clé Run : aucune entrée arbitraire n'est créée.
            let mut query = system_tool(&windows, "reg.exe");
            query.args(["query", run_key, "/v", name.as_str()]);
            if run(query) != 0 {
                return 2;
            }
            // Format de Windows (Gestionnaire des tâches) : 02… activé, 03… désactivé.
            let data = if *enabled { "020000000000000000000000" } else { "030000000000000000000000" };
            let mut add = system_tool(&windows, "reg.exe");
            add.args(["add", approved, "/v", name.as_str(), "/t", "REG_BINARY", "/d", data, "/f"]);
            run(add)
        }
    }
}

pub fn result_json(code: i32) -> String {
    format!("{{\"v\":3,\"type\":\"tune\",\"ok\":{},\"code\":{code}}}", code == 0)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn accepts_exact_allowlisted_commands() {
        assert_eq!(
            parse_tune_command("{\"v\":3,\"type\":\"tune-service\",\"name\":\"SysMain\",\"start\":\"disabled\"}\n"),
            Some(TuneCommand::Service { name: "SysMain", start: "disabled" })
        );
        assert_eq!(
            parse_tune_command("{\"v\":3,\"type\":\"tune-task\",\"id\":\"ceip-usb\",\"enabled\":\"false\"}"),
            Some(TuneCommand::Task { path: r"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip", enabled: false })
        );
        assert_eq!(
            parse_tune_command("{\"v\":3,\"type\":\"tune-startup\",\"key\":\"Run\",\"name\":\"RtkAudUService\",\"enabled\":\"true\"}"),
            Some(TuneCommand::Startup { key: "Run", name: "RtkAudUService".into(), enabled: true })
        );
    }

    #[test]
    fn rejects_everything_else() {
        for line in [
            "{\"v\":3,\"type\":\"tune-service\",\"name\":\"WinDefend\",\"start\":\"disabled\"}",
            "{\"v\":3,\"type\":\"tune-service\",\"name\":\"SysMain\",\"start\":\"boot\"}",
            "{\"v\":3,\"type\":\"tune-service\",\"name\":\"SysMain\",\"start\":\"disabled\",\"x\":\"y\"}",
            "{\"v\":3,\"type\":\"tune-service\",\"start\":\"disabled\",\"name\":\"SysMain\"}",
            "{\"v\":3,\"type\":\"tune-task\",\"id\":\"\\\\Microsoft\\\\Windows\\\\Defrag\",\"enabled\":\"false\"}",
            "{\"v\":3,\"type\":\"tune-task\",\"id\":\"ceip-usb\",\"enabled\":\"0\"}",
            "{\"v\":3,\"type\":\"tune-startup\",\"key\":\"RunOnce\",\"name\":\"x\",\"enabled\":\"true\"}",
            "{\"v\":3,\"type\":\"tune-startup\",\"key\":\"Run\",\"name\":\"\",\"enabled\":\"true\"}",
            "{\"v\":3,\"type\":\"tune-startup\",\"key\":\"Run\",\"name\":\"a\\\\b\",\"enabled\":\"true\"}",
            "{\"v\":3,\"type\":\"scan\",\"volume\":\"C\"}",
        ] {
            assert_eq!(parse_tune_command(line), None, "{line:?}");
        }
    }

    #[test]
    fn result_line() {
        assert_eq!(result_json(0), "{\"v\":3,\"type\":\"tune\",\"ok\":true,\"code\":0}");
        assert_eq!(result_json(5), "{\"v\":3,\"type\":\"tune\",\"ok\":false,\"code\":5}");
    }
}
