#!/usr/bin/env Rscript

if (!requireNamespace("actdata", quietly = TRUE)) {
  if (!requireNamespace("remotes", quietly = TRUE)) install.packages("remotes", repos = "https://cloud.r-project.org")
  remotes::install_github("ahcombs/actdata")
}
library(actdata)

# Try to look at the structure of a single dictionary object to find identities/concepts
dicts <- actdata::get_dicts()
if (length(dicts) > 0) {
    d <- dicts[[1]]
    cat("Class of dictionary object:\n")
    print(class(d))
    cat("\nSlots:\n")
    print(slotNames(d))
    
    # Usually identities are stored in specific slots or methods.
    # Let's try to see if there is a slot for entries, terms, or identities.
    # Based on previous file reads, we saw slots like 'components', 'groups', 'stats'.
    # We didn't see 'identities' explicitly listed in the previous JSON output script, 
    # but let's check the slots again carefully.
}
