use std::path::PathBuf;

#[derive(clap::Parser, Debug)]
pub enum RootArgs {
    #[command(about = "Builds a Rust project.")]
    Build(BuildArgs),
    #[command(about = "Weaves the specified WebAssembly file.")]
    Weave(WeaveArgs),
}

#[derive(clap::Args, Debug)]
pub struct BuildArgs {
    #[arg(short, long)]
    pub project: Option<String>,
    #[arg(short, long)]
    pub feature: Vec<String>,
    #[arg(short, long)]
    pub output: PathBuf,
    #[arg(last = true)]
    pub cargo_args: Vec<String>,
}

#[derive(clap::Args, Debug)]
pub struct WeaveArgs {
    pub input: PathBuf,
    #[arg(short, long)]
    pub output: PathBuf,
}
