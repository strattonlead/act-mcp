#!/usr/bin/env Rscript

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

# Args: actor_term, object_term, dictionary_key, gender
args <- commandArgs(trailingOnly = TRUE)
actor_term <- if (length(args) >= 1) args[1] else "student"
object_term <- if (length(args) >= 2) args[2] else "instructor"
dictionary_key <- if (length(args) >= 3) args[3] else "germany2007"
target_gender <- if (length(args) >= 4) args[4] else "male"

# 1. Get Actor and Object EPAs
get_epa <- function(term, component, gender="all") {
  tryCatch({
    res <- suppressWarnings(actdata::epa_subset(expr = term, exactmatch = TRUE, dataset = dictionary_key, component = component, group = gender))
    if (!is.null(res) && nrow(res) > 0) {
      if ("E" %in% names(res)) {
          return(as.numeric(c(res$E[1], res$P[1], res$A[1])))
      }
      return(as.numeric(c(res[1,1], res[1,2], res[1,3]))) 
    }
  }, error = function(e) return(NULL))
  return(NULL)
}

ae <- get_epa(actor_term, "identity", target_gender)
oe <- get_epa(object_term, "identity", target_gender)

result_list <- list()

if (is.null(ae) || is.null(oe)) {
    cat(jsonlite::toJSON(list(error = "Could not find EPA for actor or object"), auto_unbox = TRUE))
    quit()
}

# 2. Get All Behaviors
all_behaviors <- actdata::epa_subset(expr = ".*", exactmatch = FALSE, dataset = dictionary_key, component = "behavior", group = target_gender)

if (is.null(all_behaviors) || nrow(all_behaviors) == 0) {
    # Fallback to 'all' gender if specific gender empty
    all_behaviors <- actdata::epa_subset(expr = ".*", exactmatch = FALSE, dataset = dictionary_key, component = "behavior", group = "all")
}

if (is.null(all_behaviors) || nrow(all_behaviors) == 0) {
     cat(jsonlite::toJSON(list(error = "No behaviors found in dictionary"), auto_unbox = TRUE))
     quit()
}

# 3. Load Equations
eqs <- tryCatch({
    actdata::get_eqn(dictionary_key, "impressionabo", target_gender)
}, error = function(e) return(NULL))

if (is.null(eqs)) {
     eqs <- actdata::get_eqn(dictionary_key, "impressionabo", "all")
}

coeffs <- NULL
if (is.data.frame(eqs)) {
     if ("df" %in% names(eqs) && length(eqs$df) >= 1) {
         coeffs <- eqs$df[[1]]
     } else {
         if (!("df" %in% names(eqs))) coeffs <- eqs
     }
}

if (is.null(coeffs)) {
     # Fallback
     eqs <- actdata::get_eqn(dictionary_key, "impressionabo", "all")
     if (!is.null(eqs)) coeffs <- eqs$df[[1]]
}

if (is.null(coeffs)) {
    cat(jsonlite::toJSON(list(error = "Could not load equations"), auto_unbox = TRUE))
    quit()
}

# Pre-process coefficients for speed
# Standard Impression Equations usually have 9 dependent variables (Ae, Ap, Aa, Be, Bp, Ba, Oe, Op, Oa)
# We want to form a matrix operation if possible, but iteration is safer for correctness first.

coeff_mat <- as.matrix(coeffs[, 2:ncol(coeffs)])
z_terms <- coeffs[, 1]

# Function to calculate deflection
calc_deflection <- function(be_vec) {
    inputs <- c(ae, be_vec, oe)
    
    # Construct Design Row
    # This part is the bottleneck if looped. 
    # Can we optimize? 
    # Most terms are like: 1, A_e, A_p, A_a, B_e, ... A_e*B_e ... 
    # We will just implement the loop for now. R is fast enough for ~1000 items.
    
    design_row <- numeric(length(z_terms))
    for (i in 1:length(z_terms)) {
        z <- z_terms[i]
        code <- substr(z, 2, nchar(z))
        val <- 1
        if (code != "000000000" && code != "000000") {
            chars <- strsplit(code, "")[[1]]
            # Optimization: We only need to multiply non-1s? No, logic is positional.
            for (j in 1:length(chars)) {
                if (chars[j] == "1") {
                    if (j <= length(inputs)) val <- val * inputs[j]
                }
            }
        }
        design_row[i] <- val
    }
    
    transients <- as.vector(design_row %*% coeff_mat)
    
    t_ae <- transients[1:3]
    t_be <- transients[4:6]
    t_oe <- transients[7:9]
    
    sq_diff_a <- sum((ae - t_ae)^2)
    sq_diff_b <- sum((be_vec - t_be)^2)
    sq_diff_o <- sum((oe - t_oe)^2)
    
    return(sq_diff_a + sq_diff_b + sq_diff_o)
}

results <- list()
count <- 0

# Loop through behaviors
# Ensure columns E, P, A exist
if (!all(c("E", "P", "A") %in% names(all_behaviors))) {
    # maybe unnamed? usually actdata returns data frame with E, P, A
    # If not, try by index 2,3,4? (1 is term)
    # actdata usually: term, E, P, A, ...
}

for (i in 1:nrow(all_behaviors)) {
    term <- all_behaviors$term[i]
    # Check if term is valid
    if (is.na(term)) next
    
    be_vec <- c(all_behaviors$E[i], all_behaviors$P[i], all_behaviors$A[i])
    if (any(is.na(be_vec))) next
    
    def <- calc_deflection(be_vec)
    
    results[[i]] <- list(
        term = term,
        deflection = def,
        epa = be_vec
    )
}

# Remove nulls
results <- results[!sapply(results, is.null)]

# Sort by deflection directly on the list
if (length(results) > 0) {
    deflections <- sapply(results, function(x) x$deflection)
    results <- results[order(deflections)]
}

# Limit to top 50
top_n <- head(results, 50)

cat(jsonlite::toJSON(top_n, auto_unbox = TRUE))
