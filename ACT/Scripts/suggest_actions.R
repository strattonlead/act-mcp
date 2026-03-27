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

# Helper to extract coefficients from equation object
extract_coeffs <- function(eqs) {
    if (is.null(eqs)) return(NULL)
    if (is.data.frame(eqs)) {
        if ("df" %in% names(eqs) && length(eqs$df) >= 1) return(eqs$df[[1]])
        if (!("df" %in% names(eqs))) return(eqs)
    }
    return(NULL)
}

ae <- get_epa(actor_term, "identity", target_gender)
oe <- get_epa(object_term, "identity", target_gender)

if (is.null(ae) || is.null(oe)) {
    cat(jsonlite::toJSON(list(error = "Could not find EPA for actor or object"), auto_unbox = TRUE))
    quit()
}

# 2. Get All Behaviors
all_behaviors <- actdata::epa_subset(expr = ".*", exactmatch = FALSE, dataset = dictionary_key, component = "behavior", group = target_gender)

if (is.null(all_behaviors) || nrow(all_behaviors) == 0) {
    all_behaviors <- actdata::epa_subset(expr = ".*", exactmatch = FALSE, dataset = dictionary_key, component = "behavior", group = "all")
}

if (is.null(all_behaviors) || nrow(all_behaviors) == 0) {
     cat(jsonlite::toJSON(list(error = "No behaviors found in dictionary"), auto_unbox = TRUE))
     quit()
}

# 3. Load ABOS equations (preferred, matches Interact program) with ABO fallback
coeffs <- NULL
is_abos <- FALSE

for (g in c(target_gender, "male", "female", "all", "average")) {
    eqs <- tryCatch(actdata::get_eqn(dictionary_key, "impressionabos", g), error = function(e) NULL)
    coeffs <- extract_coeffs(eqs)
    if (!is.null(coeffs)) { is_abos <- TRUE; break }
}

if (is.null(coeffs)) {
    for (g in c(target_gender, "all", "average")) {
        eqs <- tryCatch(actdata::get_eqn(dictionary_key, "impressionabo", g), error = function(e) NULL)
        coeffs <- extract_coeffs(eqs)
        if (!is.null(coeffs)) break
    }
}

if (is.null(coeffs)) {
    cat(jsonlite::toJSON(list(error = "Could not load equations"), auto_unbox = TRUE))
    quit()
}

z_terms <- as.character(coeffs[, 1])
coeff_mat <- as.matrix(coeffs[, 2:ncol(coeffs)])
n_terms <- nrow(coeffs)
n_out <- ncol(coeffs) - 1

# Function to calculate deflection using ABOS or ABO equations
calc_deflection <- function(be_vec) {
    if (is_abos) {
        setting <- c(0, 0, 0)
        inputs <- c(ae, be_vec, oe, setting)
    } else {
        inputs <- c(ae, be_vec, oe)
    }

    # Build design row (matching Interact's MathModel.impressions())
    t_arr <- numeric(n_terms)
    t_arr[1] <- 1.0
    for (slot in 0:(n_out/3 - 1)) {
        for (epa in 0:2) {
            idx <- 3*slot + epa + 2
            if (idx <= n_terms) t_arr[idx] <- inputs[slot*3 + epa + 1]
        }
    }
    for (i in (n_out + 2):n_terms) {
        if (i > n_terms) break
        code <- substr(z_terms[i], 2, nchar(z_terms[i]))
        chars <- strsplit(code, "")[[1]]
        val <- 1.0
        for (col in 1:length(chars)) {
            if (chars[col] == "1") val <- val * t_arr[col + 1]
        }
        t_arr[i] <- val
    }

    tau <- numeric(n_out)
    for (col in 1:n_out) {
        s <- 0
        for (i in 1:n_terms) s <- s + t_arr[i] * coeff_mat[i, col]
        tau[col] <- s
    }

    t_ae <- tau[1:3]; t_be <- tau[4:6]; t_oe <- tau[7:9]
    defl <- sum((ae - t_ae)^2) + sum((be_vec - t_be)^2) + sum((oe - t_oe)^2)

    if (is_abos && length(tau) >= 12) {
        t_se <- tau[10:12]
        defl <- defl + sum(t_se^2)  # setting fundamental = [0,0,0]
    }

    return(defl)
}

results <- list()

for (i in 1:nrow(all_behaviors)) {
    term <- all_behaviors$term[i]
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

# Remove nulls and sort by deflection
results <- results[!sapply(results, is.null)]
if (length(results) > 0) {
    deflections <- sapply(results, function(x) x$deflection)
    results <- results[order(deflections)]
}

# Limit to top 50
top_n <- head(results, 50)

cat(jsonlite::toJSON(top_n, auto_unbox = TRUE))
