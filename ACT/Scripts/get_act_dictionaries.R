#!/usr/bin/env Rscript

# Function to check and install packages
ensure_package <- function(pkg, github_repo = NULL) {
  if (!requireNamespace(pkg, quietly = TRUE)) {
    message(paste("Package", pkg, "not found. Attempting to install..."))
    if (!is.null(github_repo)) {
      if (!requireNamespace("remotes", quietly = TRUE)) {
        message("Installing 'remotes' package to install from GitHub...")
        install.packages("remotes", repos = "https://cloud.r-project.org")
      }
      message(paste("Installing", pkg, "from", github_repo, "..."))
      remotes::install_github(github_repo)
    } else {
      install.packages(pkg, repos = "https://cloud.r-project.org")
    }
  }
  
  tryCatch({
    library(pkg, character.only = TRUE)
    message(paste("Successfully loaded:", pkg))
  }, error = function(e) {
    stop(paste("Failed to load package:", pkg, "\nError:", e$message))
  })
}

# Ensure actdata is installed
# actdata contains the dictionaries for ACT, Interact, and BayesACT
# Reference: https://affectcontroltheory.org/resources-for-researchers/tools-and-software/act-related-r-packages/
ensure_package("actdata", github_repo = "ahcombs/actdata")

# Ensure jsonlite is installed for machine-readable output
if (!requireNamespace("jsonlite", quietly = TRUE)) {
  install.packages("jsonlite", repos = "https://cloud.r-project.org")
}

suppressPackageStartupMessages({
  library(actdata)
  library(jsonlite)
  library(methods) # Ensure S4 methods are available
})

# get_dicts() returns a list of S4 objects of class "dictionary"
dicts_list <- actdata::get_dicts()

# Helper function to convert S4 dictionary object to a plain list
dict_to_list <- function(d) {
  # These are the slots observed in the output
  list(
    key = slot(d, "key"),
    context = slot(d, "context"),
    year = slot(d, "year"),
    components = slot(d, "components"),
    stats = slot(d, "stats"),
    groups = slot(d, "groups"),
    individual = slot(d, "individual"),
    description = slot(d, "description"),
    source = slot(d, "source"),
    citation = slot(d, "citation"),
    notes = slot(d, "notes")
  )
}

# Convert all dictionary objects to a list of lists
dictionaries_data <- lapply(dicts_list, dict_to_list)

# Convert to JSON and print to stdout
# auto_unbox = TRUE makes single element vectors into scalars (e.g. "key": "value" instead of "key": ["value"])
json_output <- toJSON(dictionaries_data, pretty = TRUE, auto_unbox = TRUE)
cat(json_output)
