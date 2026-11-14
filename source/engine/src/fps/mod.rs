//! FPS des jeux par ETW (sans injection), dans le composant de session élevé.
//!
//! Une session ETW temps réel « OPTIVANCE-FPS » écoute les événements « Present » publiés par
//! Windows : runtimes DXGI (Direct3D 10/11/12) et D3D9, et noyau graphique DxgKrnl (tous les
//! jeux, y compris OpenGL et Vulkan ; même source que PresentMon). Chaque source est comptée
//! séparément par processus pour ne pas compter deux fois une même image : le runtime est
//! prioritaire, sinon la plus active des sources du noyau. La session est arrêtée à la fin du
//! composant (et une session orpheline est arrêtée au démarrage).

pub mod sys;

use std::collections::VecDeque;

use crate::fasthash::FastMap;

/// Événements conservés par processus (≈ 3 s à 500 images/s).
const MAX_EVENTS: usize = 1500;

/// Source d'un événement « image affichée ».
#[derive(Clone, Copy, PartialEq, Eq, Hash, Debug)]
pub enum Source {
    /// DXGI ou D3D9 (une image = un événement).
    Runtime,
    /// DxgKrnl Present_Info (appel D3DKMTPresent).
    KernelPresent,
    /// DxgKrnl PresentHistory(Detailed)_Start (image composée par le bureau).
    KernelHistory,
}

/// Horodatages d'images par processus et par source (unités QPC).
#[derive(Default)]
pub struct FpsCounter {
    events: FastMap<(u32, Source), VecDeque<i64>>,
}

impl FpsCounter {
    pub fn record(&mut self, pid: u32, timestamp: i64) {
        self.record_from(pid, Source::Runtime, timestamp);
    }

    pub fn record_from(&mut self, pid: u32, source: Source, timestamp: i64) {
        let queue = self.events.entry((pid, source)).or_default();
        queue.push_back(timestamp);
        while queue.len() > MAX_EVENTS {
            queue.pop_front();
        }
    }

    /// FPS de `pid` sur la dernière seconde d'événements reçus. `None` si le processus n'a rien
    /// affiché depuis plus de 2 s (`now`, horloge QPC) ou s'il n'y a pas assez d'images.
    pub fn fps(&self, pid: u32, now: i64, frequency: i64) -> Option<f64> {
        if let Some(runtime) = self.source_fps(pid, Source::Runtime, now, frequency) {
            return Some(runtime);
        }
        match (self.source_fps(pid, Source::KernelPresent, now, frequency), self.source_fps(pid, Source::KernelHistory, now, frequency)) {
            (Some(a), Some(b)) => Some(a.max(b)),
            (a, b) => a.or(b),
        }
    }

    fn source_fps(&self, pid: u32, source: Source, now: i64, frequency: i64) -> Option<f64> {
        let queue = self.events.get(&(pid, source))?;
        let last = *queue.back()?;
        if frequency <= 0 || now - last > 2 * frequency {
            return None;
        }
        let window_start = last - frequency;
        let inside: Vec<i64> = queue.iter().copied().filter(|t| *t >= window_start).collect();
        if inside.len() < 2 {
            return None;
        }
        let span = (inside[inside.len() - 1] - inside[0]) as f64 / frequency as f64;
        (span > 0.0).then(|| (inside.len() - 1) as f64 / span)
    }

    /// Oublie les processus inactifs depuis plus de 5 s.
    pub fn prune(&mut self, now: i64, frequency: i64) {
        self.events.retain(|_, queue| queue.back().is_some_and(|last| now - last <= 5 * frequency));
    }
}

/// Commande `{"v":3,"type":"fps-query","pid":1234}` → pid.
pub fn parse_query(line: &str) -> Option<u32> {
    let pid = line.trim_end_matches(['\r', '\n']).strip_prefix("{\"v\":3,\"type\":\"fps-query\",\"pid\":")?.strip_suffix('}')?;
    (!pid.is_empty() && pid.len() <= 10 && pid.bytes().all(|b| b.is_ascii_digit())).then(|| pid.parse().ok()).flatten()
}

pub fn result_json(pid: u32, fps: Option<f64>, running: bool) -> String {
    match fps {
        Some(value) => format!("{{\"v\":3,\"type\":\"fps\",\"pid\":{pid},\"fps\":{value:.1},\"running\":{running}}}"),
        None => format!("{{\"v\":3,\"type\":\"fps\",\"pid\":{pid},\"fps\":null,\"running\":{running}}}"),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const FREQ: i64 = 10_000_000;

    #[test]
    fn counts_frames_over_the_last_second() {
        let mut counter = FpsCounter::default();
        // 144 images/s pendant 2 s pour le processus 42.
        for i in 0..=288 {
            counter.record(42, i * FREQ / 144);
        }
        let fps = counter.fps(42, 2 * FREQ, FREQ).unwrap();
        assert!((fps - 144.0).abs() < 1.0, "{fps}");
        assert_eq!(counter.fps(7, 2 * FREQ, FREQ), None);
    }

    #[test]
    fn stale_or_sparse_processes_have_no_fps() {
        let mut counter = FpsCounter::default();
        counter.record(1, 0);
        assert_eq!(counter.fps(1, 0, FREQ), None, "une seule image");
        counter.record(1, FREQ / 60);
        assert!(counter.fps(1, FREQ / 60, FREQ).is_some());
        assert_eq!(counter.fps(1, 3 * FREQ, FREQ), None, "plus rien depuis 3 s");
        counter.prune(10 * FREQ, FREQ);
        assert!(counter.events.is_empty());
    }

    #[test]
    fn keeps_memory_bounded() {
        let mut counter = FpsCounter::default();
        for i in 0..10_000 {
            counter.record(3, i);
        }
        assert_eq!(counter.events[&(3, Source::Runtime)].len(), MAX_EVENTS);
    }

    #[test]
    fn runtime_wins_and_kernel_sources_are_not_added() {
        let mut counter = FpsCounter::default();
        // OpenGL : seulement le noyau, 60 images/s par Present_Info et autant par PresentHistory.
        for i in 0..=120 {
            counter.record_from(9, Source::KernelPresent, i * FREQ / 60);
            counter.record_from(9, Source::KernelHistory, i * FREQ / 60);
        }
        let fps = counter.fps(9, 2 * FREQ, FREQ).unwrap();
        assert!((fps - 60.0).abs() < 1.0, "pas de double comptage : {fps}");
        // DirectX : le runtime (144) est prioritaire sur le noyau.
        for i in 0..=288 {
            counter.record_from(9, Source::Runtime, i * FREQ / 144);
        }
        let fps = counter.fps(9, 2 * FREQ, FREQ).unwrap();
        assert!((fps - 144.0).abs() < 1.0, "{fps}");
    }

    #[test]
    fn parses_only_exact_queries() {
        assert_eq!(parse_query("{\"v\":3,\"type\":\"fps-query\",\"pid\":1234}\n"), Some(1234));
        for line in ["{\"v\":3,\"type\":\"fps-query\",\"pid\":-1}", "{\"v\":3,\"type\":\"fps-query\",\"pid\":\"1\"}", "{\"v\":3,\"type\":\"fps-query\",\"pid\":}", "{\"v\":3,\"type\":\"fps-query\",\"pid\":99999999999}"] {
            assert_eq!(parse_query(line), None, "{line}");
        }
        assert_eq!(result_json(5, Some(143.94), true), "{\"v\":3,\"type\":\"fps\",\"pid\":5,\"fps\":143.9,\"running\":true}");
        assert_eq!(result_json(5, None, false), "{\"v\":3,\"type\":\"fps\",\"pid\":5,\"fps\":null,\"running\":false}");
    }
}
