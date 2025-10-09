use std::{
    error::Error,
    io::BufRead,
    path::PathBuf,
    process::{Command, Stdio},
};

use serde::Deserialize;

use crate::args::BuildArgs;

pub fn build(args: &BuildArgs) -> Result<Option<PathBuf>, Box<dyn Error>> {
    let mut command = Command::new("cargo");
    command.arg("build");

    if let Some(project) = &args.project {
        command.arg("--package").arg(project);
    }

    if !args.feature.is_empty() {
        command.arg("--features").arg(args.feature.join(","));
    }

    command.args([
        "--lib",
        "--target",
        "wasm32-unknown-unknown",
        "--release",
        "--message-format",
        "json-render-diagnostics",
    ]);

    command.args(&args.cargo_args);

    command
        .stderr(Stdio::inherit())
        .stdout(Stdio::piped())
        .stdin(Stdio::null());

    let output = command.output()?;

    let mut wasm = None;
    for line in output.stdout.lines() {
        let message: CargoMessage = serde_json::from_str(&line?)?;
        match message {
            CargoMessage::CompilerArtifact(artifact)
                if artifact
                    .target
                    .kind
                    .iter()
                    .any(|it| *it == TargetKind::Cdylib) =>
            {
                if let Some(found) = artifact
                    .filenames
                    .into_iter()
                    .find(|file| file.extension().is_some_and(|ext| ext == "wasm"))
                {
                    wasm = Some(found);
                }
            }
            CargoMessage::BuildFinished { success: false } => return Ok(None),
            CargoMessage::BuildFinished { success: true } => break,
            _ => continue,
        }
    }

    Ok(wasm)
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "kebab-case", tag = "reason")]
enum CargoMessage {
    // CompilerMessage(CompilerMessage),
    CompilerArtifact(CompilerArtifact),
    BuildFinished {
        success: bool,
    },
    #[serde(other)]
    Other,
}

// #[derive(Debug, Deserialize)]
// struct CompilerMessage {
//     package_id: PathBuf,
//     manifest_path: PathBuf,
//     message: Diagnostic,
// }

#[derive(Debug, Deserialize)]
struct CompilerArtifact {
    // package_id: PathBuf,
    // manifest_path: PathBuf,
    target: Target,
    filenames: Vec<PathBuf>,
}

#[derive(Debug, Deserialize)]
struct Target {
    kind: Vec<TargetKind>,
}

#[derive(Debug, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "kebab-case")]
enum TargetKind {
    Bin,
    Lib,
    Rlib,
    Dylib,
    Cdylib,
    Example,
    Test,
    Bench,
    CustomBuild,
    #[serde(other)]
    Other,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "kebab-case")]
enum Severity {
    Error,
    Warming,
    Note,
    Help,
    FailureNode,
    #[serde(rename = "error: internal compiler error")]
    InternalError,
}
