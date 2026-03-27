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

# Helper to extract coefficients from equation object
extract_coeffs <- function(eqs) {
    if (is.null(eqs)) return(NULL)
    if (is.data.frame(eqs)) {
        if ("df" %in% names(eqs) && length(eqs$df) >= 1) return(eqs$df[[1]])
        if (!("df" %in% names(eqs))) return(eqs)
    }
    return(NULL)
}

# Helper to compute transients from inputs using impression formation equations.
# This mirrors the Interact Java program's MathModel.impressions() method exactly.
# Works for both ABO (9-col) and ABOS (12-col) equations.
compute_transients <- function(inputs, coeffs) {
    z_terms <- as.character(coeffs[, 1])
    n <- nrow(coeffs)
    n_out <- ncol(coeffs) - 1  # 9 for ABO, 12 for ABOS

    # Build design row matching Interact's algorithm:
    # t[0] = 1.0 (constant)
    # t[1..n_out] = input EPA values (main effects)
    # t[n_out+1..n-1] = interaction terms (products of main effects)
    t_arr <- numeric(n)
    t_arr[1] <- 1.0

    # Fill main effect slots
    for (slot in 0:(n_out/3 - 1)) {
        for (epa in 0:2) {
            idx <- 3*slot + epa + 2  # +2: R 1-indexed, t[1]=constant
            if (idx <= n) {
                t_arr[idx] <- inputs[slot*3 + epa + 1]
            }
        }
    }

    # Fill interaction terms
    for (i in (n_out + 2):n) {
        if (i > n) break
        code <- substr(z_terms[i], 2, nchar(z_terms[i]))
        chars <- strsplit(code, "")[[1]]
        val <- 1.0
        for (col in 1:length(chars)) {
            if (chars[col] == "1") {
                val <- val * t_arr[col + 1]
            }
        }
        t_arr[i] <- val
    }

    # Matrix multiplication: tau[col] = sum(t[i] * coef[i][col])
    coeff_mat <- as.matrix(coeffs[, 2:ncol(coeffs)])
    tau <- numeric(n_out)
    for (col in 1:n_out) {
        s <- 0
        for (i in 1:n) {
            s <- s + t_arr[i] * coeff_mat[i, col]
        }
        tau[col] <- s
    }

    return(tau)
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
        # Load ABOS equations (Actor-Behavior-Object-Setting) as the Interact program uses.
        # The Interact Java program always uses ABOS equations internally, even without a setting.
        # When no setting is selected, setting EPA = [0,0,0].
        # Fall back to ABO equations if ABOS not available.
        coeffs <- NULL
        is_abos <- FALSE

        # Try ABOS first (preferred - matches Interact program)
        for (g in c(target_gender, "male", "female", "all", "average")) {
            eqs <- tryCatch(actdata::get_eqn(dictionary_key, "impressionabos", g), error = function(e) NULL)
            coeffs <- extract_coeffs(eqs)
            if (!is.null(coeffs)) {
                is_abos <- TRUE
                break
            }
        }

        # Fall back to ABO if ABOS not available
        if (is.null(coeffs)) {
            for (g in c(target_gender, "all", "average")) {
                eqs <- tryCatch(actdata::get_eqn(dictionary_key, "impressionabo", g), error = function(e) NULL)
                coeffs <- extract_coeffs(eqs)
                if (!is.null(coeffs)) break
            }
        }

        if (is.null(coeffs)) stop("Could not load impression formation coefficients.")

        # For event chaining: use previous transients as inputs if provided,
        # otherwise use fundamentals. Behavior always uses fundamental EPA.
        input_ae <- if (has_prev_transients) prev_actor_trans else ae
        input_oe <- if (has_prev_transients) prev_object_trans else oe

        # Build input vector - ABOS adds setting EPA [0,0,0] when no setting is selected
        if (is_abos) {
            setting <- c(0, 0, 0)
            inputs <- c(input_ae, be, input_oe, setting)
        } else {
            inputs <- c(input_ae, be, input_oe)
        }

        # Compute transient impressions
        tau <- compute_transients(inputs, coeffs)

        t_ae <- tau[1:3]
        t_be <- tau[4:6]
        t_oe <- tau[7:9]

        # Deflection = sum of squared differences between fundamentals and transient outcomes.
        # For ABOS, this includes the setting component (fundamentals [0,0,0] vs setting transients).
        sq_diff_a <- sum((ae - t_ae)^2)
        sq_diff_b <- sum((be - t_be)^2)
        sq_diff_o <- sum((oe - t_oe)^2)
        total_deflection <- sq_diff_a + sq_diff_b + sq_diff_o

        if (is_abos && length(tau) >= 12) {
            t_se <- tau[10:12]
            setting_fund <- c(0, 0, 0)
            sq_diff_s <- sum((setting_fund - t_se)^2)
            total_deflection <- total_deflection + sq_diff_s
        }

        result$deflection <- total_deflection
        result$transient_actor <- t_ae
        result$transient_behavior <- t_be
        result$transient_object <- t_oe

        # --- Actor Emotions ---

        emo_coeffs <- NULL
        for (g in c(target_gender, "all", "average")) {
            emo_eqs <- tryCatch(actdata::get_eqn(dictionary_key, "emotionid", g), error = function(e) NULL)
            emo_coeffs <- extract_coeffs(emo_eqs)
            if (!is.null(emo_coeffs)) break
        }

        if (!is.null(emo_coeffs)) {
            # Predict Emotion from fundamental + transient identity EPAs
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
