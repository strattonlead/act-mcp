#!/usr/bin/env Rscript

args <- commandArgs(trailingOnly = TRUE)
if (length(args) == 0) {
  stop("Dictionary key must be provided as an argument.")
}
dict_key <- args[1]

# Ensure packages
if (!requireNamespace("actdata", quietly = TRUE)) {
   stop("actdata package missing")
}
if (!requireNamespace("jsonlite", quietly = TRUE)) {
   install.packages("jsonlite", repos = "https://cloud.r-project.org")
}
suppressPackageStartupMessages({
  library(actdata)
  library(jsonlite)
  library(dplyr)
})

# Access term_table
tt <- actdata::term_table

# Check if key exists in columns
if (!dict_key %in% colnames(tt)) {
    cat("[]")
    quit(status=0)
}

# Filter for behaviors
# We select 'term' where component == 'behavior' and the specific dictionary column == 1
behaviors <- tt %>%
  filter(component == "behavior") %>%
  filter(.data[[dict_key]] == 1) %>%
  pull(term)

# Sort alphabetically
behaviors <- sort(behaviors)

# Output JSON
cat(toJSON(behaviors, auto_unbox = TRUE))
