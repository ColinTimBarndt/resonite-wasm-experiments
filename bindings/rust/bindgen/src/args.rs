use std::path::PathBuf;

#[derive(clap::Parser, Debug)]
pub struct BuildArgs {
    pub imports: PathBuf,
    #[arg(short, long)]
    pub output: PathBuf,
}
