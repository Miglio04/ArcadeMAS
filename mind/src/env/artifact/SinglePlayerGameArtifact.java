package artifact;

import artifact.lib.maselements.AbstractMasElementArtifact;
import artifact.lib.model.WsMessage;
import artifact.lib.utils.ObjectMapperUtils;
import cartago.INTERNAL_OPERATION;
import cartago.OPERATION;

import org.json.JSONObject;


public class SinglePlayerGameArtifact extends AbstractMasElementArtifact {
    @OPERATION
    public void init(String artifactName, int webSocketPort) {
        super.init(artifactName, webSocketPort);
        writeLog("Inizializing Single Player Game");
        defineObsProperty("type", "singlePlayerGame");
        defineObsProperty("owner", "");
    }

    @INTERNAL_OPERATION
    public void playGame(String owner) {
        getObsProperty("owner").updateValue(owner);
        signal("available", false);
        writeLog("Started playing " + artifactName);
        
        notifyUnityAction("playGame", owner);
    }

    @INTERNAL_OPERATION
    public void stopGame(String owner) {
        getObsProperty("owner").updateValue(owner);
        
        writeLog("Stopped playing " + artifactName);
        execInternalOp("signalAgentsByTick");
        signal("available", true);
        
        notifyUnityAction("stopGame", owner);
        
        getObsProperty("owner").updateValue("");
    }

    private void notifyUnityAction(String actionName, String requestingAgent) {
        WsMessage wsMessage = new WsMessage();
        wsMessage.setMessageType("artifactAction");
        wsMessage.setMessagePayload("triggered");
        wsMessage.setAgentEvent(actionName);
        wsMessage.setAgentName(this.artifactName);
        wsMessage.setParam(requestingAgent);

        send(ObjectMapperUtils.convertIntoJsonString(wsMessage));
    }

    @Override
    public void onMessageReceived(String message) {
        try{
            lock.lock();
            JSONObject msg = new JSONObject(message);
            if (msg.has("type")) {
                String type = msg.getString("type");
                if (type.equals("playGame")) {
                    execInternalOp("playGame", "user");
                }
                else if (type.equals("stopGame")) {
                    execInternalOp("stopGame", "user");
                }
            }
        }
        catch (Exception e) {
            writeLog("Error processing message: " + e.getMessage());
        }finally {
            lock.unlock();
        }
    }
}
