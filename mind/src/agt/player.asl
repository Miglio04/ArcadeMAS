{ include("libraryPlans.asl") }
{ include("artifacts.asl") }

+user_interaction
    <- .print("User interaction detected. Initiating play sequence...");
        vesna.stop;
        !play_with_user;
        .wait(2000000).

+!play_with_user
    <- .print("Attempting to play").

+!play
    : tokens(T) & T > 0 & games(Games) & not games([])
    <- !get_random_game;
        ?chosenGame(Game);
        .print("Attempting to play ", Game, "...");
        !reach_dest(Game);
        .nth(0, Game, Upper); // Get the first character of the string
        .lower_case(Upper, Lower); // Make it lowercase
        .replace(Game, Upper, Lower, NormalizedName); // replace the first character.
        lookupArtifact(NormalizedName, ArtifactId);
        focus(ArtifactId);

        // Blocco test owner
        .my_name(Me);
        if ( not owner("")[artifact_id(ArtifactId)] & not owner(Me)[artifact_id(ArtifactId)] ) {
            .print("The artifact ", NormalizedName, " is occupied. Waiting for it to become available...");
            requestAccess(Me);
            .wait( access_granted_ready(NormalizedName) );
            -access_granted_ready(NormalizedName);
            .print("The artifact is now available! Proceeding.");
            .wait(1000);
        }
        // Fine blocco test owner

        playGame(Me)[artifact_id(ArtifactId)];
        -+tokens(T-1);
        Val = math.floor(2 + math.random((5 - 2) + 1)) * 1000;
        .wait(Val);
        stopGame(Me)[artifact_id(ArtifactId)];
        stopFocus(ArtifactId);
        -chosenGame(Game);
        -movement_in_progress(_);
        !play.

+!play
    : tokens(T) & T > 0
    <- .print("Attempting to play a game...");
        lookupArtifact("envManager", ArtId);
        focus(ArtId);
        !retrieve_nearest_artifacts_by_type("SinglePlayerGame", Games);
        +games(Games);
        !get_random_game;
        ?chosenGame(Game);
        stopFocus(ArtId);
        !reach_dest(Game);
        .nth(0, Game, Upper); // Get the first character of the string
        .lower_case(Upper, Lower); // Make it lowercase
        .replace(Game, Upper, Lower, NormalizedName); // replace the first character.
        lookupArtifact(NormalizedName, ArtifactId);
        focus(ArtifactId);

        // Blocco test owner
        .my_name(Me);
        if ( not owner("")[artifact_id(ArtifactId)] & not owner(Me)[artifact_id(ArtifactId)] ) {
            .print("The artifact ", NormalizedName, " is occupied. Waiting for it to become available...");
            requestAccess(Me);
            .wait( access_granted_ready(NormalizedName) );
            -access_granted_ready(NormalizedName);
            .print("The artifact is now available! Proceeding.");
            .wait(1000);
        }
        // Fine blocco test owner

        playGame(Me)[artifact_id(ArtifactId)];
        -+tokens(T-1);
        Val = math.floor(2 + math.random((5 - 2) + 1)) * 1000;
        .wait(Val);
        stopGame(Me)[artifact_id(ArtifactId)];
        stopFocus(ArtifactId);
        -chosenGame(Game);
        -movement_in_progress(_);
        !play.

+!play
    : tokens(T) & T == 0
    <- !buy_tokens.

+access_granted(ArtifactName)
    <- +access_granted_ready(ArtifactName).

+!get_random_game
    : games(Games) & not games([])
    <- .shuffle(Games, ShuffledGames);
        .nth(0, ShuffledGames, Game);
        +chosenGame(Game).

+!buy_tokens
    : budget(B) & token_price(TokenPrice) & B >= TokenPrice
    <- .print("Attempting to buy tokens...");
        lookupArtifact("envManager", ArtId);
        focus(ArtId);
        !retrieve_nearest_artifacts_by_type("TokenMachine", Artifacts);
        Artifacts = [Game | _];
        stopFocus(ArtId);
        !reach_dest(Game);
        .nth(0, Game, Upper); // Get the first character of the string
        .lower_case(Upper, Lower); // Make it lowercase
        .replace(Game, Upper, Lower, NormalizedName); 
        lookupArtifact(NormalizedName, ArtifactId);
        focus(ArtifactId);

        // Blocco test owner
        .my_name(Me);
        if ( not owner("")[artifact_id(ArtifactId)] & not owner(Me)[artifact_id(ArtifactId)] ) {
            .print("The artifact ", NormalizedName, " is occupied. Waiting for it to become available...");
            requestAccess(Me);
            .wait( access_granted_ready(NormalizedName) );
            -access_granted_ready(NormalizedName);
            .print("The artifact is now available! Proceeding.");
            .wait(1000);
        }
        // Fine blocco test owner

        Qty = math.floor(1 + math.random((4 - 1) + 1));
        MaxAffordable = math.floor(B / TokenPrice);
        if (Qty > MaxAffordable) {
            PurchasedQty = MaxAffordable;
        } else {
            PurchasedQty = Qty;
        }
        !increment_tokens_times(PurchasedQty, ArtifactId);
        Val = math.floor(1 + math.random((3 - 1) + 1)) * 1000;
        .wait(Val);
        pay(Me)[artifact_id(ArtifactId)];
        -+tokens(PurchasedQty);
        TotalCost = PurchasedQty * TokenPrice;
        -+budget(B - TotalCost);
        .print("Tokens purchased successfully! Bought ", PurchasedQty, " tokens.");
        stopFocus(ArtifactId);
        -movement_in_progress(_);
        !play.

+!buy_tokens
    : budget(B) & token_price(TokenPrice) & B < TokenPrice
    <- .print("Not enough budget to buy tokens. Please check your budget and try again.");
        lookupArtifact("envManager", ArtId);
        focus(ArtId);
        !retrieve_nearest_artifacts_by_type("Door", Artifacts);
        Artifacts = [Door | _];
        stopFocus(ArtId);
        !reach_dest(Door).

+!increment_tokens_times(0, ArtifactId)
    <- true.

+!increment_tokens_times(N, ArtifactId)
    : N > 0 & budget(B) & token_price(TokenPrice) & B >= TokenPrice * N
    <- .my_name(Me);
        incrementTokens(Me)[artifact_id(ArtifactId)];
        .wait(700);
        N1 = N - 1;
        !increment_tokens_times(N1, ArtifactId).

{ include("$jacamo/templates/common-cartago.asl") }
{ include("$jacamo/templates/common-moise.asl") }
{ include("$moise/asl/org-obedient.asl") }