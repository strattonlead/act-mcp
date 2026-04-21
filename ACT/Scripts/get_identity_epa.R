#!/usr/bin/env Rscript

# Returns the fundamental EPA of a single identity in a dictionary as JSON.
# Args: <dictionary_key> <identity_term> [<gender>]
# Gender defaults to "average" to match the calculate_interaction.R convention.
# Output: {"e":<num>,"p":<num>,"a":<num>}  or  {"error":"..."} on failure.

args <- commandArgs(trailingOnly = TRUE)
if (length(args) < 2) {
  cat('{"error":"dictionary_key and identity_term required"}')
  quit(status = 0)
}

dict_key <- args[1]
identity_term <- args[2]
target_gender <- if (length(args) >= 3) args[3] else "average"

if (!requireNamespace("actdata", quietly = TRUE)) {
  cat('{"error":"actdata package missing"}')
  quit(status = 0)
}
if (!requireNamespace("jsonlite", quietly = TRUE)) {
  install.packages("jsonlite", repos = "https://cloud.r-project.org")
}

suppressPackageStartupMessages({
  library(actdata)
  library(jsonlite)
})

result <- tryCatch({
  # Try several gender fallbacks in the same spirit as calculate_interaction.R
  res <- NULL
  for (g in unique(c(target_gender, "average", "all", "male", "female"))) {
    res <- suppressWarnings(
      actdata::epa_subset(
        expr = identity_term,
        exactmatch = TRUE,
        dataset = dict_key,
        component = "identity",
        group = g
      )
    )
    if (!is.null(res) && nrow(res) > 0) break
  }

  if (is.null(res) || nrow(res) == 0) {
    list(error = paste0("Identity '", identity_term, "' not found in dictionary '", dict_key, "'"))
  } else if (all(c("E", "P", "A") %in% names(res))) {
    list(e = as.numeric(res$E[1]), p = as.numeric(res$P[1]), a = as.numeric(res$A[1]))
  } else {
    list(
      e = as.numeric(res[1, 1]),
      p = as.numeric(res[1, 2]),
      a = as.numeric(res[1, 3])
    )
  }
}, error = function(e) {
  list(error = paste("R Error:", conditionMessage(e)))
})

cat(toJSON(result, auto_unbox = TRUE))
