//! IDE Detection — placeholder module.
//! Window title scanning for IDE project detection may be added later.

#[derive(Debug, Clone, serde::Serialize)]
pub struct DetectedProject {
    pub name: String,
    pub path: Option<String>,
    pub ide: String,
}

pub fn detect_projects() -> Vec<DetectedProject> {
    // IDE detection is not yet implemented — manual session creation only
    Vec::new()
}
