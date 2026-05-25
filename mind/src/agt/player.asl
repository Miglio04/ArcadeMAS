{ include("libraryPlans.asl") }
{ include("artifacts.asl") }

+!play
    : tokens(T) & T > 0 & played(_)
    <- .print("Attempting to play another game...");
        lookupArtifact("envManager", ArtId);
        focus(ArtId);
        !retrieve_nearest_artifacts_by_type("SinglePlayerGame", Artifacts);
        Artifacts = [_ | Rest];
        Rest = [Next | _];
        stopFocus(ArtId);
        !reach_dest(Next);
        .nth(0, Next, Upper); // Get the first character of the string
        .lower_case(Upper, Lower); // Make it lowercase
        .replace(Next, Upper, Lower, NormalizedName); // replace the first character.
        lookupArtifact(NormalizedName, ArtifactId);
        focus(ArtifactId);
        playGame[artifact_id(ArtifactId)];
        Val = math.floor(2 + math.random((5 - 2) + 1)) * 1000;
        .wait(Val);
        stopGame[artifact_id(ArtifactId)];
        -+tokens(T-1);
        stopFocus(ArtifactId);
        -movement_in_progress(_);
        +played(Next);
        !play.

+!play
    : tokens(T) & T > 0
    <- .print("Attempting to play a game...");
        lookupArtifact("envManager", ArtId);
        focus(ArtId);
        !retrieve_nearest_artifacts_by_type("SinglePlayerGame", Artifacts);
        Artifacts = [First | Rest];
        stopFocus(ArtId);
        !reach_dest(First);
        .nth(0, First, Upper); // Get the first character of the string
        .lower_case(Upper, Lower); // Make it lowercase
        .replace(First, Upper, Lower, NormalizedName); // replace the first character.
        lookupArtifact(NormalizedName, ArtifactId);
        focus(ArtifactId);
        playGame[artifact_id(ArtifactId)];
        Val = math.floor(2 + math.random((5 - 2) + 1)) * 1000;
        .wait(Val);
        stopGame[artifact_id(ArtifactId)];
        -+tokens(T-1);
        stopFocus(ArtifactId);
        -movement_in_progress(_);
        +played(First);
        !play.

+!play
    : tokens(T) & T == 0
    <- !buy_tokens.

+!buy_tokens
    : budget(B) & token_price(TokenPrice) & B >= TokenPrice
    <- .print("Attempting to buy tokens...");
        lookupArtifact("envManager", ArtId);
        focus(ArtId);
        !retrieve_nearest_artifacts_by_type("TokenMachine", Artifacts);
        Artifacts = [First | _];
        stopFocus(ArtId);
        !reach_dest(First);
        .nth(0, First, Upper); // Get the first character of the string
        .lower_case(Upper, Lower); // Make it lowercase
        .replace(First, Upper, Lower, NormalizedName); 
        lookupArtifact(NormalizedName, ArtifactId);
        focus(ArtifactId);
        Qty = math.floor(1 + math.random((4 - 1) + 1));
        !increment_tokens_times(Qty, ArtifactId);
        pay[artifact_id(ArtifactId)];
        Val = math.floor(1 + math.random((4 - 1) + 1)) * 1000;
        .wait(Val);
        -+tokens(Qty);
        TotalCost = Qty * TokenPrice;
        -+budget(B - TotalCost);
        .print("Tokens purchased successfully! Bought ", Qty, " tokens.");
        stopFocus(ArtifactId);
        -movement_in_progress(_);
        !play.

+!buy_tokens
    : budget(B) & token_price(TokenPrice) & B < TokenPrice
    <- .print("Not enough budget to buy tokens. Please check your budget and try again.");
        lookupArtifact("envManager", ArtId);
        focus(ArtId);
        !retrieve_nearest_artifacts_by_type("Door", Artifacts);
        Artifacts = [First | _];
        stopFocus(ArtId);
        !reach_dest(First).

+!increment_tokens_times(0, ArtifactId)
    <- true.

+!increment_tokens_times(N, ArtifactId)
    : N > 0 & budget(B) & token_price(TokenPrice) & B >= TokenPrice * N
    <- incrementTokens[artifact_id(ArtifactId)];
       N1 = N - 1;
       !increment_tokens_times(N1, ArtifactId).

+!increment_tokens_times(N, ArtifactId)
    : N > 0 & (budget(B) & token_price(TokenPrice) & B < TokenPrice * N)
    <- MaxAffordable = math.floor(B / TokenPrice);
       incrementTokens[artifact_id(ArtifactId)];
       N1 = N - 1;
       !increment_tokens_times(N1, ArtifactId).

{ include("$jacamo/templates/common-cartago.asl") }
{ include("$jacamo/templates/common-moise.asl") }
{ include("$moise/asl/org-obedient.asl") }