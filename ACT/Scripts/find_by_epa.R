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
  suppressPackageStartupMessages(library(pkg, character.only = TRUE))
}

ensure_package("actdata", github_repo = "ahcombs/actdata")

args <- commandArgs(trailingOnly = TRUE)
# Args: E P A component gender
target_e <- as.numeric(args[1])
target_p <- as.numeric(args[2])
target_a <- as.numeric(args[3])
comp <- args[4]
gen <- args[5]

cat(paste("Searching for EPA:", target_e, target_p, target_a, "in", comp, "group", gen, "\n"))

# Get all items
df <- actdata::epa_subset(expr = ".*", exactmatch = FALSE, dataset = "germany2007", component = comp, group = gen)

if (!is.null(df)) {
    # Calculate distance
    # Assuming columns E, P, A
    
    # If returned structure has E, P, A columns?
    df$dist <- sqrt( (df$E - target_e)^2 + (df$P - target_p)^2 + (df$A - target_a)^2 )
    
    # Sort
    df_sorted <- df[order(df$dist), ]
    
    print(head(df_sorted[, c("term", "E", "P", "A", "dist")], 10))
    
} else {
    print("No data found")
}
