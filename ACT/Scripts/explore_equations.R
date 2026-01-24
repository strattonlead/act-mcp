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
  library(pkg, character.only = TRUE)
}

ensure_package("actdata", github_repo = "ahcombs/actdata")

# Inspect equations functions
print("Args for get_eqn:")
print(args(actdata::get_eqn))

print("Structure of equations dataset (if available):")
# Assuming 'equations' might be a dataset or function
tryCatch({
    print(class(actdata::equations))
    if ("data.frame" %in% class(actdata::equations)) {
        print(head(actdata::equations))
    }
}, error = function(e) print(e))

# Check for inteRact package
ensure_package("inteRact", github_repo = "ekmaloney/inteRact")

print("Available in inteRact:")
print(ls("package:inteRact"))

# Test Transient Impression and Deflection
print("Testing transient_impression and deflection...")

tryCatch({
    # We need A, B, O EPA values.
    # From previous verification we know "student" (A=1.77, 0.02, 0.84)
    # And "request something from" (B)
    # And "assistant" (O) - need to get this value first.
    
    # Let's get "assistant" (identity) and "request_something_from" (behavior)
    # Using 'germany2007'
    
    # Helper to get EPA
    get_epa <- function(term, component) {
        res <- actdata::epa_subset(expr = term, exactmatch = TRUE, dataset = "germany2007", component = component)
        if (!is.null(res) && nrow(res) > 0) {
            # Return c(E, P, A) as numeric vector
            return(as.numeric(c(res$E, res$P, res$A)))
        }
        return(NULL)
    }

    epa_actor <- get_epa("student", "identity")
    epa_object <- get_epa("assistant", "identity")
    epa_behavior <- get_epa("request_something_from", "behavior")
    
    cat("Actor (student) EPA:", paste(epa_actor, collapse=", "), "\n")
    cat("Object (assistant) EPA:", paste(epa_object, collapse=", "), "\n")
    cat("Behavior (request...) EPA:", paste(epa_behavior, collapse=", "), "\n")
    
    if (!is.null(epa_actor) && !is.null(epa_object) && !is.null(epa_behavior)) {
        
        # inteRact::transient_impression likely expects a dataframe `d` with columns:
        # A_E, A_P, A_A, B_E, B_P, B_A, O_E, O_P, O_A (or similar)
        # Or actor_epa, behavior_epa, object_epa as names might be inferred.
        
        # Let's inspect get_equation("germany2007") structure to see coefficient names which often hint at required columns
        # (e.g. Z000, 100...)
        
        # Creating a dataframe
        # Note: EPA vectors from actdata seem to be returning multiple variants (male/female/avg from stats?) 
        # The output showed "1.77, 1.74, 1.8..." - actdata::epa_subset returns a dataframe of multiple rows if multiple group stats are present?
        # Or maybe epa_subset returns one row but multiple columns like E, P, A and maybe gender specific columns?
        # Wait, get_epa function I wrote:
        # return(as.numeric(c(res$E, res$P, res$A)))
        # If res has multiple rows, this concatenates them.
        
        # Refine get_epa to get MEAN values if possible or first row
        # germany2007 usually has group="all"
        
        # Let's use the first 3 values which should be E, P, A
        ae <- epa_actor[1:3]
        be <- epa_behavior[1:3]
        oe <- epa_object[1:3]
        
        input_df <- data.frame(
            AE = ae[1], AP = ae[2], AA = ae[3],
            BE = be[1], BP = be[2], BA = be[3],
            OE = oe[1], OP = oe[2], OA = oe[3],
            stringsAsFactors = FALSE
        )
        print("Input DataFrame:")
        print(input_df)
        
        # transient_impression needs equations. 
        # It takes `eq_df` or `equation_key`.
        if (exists("germany_2007", envir=as.environment("package:inteRact"))) {
             # Maybe inteRact has its own equation object or format
        }

        # transient_impression seems to fail with strict data expectations.
        # Let's try get_deflection which might calculate the deflection based on similar inputs.
        
        print("Args for get_deflection:")
        print(args(inteRact::get_deflection))
        
        print("Args for characteristic_emotion:")
        print(args(inteRact::characteristic_emotion))
        
        print("Args for emotions_coefficients:")
        print(args(inteRact::emotions_coefficients))
        
        # Check if we can load emotion equations
        tryCatch({
           emo_eqs <- actdata::get_eqn("germany2007", "emotionid", "all")
           print("Emotion Eqs Loaded:")
           print(emo_eqs)
        }, error = function(e) print(e))

        quit()
        
        # There might be a total deflection function
    } else {
        message("Could not retrieve all EPA values.")
    }

}, error = function(e) print(e))

quit()
