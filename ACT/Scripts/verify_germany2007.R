#!/usr/bin/env Rscript

# Function to check and install packages
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
ensure_package("jsonlite")

# Get arguments: term, component
args <- commandArgs(trailingOnly = TRUE)
target_term <- "student"
target_component <- "identity"

if (length(args) >= 1) target_term <- args[1]
if (length(args) >= 2) target_component <- args[2]

# Helper to find term
find_term <- function(term, component) {
  tryCatch({
    suppressWarnings(
      actdata::epa_subset(expr = term, exactmatch = TRUE, dataset = "germany2007", component = component)
    )
  }, error = function(e) NULL)
}

result_df <- find_term(target_term, target_component)

# Debug prints (to stderr)
if (is.null(result_df)) {
    message("result_df is NULL")
} else {
    message(paste("result_df class:", paste(class(result_df), collapse=", ")))
    message(paste("result_df nrow:", nrow(result_df)))
}

found <- FALSE
data_list <- list()

if (!is.null(result_df)) {
    # Separate check
    if (length(nrow(result_df)) > 0 && !is.na(nrow(result_df)) && nrow(result_df) > 0) {
        found <- TRUE
        entry <- result_df[1, ]
        data_list <- as.list(entry)
    }
}

if (!found) {
    message(paste("Term '", target_term, "' not found in component '", target_component, "'.", sep=""))
    
    tryCatch({
         all_items <- suppressWarnings(actdata::epa_subset(expr = ".*", exactmatch = FALSE, dataset = "germany2007", component = target_component))
         if (!is.null(all_items) && is.data.frame(all_items)) {
            message(paste("Columns:", paste(colnames(all_items), collapse=", ")))
            
            if ("term" %in% colnames(all_items)) {
                terms <- all_items$term
                matches <- grep(target_term, terms, ignore.case = TRUE, value = TRUE)
                if (length(matches) > 0) {
                    message("Possible matches found in 'term' column:")
                    message(paste(head(matches, 10), collapse=", "))
                } else {
                    message("No similar matches found in 'term' column.")
                    message(paste("Top terms:", paste(head(terms, 10), collapse=", ")))
                }
            } else {
                 message("Top row names:")
                 print(head(rownames(all_items), 10))
            }
         }
    }, error = function(e) message("Could not list items."))
}

output <- list(
  dictionary = "germany2007",
  term = target_term,
  component = target_component,
  found = found,
  data = data_list
)

json_output <- jsonlite::toJSON(output, pretty = TRUE, auto_unbox = TRUE)
cat(json_output)
