#!/usr/bin/env Rscript
# Unit test: Compare our calculation against real Interact program output
# Uses expert-annotated data from Chat 01, germany2007, average gender

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

dictionary_key <- "germany2007"
target_gender <- "male"  # Use male sentiments (Interact Person 1 = male)

# ---- Helper functions (same as calculate_interaction.R) ----

get_epa <- function(term, component, gender="all") {
  tryCatch({
    res <- suppressWarnings(actdata::epa_subset(expr = term, exactmatch = TRUE, dataset = dictionary_key, component = component, group = gender))
    if (!is.null(res) && nrow(res) > 0) {
      if ("E" %in% names(res)) return(as.numeric(c(res$E[1], res$P[1], res$A[1])))
      return(as.numeric(c(res[1,1], res[1,2], res[1,3])))
    }
  }, error = function(e) return(NULL))
  return(NULL)
}

load_coefficients <- function() {
  eqs <- tryCatch(actdata::get_eqn(dictionary_key, "impressionabo", target_gender), error = function(e) NULL)
  if (is.null(eqs)) eqs <- actdata::get_eqn(dictionary_key, "impressionabo", "all")
  coeffs <- NULL
  if (is.data.frame(eqs)) {
    if ("df" %in% names(eqs) && length(eqs$df) >= 1) coeffs <- eqs$df[[1]]
    else if (!("df" %in% names(eqs))) coeffs <- eqs
  }
  if (is.null(coeffs)) {
    eqs <- actdata::get_eqn(dictionary_key, "impressionabo", "all")
    coeffs <- eqs$df[[1]]
  }
  return(coeffs)
}

calc_event <- function(actor_identity, behavior, object_identity, coeffs,
                       prev_actor_trans = NULL, prev_object_trans = NULL) {
  ae <- get_epa(actor_identity, "identity", target_gender)
  be <- get_epa(behavior, "behavior", target_gender)
  oe <- get_epa(object_identity, "identity", target_gender)

  if (is.null(ae) || is.null(be) || is.null(oe)) {
    stop(sprintf("EPA not found: actor=%s(%s) behavior=%s(%s) object=%s(%s)",
                 actor_identity, paste(ae, collapse=","),
                 behavior, paste(be, collapse=","),
                 object_identity, paste(oe, collapse=",")))
  }

  # Use previous transients if provided (chaining), otherwise fundamentals
  input_ae <- if (!is.null(prev_actor_trans)) prev_actor_trans else ae
  input_oe <- if (!is.null(prev_object_trans)) prev_object_trans else oe
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

  # Deflection = sum of squared differences (fundamentals vs outcome transients)
  sq_diff_a <- sum((ae - t_ae)^2)
  sq_diff_b <- sum((be - t_be)^2)
  sq_diff_o <- sum((oe - t_oe)^2)
  deflection <- sq_diff_a + sq_diff_b + sq_diff_o

  return(list(
    actor_epa = t_ae,
    behavior_epa = t_be,
    object_epa = t_oe,
    deflection = deflection,
    fund_actor = ae,
    fund_behavior = be,
    fund_object = oe
  ))
}

# ---- Test data from expert annotations (Interact program output) ----
# Chat 01, Expert 1, germany2007, identities: student & assistant

test_situations <- list(
  list(
    name = "Situation 1: Verneinung der Prüfungsangst",
    events = list(
      list(actor = "student", behavior = "deny_something_to", object = "assistant",
           expected_actor_epa = c(-1.19, 2.28, 1.30),
           expected_object_epa = c(0.40, -1.90, 0.69),
           expected_deflection = 7.5),
      list(actor = "assistant", behavior = "agree_with", object = "student",
           expected_actor_epa = c(1.09, 1.10, 0.07),
           expected_object_epa = c(-0.78, 1.87, 0.81),
           expected_deflection = 8.1)
    )
  ),
  list(
    name = "Situation 2: Erklärung der eigenen Entspannung",
    events = list(
      list(actor = "student", behavior = "explain_something_to", object = "assistant",
           expected_actor_epa = c(2.17, 2.27, 0.42),
           expected_object_epa = c(1.60, -0.09, 0.80),
           expected_deflection = 2.0),
      list(actor = "assistant", behavior = "reply_to", object = "student",
           expected_actor_epa = c(2.46, 2.35, 0.58),
           expected_object_epa = c(1.42, 1.31, 0.37),
           expected_deflection = 2.4)
    )
  ),
  list(
    name = "Situation 3: Teilen der eigenen Philosophie zum Thema Stress",
    events = list(
      list(actor = "student", behavior = "explain_something_to", object = "assistant",
           expected_actor_epa = c(2.17, 2.27, 0.42),
           expected_object_epa = c(1.60, -0.09, 0.80),
           expected_deflection = 2.0),
      list(actor = "assistant", behavior = "ask_about_something", object = "student",
           expected_actor_epa = c(2.49, 1.84, 0.60),
           expected_object_epa = c(1.44, 1.42, 0.37),
           expected_deflection = 2.0)
    )
  ),
  list(
    name = "Situation 4: Strategien für Gelassenheit",
    events = list(
      list(actor = "student", behavior = "reply_to", object = "assistant",
           expected_actor_epa = c(1.83, 1.46, 0.63),
           expected_object_epa = c(1.48, -0.20, 0.79),
           expected_deflection = 1.1),
      list(actor = "assistant", behavior = "recommend_something_to", object = "student",
           expected_actor_epa = c(2.24, 0.97, 0.26),
           expected_object_epa = c(1.22, 1.02, 0.51),
           expected_deflection = 1.7)
    )
  ),
  list(
    name = "Situation 5: Anbieten von Entspannungstechniken",
    events = list(
      list(actor = "assistant", behavior = "recommend_something_to", object = "student",
           expected_actor_epa = c(2.35, 0.65, 0.21),
           expected_object_epa = c(1.45, -0.08, 0.77),
           expected_deflection = 1.4),
      list(actor = "student", behavior = "reply_to", object = "assistant",
           expected_actor_epa = c(1.63, 1.59, 0.46),
           expected_object_epa = c(1.95, 0.68, 0.45),
           expected_deflection = 1.4)
    )
  ),
  list(
    name = "Situation 7: Frage und Erklären der Aufgabe",
    events = list(
      list(actor = "student", behavior = "ask_about_something", object = "assistant",
           expected_actor_epa = c(1.82, 0.97, 0.64),
           expected_object_epa = c(1.48, -0.16, 0.79),
           expected_deflection = 0.9),
      list(actor = "assistant", behavior = "reply_to", object = "student",
           expected_actor_epa = c(2.30, 2.26, 0.56),
           expected_object_epa = c(1.20, 0.49, 0.54),
           expected_deflection = 2.4)
    )
  )
)

# ---- Run tests ----
coeffs <- load_coefficients()

cat("=== ACT Calculation Verification against Interact Program ===\n")
cat(sprintf("Dictionary: %s, Gender: %s\n\n", dictionary_key, target_gender))

# Show fundamental EPAs for reference
student_fund <- get_epa("student", "identity", target_gender)
assistant_fund <- get_epa("assistant", "identity", target_gender)
cat(sprintf("Fundamental EPAs:\n"))
cat(sprintf("  student:   [%.2f, %.2f, %.2f]\n", student_fund[1], student_fund[2], student_fund[3]))
cat(sprintf("  assistant: [%.2f, %.2f, %.2f]\n\n", assistant_fund[1], assistant_fund[2], assistant_fund[3]))

total_tests <- 0
passed_tests <- 0
tolerance <- 0.15  # Allow small rounding differences

for (sit in test_situations) {
  cat(sprintf("--- %s ---\n", sit$name))

  # Track transients by identity for chaining
  transients_by_identity <- list()

  for (i in seq_along(sit$events)) {
    evt <- sit$events[[i]]
    total_tests <- total_tests + 1

    # Look up previous transients by identity (handles role swaps!)
    prev_actor <- transients_by_identity[[evt$actor]]
    prev_object <- transients_by_identity[[evt$object]]

    result <- tryCatch(
      calc_event(evt$actor, evt$behavior, evt$object, coeffs, prev_actor, prev_object),
      error = function(e) { cat(sprintf("  ERROR: %s\n", e$message)); NULL }
    )

    if (is.null(result)) next

    # Update transient map by identity
    transients_by_identity[[evt$actor]] <- result$actor_epa
    transients_by_identity[[evt$object]] <- result$object_epa

    # Compare
    actor_diff <- max(abs(result$actor_epa - evt$expected_actor_epa))
    object_diff <- max(abs(result$object_epa - evt$expected_object_epa))
    defl_diff <- abs(result$deflection - evt$expected_deflection)

    actor_ok <- actor_diff < tolerance
    object_ok <- object_diff < tolerance
    defl_ok <- defl_diff < tolerance

    all_ok <- actor_ok && object_ok && defl_ok
    if (all_ok) passed_tests <- passed_tests + 1

    status <- if (all_ok) "PASS" else "FAIL"

    cat(sprintf("  E%d: %s %s %s => %s\n", i, evt$actor, evt$behavior, evt$object, status))
    cat(sprintf("    Actor EPA:  got [%.2f, %.2f, %.2f]  expected [%.2f, %.2f, %.2f]  diff=%.3f %s\n",
                result$actor_epa[1], result$actor_epa[2], result$actor_epa[3],
                evt$expected_actor_epa[1], evt$expected_actor_epa[2], evt$expected_actor_epa[3],
                actor_diff, if (actor_ok) "OK" else "MISMATCH"))
    cat(sprintf("    Object EPA: got [%.2f, %.2f, %.2f]  expected [%.2f, %.2f, %.2f]  diff=%.3f %s\n",
                result$object_epa[1], result$object_epa[2], result$object_epa[3],
                evt$expected_object_epa[1], evt$expected_object_epa[2], evt$expected_object_epa[3],
                object_diff, if (object_ok) "OK" else "MISMATCH"))
    cat(sprintf("    Deflection: got %.2f  expected %.1f  diff=%.3f %s\n",
                result$deflection, evt$expected_deflection, defl_diff, if (defl_ok) "OK" else "MISMATCH"))
  }
  cat("\n")
}

cat(sprintf("=== RESULTS: %d/%d tests passed (tolerance=%.2f) ===\n", passed_tests, total_tests, tolerance))
if (passed_tests == total_tests) {
  cat("ALL TESTS PASSED!\n")
} else {
  cat(sprintf("WARNING: %d tests FAILED\n", total_tests - passed_tests))
}
