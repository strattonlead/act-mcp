#!/usr/bin/env Rscript

library(actdata)

cat("Column names of term_table:\n")
print(colnames(actdata::term_table))

cat("\nFirst 5 rows of term_table:\n")
print(head(actdata::term_table, 5))

cat("\nUnique values in 'dataset' column (if it exists):\n")
if ("dataset" %in% colnames(actdata::term_table)) {
    print(unique(actdata::term_table$dataset))
} else {
    cat("'dataset' column not found.\n")
}
