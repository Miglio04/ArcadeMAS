package artifact;

import artifact.lib.maselements.AbstractMasElementArtifact;
import cartago.INTERNAL_OPERATION;
import cartago.OPERATION;
import org.json.JSONObject;

public class TokenMachineArtifact extends AbstractMasElementArtifact {
    private static final String TOKENS = "tokens";
    
    @Override
    @OPERATION
    public void init(String artifactName, int webSocketPort) {
        super.init(artifactName, webSocketPort);
        defineObsProperty(TOKENS, 0);
        defineObsProperty("token_price", 2);
        defineObsProperty("type", "tokenMachine");
    }

    @INTERNAL_OPERATION
    public void clearTokenMachine() {
        writeLog("Clearing token machine");
        getObsProperty(TOKENS).updateValue(0);
    }

    @INTERNAL_OPERATION
    public void incrementTokens() {
        int tokens = ((Number) getObsProperty(TOKENS).getValue()).intValue();
        tokens++;
        getObsProperty(TOKENS).updateValue(tokens);
        writeLog("Tokens: " + tokens);
    }

    @INTERNAL_OPERATION
    public void decrementTokens() {
        int tokens = ((Number) getObsProperty(TOKENS).getValue()).intValue();
        if (tokens > 0) {
            tokens--;
            getObsProperty(TOKENS).updateValue(tokens);
            writeLog("Tokens: " + tokens);
        }
    }

    @INTERNAL_OPERATION
    public void pay() {
        int tokens = ((Number) getObsProperty(TOKENS).getValue()).intValue();
        execInternalOp("signalAgentsByTick");
        signal(TOKENS, tokens);
        writeLog("Payment successful");
        clearTokenMachine();
    }

    @Override
    public void onMessageReceived(String message) {
        try{
            JSONObject msg = new JSONObject(message);
            writeLog(message);
            if (msg.has("type")) {
                String type = msg.getString("type");
                switch (type) {
                    case "incrementTokens":
                        execInternalOp("incrementTokens");
                        break;
                    case "decrementTokens":
                        execInternalOp("decrementTokens");
                        break;
                    case "pay":
                        execInternalOp("pay");
                        break;
                    case "clearTokenMachine":
                        execInternalOp("clearTokenMachine");
                        break;
                    default:
                        break;
                }
            }
        }
        catch (Exception e) {
            writeLog("Error processing message: " + e.getMessage());
        } 
    }
}
