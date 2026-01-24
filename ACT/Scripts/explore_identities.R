#!/usr/bin/env Rscript

library(actdata)

# Test if we can get EPA data or terms for a dictionary
key <- "germany2007"
cat(paste("Attempting to get data for:", key, "\n"))

# Check for get_epa function
if (exists("get_epa", where = asNamespace("actdata"), mode = "function")) {
    cat("get_epa exists.\n")
    df <- tryCatch({
        actdata::get_epa(key)
    }, error = function(e) {
        cat("Error calling get_epa:", e$message, "\n")
        NULL
    })
    
    if (!is.null(df)) {
        cat("Data frame returned by get_epa. Structure:\n")
        str(df)
        cat("\nFirst few rows:\n")
        print(head(df))
    }
} else {
    cat("get_epa does NOT exist in actdata.\n")
    
    # Check if there is a 'get_dataset' or similar
    ls_funcs <- ls("package:actdata")
    cat("Functions in actdata:\n")
    print(ls_funcs)
}
