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
    # It might be that the key passed doesn't match the column name exactly (e.g. key vs filename)
    # But usually actdata keys match these columns.
    # Return empty list or error
    cat("[]")
    quit(status=0)
}

# Filter
# We select 'term' where component == 'identity' and the specific dictionary column == 1
# dynamically select the column using .data[[dict_key]] or standard subsetting
identities <- tt %>%
  filter(component == "identity") %>%
  filter(.data[[dict_key]] == 1) %>%
  pull(term)

# Sort alphabetically
identities <- sort(identities)

# Output JSON
cat(toJSON(identities, auto_unbox = TRUE))
