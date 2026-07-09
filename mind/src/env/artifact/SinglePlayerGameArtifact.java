package artifact;

import artifact.lib.maselements.AbstractMasElementArtifact;
import artifact.lib.model.WsMessage;
import artifact.lib.utils.ObjectMapperUtils;
import cartago.INTERNAL_OPERATION;
import cartago.OPERATION;

import org.json.JSONObject;

import java.util.ArrayList;
import java.util.Map;


public class SinglePlayerGameArtifact extends AbstractMasElementArtifact {
    private static final String OWNER_PROPERTY = "owner";
    private static final String PLAY_GAME_ACTION = "playGame";
    private static final String STOP_GAME_ACTION = "stopGame";
    private static final String ACCESS_GRANTED_ACTION = "access_granted";
    private static final ArrayList<String> waitList = new ArrayList<>();

    @Override
    @OPERATION
    public void init(String artifactName, int webSocketPort) {
        super.init(artifactName, webSocketPort);
        writeLog("Inizializing Single Player Game");
        defineObsProperty("type", "singlePlayerGame");
        defineObsProperty(OWNER_PROPERTY, "");
    }

    @INTERNAL_OPERATION
    public void requestAccess(String agentName) {
        String currentOwner = String.valueOf(getObsProperty(OWNER_PROPERTY).getValue());
        writeLog("requestAccess called by " + agentName + ", current owner=" + currentOwner + ", waitList=" + waitList);
        if (!waitList.contains(agentName)) {
            waitList.add(agentName);
            writeLog("Wait list after add: " + waitList);
        } else {
            writeLog("Agent already present in wait list: " + waitList);
        }
    }

    @INTERNAL_OPERATION
    public void playGame(String owner) {
        getObsProperty(OWNER_PROPERTY).updateValue(owner);
        signal("available", false);
        writeLog("Started playing " + artifactName);
        
        notifyUnityAction(PLAY_GAME_ACTION, Map.of(OWNER_PROPERTY, owner));
    }

    @INTERNAL_OPERATION
    public void stopGame(String owner) {
        getObsProperty(OWNER_PROPERTY).updateValue(owner);
        
        writeLog("Stopped playing " + artifactName);
        execInternalOp("signalAgentsByTick");
        signal("available", true);
        
        notifyUnityAction(STOP_GAME_ACTION, Map.of(OWNER_PROPERTY, owner));
        
        if (waitList.isEmpty()) {
            getObsProperty(OWNER_PROPERTY).updateValue("");
            writeLog("No agents waiting, owner cleared");
        } else {
            String nextAgent = waitList.remove(0);
            getObsProperty(OWNER_PROPERTY).updateValue(nextAgent);
            signalAgent(nextAgent, ACCESS_GRANTED_ACTION, normalizedArtifactName());
            writeLog("Granted access to " + nextAgent + ", remaining queue=" + waitList);
        }
    }

    private void notifyUnityAction(String actionName, Map<String, String> params) {
        WsMessage wsMessage = new WsMessage();
        wsMessage.setMessageType("artifactAction");
        wsMessage.setMessagePayload("triggered");
        wsMessage.setAgentEvent(actionName);
        wsMessage.setAgentName(this.artifactName);
        wsMessage.setParam(params);

        send(ObjectMapperUtils.convertIntoJsonString(wsMessage));
    }

    private String normalizedArtifactName() {
        if (artifactName == null || artifactName.isEmpty()) {
            return artifactName;
        }

        return Character.toLowerCase(artifactName.charAt(0)) + artifactName.substring(1);
    }

    @Override
    public void onMessageReceived(String message) {
        try{
            lock.lock();
            JSONObject msg = new JSONObject(message);
            if (msg.has("type")) {
                String type = msg.getString("type");
                if (type.equals(PLAY_GAME_ACTION)) {
                    execInternalOp(PLAY_GAME_ACTION, "user");
                }
                else if (type.equals(STOP_GAME_ACTION)) {
                    execInternalOp(STOP_GAME_ACTION, "user");
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
