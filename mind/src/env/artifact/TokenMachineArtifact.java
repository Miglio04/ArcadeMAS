package artifact;

import artifact.lib.maselements.AbstractMasElementArtifact;
import artifact.lib.model.WsMessage;
import artifact.lib.utils.ObjectMapperUtils;
import cartago.INTERNAL_OPERATION;
import cartago.OPERATION;
import cartago.ObsProperty;
import cartago.OpFeedbackParam;
import jason.stdlib.signal;
import jason.stdlib.map.get;

import com.fasterxml.jackson.core.type.TypeReference;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.List;

public class TokenMachineArtifact extends AbstractMasElementArtifact {
    
    @OPERATION
    public void init(String artifactName, int webSocketPort) {
        super.init(artifactName, webSocketPort);
        defineObsProperty("tokens", 100);
        defineObsProperty("token_price", 2);
        defineObsProperty("type", "tokenMachine");
    }

    private int tokens = 0;

    @INTERNAL_OPERATION
    public void clearTokenMachine() {
        tokens = 0; // Reset tokens after transaction
        //lock.unlock();
    }

    @INTERNAL_OPERATION
    public void incrementTokens() {
        tokens++;
    }

    @INTERNAL_OPERATION
    public void decrementTokens() {
        if (tokens > 0) {
            tokens--;
        }
    }

    @INTERNAL_OPERATION
    public void pay() {
        getObsProperty("tokens").updateValue(tokens);
        execInternalOp("signalAgentsByTick");
        signal("tokens", tokens);
        clearTokenMachine();
    }

    @Override
    public void onMessageReceived(String message) {
        try{
            //lock.lock();
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

    @OPERATION
    void sellTokens(int qt, int budget, OpFeedbackParam<Boolean> success, OpFeedbackParam<Integer> tokensRemaining,
            OpFeedbackParam<Integer> totalCost) {
        ObsProperty tokens = getObsProperty("tokens");
        ObsProperty tokenPrice = getObsProperty("token_price");
        if (tokens.intValue() >= qt && qt * tokenPrice.intValue() <= budget) {
            tokens.updateValue(tokens.intValue() - qt);
            success.set(true);
            totalCost.set(qt * tokenPrice.intValue());
        } else {
            success.set(false);
            totalCost.set(0);
        }
        tokensRemaining.set(tokens.intValue());
    }
    
}
