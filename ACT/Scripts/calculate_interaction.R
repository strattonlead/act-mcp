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
ensure_package("inteRact", github_repo = "ekmaloney/inteRact")
ensure_package("jsonlite")

# Get arguments: actor_term, behavior_term, object_term, gender,
#   [optional] prev_actor_e, prev_actor_p, prev_actor_a, prev_object_e, prev_object_p, prev_object_a
# When previous transient args are provided (for event chaining within a situation),
# they replace the fundamental actor/object EPAs in the impression formation equation inputs.
# Behavior always uses its fundamental EPA. Deflection is still computed against fundamentals.
args <- commandArgs(trailingOnly = TRUE)
actor_term <- if (length(args) >= 1) args[1] else "student"
behavior_term <- if (length(args) >= 2) args[2] else "request_something_from"
object_term <- if (length(args) >= 3) args[3] else "assistant"
target_gender <- if (length(args) >= 4) args[4] else "average"

# Optional: previous transient EPAs for chaining events within a situation
has_prev_transients <- (length(args) >= 10)
prev_actor_trans <- NULL
prev_object_trans <- NULL
if (has_prev_transients) {
    prev_actor_trans <- as.numeric(c(args[5], args[6], args[7]))
    prev_object_trans <- as.numeric(c(args[8], args[9], args[10]))
    if (any(is.na(prev_actor_trans)) || any(is.na(prev_object_trans))) {
        has_prev_transients <- FALSE
        prev_actor_trans <- NULL
        prev_object_trans <- NULL
    }
}

dictionary_key <- "germany2007"

# Helper to get EPA
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
be <- get_epa(behavior_term, "behavior", target_gender)
oe <- get_epa(object_term, "identity", target_gender)

result <- list(
  deflection = 0,
  transient_actor = c(0,0,0),
  transient_behavior = c(0,0,0),
  transient_object = c(0,0,0),
  actor_emotion = c(0,0,0),
  object_emotion = c(0,0,0),
  success = FALSE,
  error = ""
)

if (!is.null(ae) && !is.null(be) && !is.null(oe)) {
    tryCatch({
        # Load equations
        eqs <- tryCatch({
            actdata::get_eqn(dictionary_key, "impressionabo", target_gender)
        }, error = function(e) {
            return(NULL)
        })
        
        if (is.null(eqs)) {
             eqs <- actdata::get_eqn(dictionary_key, "impressionabo", "all")
        }
        
        # Extract coefficients
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
             coeffs <- eqs$df[[1]]
        }
        
        if (is.null(coeffs)) stop("Could not load coefficients.")

        # For event chaining: use previous transients as inputs if provided,
        # otherwise use fundamentals. Behavior always uses fundamental EPA.
        input_ae <- if (has_prev_transients) prev_actor_trans else ae
        input_oe <- if (has_prev_transients) prev_object_trans else oe
        inputs <- c(input_ae, be, input_oe)
        z_terms <- coeffs[, 1]
        design_row <- numeric(length(z_terms))
        
        for (i in 1:length(z_terms)) {
            z <- z_terms[i]
            code <- substr(z, 2, nchar(z))
            val <- 1
            if (code != "000000000" && code != "000000") {
                chars <- strsplit(code, "")[[1]]
                for (j in 1:length(chars)) {
                    if (chars[j] == "1") {
                        if (j <= length(inputs)) val <- val * inputs[j]
                    }
                }
            }
            design_row[i] <- val
        }
        
        coeff_mat <- as.matrix(coeffs[, 2:ncol(coeffs)])
        transients <- as.vector(design_row %*% coeff_mat)
        
        t_ae <- transients[1:3]
        t_be <- transients[4:6]
        t_oe <- transients[7:9]
        
        sq_diff_a <- sum((ae - t_ae)^2)
        sq_diff_b <- sum((be - t_be)^2)
        sq_diff_o <- sum((oe - t_oe)^2)
        
        total_deflection <- sq_diff_a + sq_diff_b + sq_diff_o
        
        result$deflection <- total_deflection
        result$transient_actor <- t_ae 
        result$transient_behavior <- t_be
        result$transient_object <- t_oe
        
        # --- Actor Emotions ---
        
        emo_eqs <- tryCatch({
            actdata::get_eqn(dictionary_key, "emotionid", target_gender)
        }, error=function(e) NULL)
        
        emo_coeffs <- NULL
        if (!is.null(emo_eqs)) {
             if ("df" %in% names(emo_eqs) && length(emo_eqs$df) >= 1) emo_coeffs <- emo_eqs$df[[1]]
             else if (nrow(emo_eqs) > 0 && !("df" %in% names(emo_eqs))) emo_coeffs <- emo_eqs
        }
        
        if (is.null(emo_coeffs)) {
             emo_eqs <- tryCatch(actdata::get_eqn(dictionary_key, "emotionid", "all"), error=function(e) NULL)
             if (!is.null(emo_eqs)) {
                 if ("df" %in% names(emo_eqs)) {
                     emo_coeffs <- emo_eqs$df[[1]]
                 } else {
                     emo_coeffs <- emo_eqs
                 }
             }
        }
        
        if (!is.null(emo_coeffs)) {
            # Predict Emotion
            predict_emo <- function(fund, trans) {
                in_vec <- c(fund, trans)
                z_e <- emo_coeffs[, 1]
                d_r <- numeric(length(z_e))
                for (k in 1:length(z_e)) {
                    z <- z_e[k]
                    cd <- substr(z, 2, nchar(z))
                    v <- 1
                    if (cd != "000000") {
                        ch <- strsplit(cd, "")[[1]]
                        for (m in 1:length(ch)) {
                            if (ch[m] == "1" && m <= length(in_vec)) v <- v * in_vec[m]
                        }
                    }
                    d_r[k] <- v
                }
                coef_m <- as.matrix(emo_coeffs[, 2:ncol(emo_coeffs)])
                return(as.vector(d_r %*% coef_m))
            }
            
            result$actor_emotion <- predict_emo(ae, t_ae)
            result$object_emotion <- predict_emo(oe, t_oe)
        } 
        
        result$success <- TRUE
        
    }, error = function(e) {
        result$error <- paste("R Error:", conditionMessage(e))
    })
} else {
    result$error <- "Could not find EPA values."
}

cat(jsonlite::toJSON(result, auto_unbox = TRUE))
