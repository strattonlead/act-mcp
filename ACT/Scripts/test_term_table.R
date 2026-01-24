#!/usr/bin/env Rscript

library(actdata)

key <- "germany2007"
cat(paste("Attempting to get term table for:", key, "\n"))

df <- tryCatch({
    # term_table seems to take a dictionary or a key
    actdata::term_table(key)
}, error = function(e) {
    cat("Error calling term_table:", e$message, "\n")
    NULL
})

if (!is.null(df)) {
    cat("Data frame returned by term_table. Structure:\n")
    str(df)
    cat("\nFirst few rows:\n")
    print(head(df))
    
    # Check if we can get just identities (terms)
    if ("term" %in% names(df)) {
        cat("\nFound 'term' column.\n")
    }
}
