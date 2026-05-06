// tokens(0).

// +!request_info
//     <- .wait(1000);
//         joinWorkspace("ws"); 
//         .print("Requesting game info from cashier...");
//         .send(cashier, askOne, available_games(Info)).

// +available_games(false)[source(cashier)]
//     <- print("Cashier is not ready to provide game info yet.");
//         .wait(500);
//         -available_games(false)[source(cashier)];
//         !request_info.

// +available_games(Info)[source(cashier)]
//     <- .print("Received available games info from cashier: ", Info);
//         !focus_on_games(Info);
//         !play(Info).

// +!focus_on_games([]).

// +!focus_on_games([Game|Rest])
//     <- .print("Focusing on game: ", Game);
//         lookupArtifact(Game, Id);
//         focus(Id);
//         !focus_on_games(Rest).

// +!play(Games)
//     : tokens(Tokens) & Tokens > 0
//     <- [Game|Rest] = Games;
//         lookupArtifact(Game, Id);
//         .print("Attempting to play game: ", Game);
//         playGame(Success)[artifact_id(Id)];
//         !check_play_result(Success, Games).

// +!check_play_result(true, _)
//     : tokens(Tokens) & available_games(AvailableGames)
//     <- .print("Played game successfully!");
//         -+tokens(Tokens-1);
//         !play(AvailableGames).

// +!check_play_result(false, Games)
//     <- .print("Failed to play game. It might be currently unavailable.");
//         [_|Rest] = Games;
//         !play(Rest).

// +!play(Games)
//     : tokens(Tokens) & budget(Budget) & Tokens = 0
//     <- .print("Not enough tokens to play. Buying more tokens!");
//         .wait(1000);
//         !buy_tokens(5, Budget).

// +!buy_tokens(Qty, Budget)
//     <- .send(cashier, tell, buy_tokens(Qty, Budget)).

// +tokens_bought(Qty, TotalCost)[source(cashier)]
//     : budget(Budget) & available_games(Games)
//     <- NewBudget = Budget - TotalCost;
//         -+budget(NewBudget);
//         -tokens_bought(Qty, TotalCost)[source(cashier)];
//         -+tokens(Qty);
//         !play(Games).

// +~tokens_bought(_, _)[source(cashier)]
//     <- .print("Failed to buy tokens. Please check your budget and try again.").

{ include("libraryPlans.asl") }
{ include("artifacts.asl") }

!play.

+!play
    //: tokens(T) & T > 0
    <- .print("Attempting to play a game...");
        lookupArtifact("envManager", ArtId);
        focus(ArtId);
        !retrieve_nearest_artifacts_by_type("SinglePlayerGame", Artifacts);
        Artifacts = [First | _];
        stopFocus(ArtId);
        !reach_dest(First);
        //.wait({ +reached(place, Dest) });
        .wait(3000);
        .nth(0, First, Upper); // Get the first character of the string
        .lower_case(Upper, Lower); // Make it lowercase
        .replace(First, Upper, Lower, NormalizedName); // replace the first character.
        lookupArtifact(NormalizedName, ArtifactId);
        focus(ArtifactId);
        playGame[artifact_id(ArtifactId)];
        stopFocus(ArtifactId);
        -movement_in_progress(_).

// +!play
//     : tokens(T) & T == 0
//     <- !buy_tokens.

// +!buy_tokens
//     : true
//     <- .print("Attempting to buy tokens...");
//         lookupArtifact("envManager", ArtId);
//         focus(ArtId);
//         !retrieve_nearest_artifact_by_type("TokenMachine", Artifact);
//         stopFocus(ArtId);
//         !reach_destination(Artifact);
//         .wait({ +reached(place, Dest) });
//         .nth(0, First, Upper); // Get the first character of the string
//         .lower_case(Upper, Lower); // Make it lowercase
//         .replace(First, Upper, Lower, NormalizedName); 
//         lookupArtifact(NormalizedName, ArtifactId);
//         focus(ArtifactId);
//         incrementTokens()[artifact_id(ArtifactId)];
//         incrementTokens()[artifact_id(ArtifactId)];
//         incrementTokens()[artifact_id(ArtifactId)];
//         incrementTokens()[artifact_id(ArtifactId)];
//         pay(8)[artifact_id(ArtifactId)];
//         +tokens(4);
//         stopFocus(ArtifactId);
//         -movement_in_progress(_).

{ include("$jacamo/templates/common-cartago.asl") }
{ include("$jacamo/templates/common-moise.asl") }
{ include("$moise/asl/org-obedient.asl") }