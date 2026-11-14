fn main() {
    // Helper élevé : ses dépendances statiques (ex. bcryptprimitives.dll, absente des
    // KnownDLLs) ne sont résolues que depuis System32, jamais depuis le dossier de
    // l'application (Windows 10 1607+). Les chargements dynamiques sont restreints au
    // démarrage par `restrict_dll_search_to_system32`.
    println!("cargo:rustc-link-arg-bin=optivance-mft-helper=/DEPENDENTLOADFLAG:0x800");
}
