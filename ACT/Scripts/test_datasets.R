#!/usr/bin/env Rscript

library(actdata)

cat("Checking if 'germany2007' is a dataset:\n")
if (exists("germany2007")) {
    cat("Yes! 'germany2007' exists.\n")
    print(class(germany2007))
    print(head(germany2007))
} else {
    cat("No 'germany2007' object found directly.\n")
}

cat("\nChecking 'term_table' class:\n")
if (exists("term_table")) {
    print(class(term_table))
}

cat("\nChecking 'epa_subset' function capabilities:\n")
# epa_subset(dataset, ...)
# Maybe pass the string "germany2007"?
