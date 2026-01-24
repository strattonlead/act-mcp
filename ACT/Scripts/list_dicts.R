#!/usr/bin/env Rscript

ensure_package <- function(pkg, github_repo = NULL) {
  if (!requireNamespace(pkg, quietly = TRUE)) {
    if (!is.null(github_repo)) {
      if (!requireNamespace("remotes", quietly = TRUE)) install.packages("remotes", repos = "https://cloud.r-project.org")
      remotes::install_github(github_repo)
    } else {
      install.packages(pkg, repos = "https://cloud.r-project.org")
    }
  }
  library(pkg, character.only = TRUE)
}

ensure_package("actdata", github_repo = "ahcombs/actdata")

dicts_list <- actdata::get_dicts()
for (d in dicts_list) {
  cat(paste("Key:", slot(d, "key"), "\n"))
  cat(paste("Description:", slot(d, "description"), "\n"))
  cat("--------------------------------------------------\n")
}
