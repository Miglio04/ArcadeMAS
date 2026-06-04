package artifact;

import artifact.lib.maselements.AbstractMasElementArtifact;
import artifact.lib.model.WsMessage;
import artifact.lib.utils.ObjectMapperUtils;
import cartago.INTERNAL_OPERATION;
import cartago.OPERATION;
import org.json.JSONObject;

import java.util.Map;

public class TokenMachineArtifact extends AbstractMasElementArtifact {
    private static final String TOKENS = "tokens";
    private static final String OWNER_PROPERTY = "owner";
    private static final String INCREMENT_TOKENS_ACTION = "incrementTokens";
    private static final String DECREMENT_TOKENS_ACTION = "decrementTokens";
    private static final String PAY_ACTION = "pay";
    private static final String CLEAR_TOKEN_MACHINE_ACTION = "clearTokenMachine";
    
    @Override
    @OPERATION
    public void init(String artifactName, int webSocketPort) {
        super.init(artifactName, webSocketPort);
        defineObsProperty(TOKENS, 0);
        defineObsProperty("token_price", 2);
        defineObsProperty("type", "tokenMachine");
        defineObsProperty(OWNER_PROPERTY, "");
    }

    @INTERNAL_OPERATION
    public void clearTokenMachine() {
        writeLog("Clearing token machine");
        String owner = String.valueOf(getObsProperty(OWNER_PROPERTY).getValue());
        notifyUnityAction(CLEAR_TOKEN_MACHINE_ACTION, Map.of(OWNER_PROPERTY, owner));
        getObsProperty(TOKENS).updateValue(0);
        getObsProperty(OWNER_PROPERTY).updateValue("");
    }

    @INTERNAL_OPERATION
    public void incrementTokens(String owner) {
        getObsProperty(OWNER_PROPERTY).updateValue(owner);
        int tokens = ((Number) getObsProperty(TOKENS).getValue()).intValue();
        tokens++;
        getObsProperty(TOKENS).updateValue(tokens);
        writeLog("Tokens: " + tokens);
        notifyUnityAction(INCREMENT_TOKENS_ACTION, Map.of(OWNER_PROPERTY, owner, TOKENS, tokens));
    }

    @INTERNAL_OPERATION
    public void decrementTokens(String owner) {
        getObsProperty(OWNER_PROPERTY).updateValue(owner);
        int tokens = ((Number) getObsProperty(TOKENS).getValue()).intValue();
        if (tokens > 0) {
            tokens--;
            getObsProperty(TOKENS).updateValue(tokens);
            writeLog("Tokens: " + tokens);
            notifyUnityAction(DECREMENT_TOKENS_ACTION, Map.of(OWNER_PROPERTY, owner, TOKENS, tokens));
        }
    }

    @INTERNAL_OPERATION
    public void pay(String owner) {
        getObsProperty(OWNER_PROPERTY).updateValue(owner);
        int tokens = ((Number) getObsProperty(TOKENS).getValue()).intValue();
        execInternalOp("signalAgentsByTick");
        signal(TOKENS, tokens);
        writeLog("Payment successful");
        clearTokenMachine();
    }

    private void notifyUnityAction(String actionName, Map<String, Object> params) {
        WsMessage wsMessage = new WsMessage();
        wsMessage.setMessageType("artifactAction");
        wsMessage.setMessagePayload("triggered");
        wsMessage.setAgentEvent(actionName);
        wsMessage.setAgentName(this.artifactName);
        wsMessage.setParam(params);

        send(ObjectMapperUtils.convertIntoJsonString(wsMessage));
    }

    @Override
    public void onMessageReceived(String message) {
        try {
            JSONObject msg = new JSONObject(message);
            writeLog(message);
            if (msg.has("type")) {
                String type = msg.getString("type");
                switch (type) {
                    case INCREMENT_TOKENS_ACTION:
                        execInternalOp(INCREMENT_TOKENS_ACTION, "user");
                        break;
                    case DECREMENT_TOKENS_ACTION:
                        execInternalOp(DECREMENT_TOKENS_ACTION, "user");
                        break;
                    case PAY_ACTION:
                        execInternalOp(PAY_ACTION, "user");
                        break;
                    case CLEAR_TOKEN_MACHINE_ACTION:
                        execInternalOp(CLEAR_TOKEN_MACHINE_ACTION);
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
