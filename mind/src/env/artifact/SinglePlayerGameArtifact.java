package artifact;

import artifact.lib.maselements.AbstractMasElementArtifact;
import artifact.lib.model.WsMessage;
import artifact.lib.utils.ObjectMapperUtils;
import cartago.INTERNAL_OPERATION;
import cartago.OPERATION;
import cartago.OpFeedbackParam;
import jason.stdlib.signal;

import com.fasterxml.jackson.core.type.TypeReference;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.List;

public class SinglePlayerGameArtifact extends AbstractMasElementArtifact {
    @OPERATION
    public void init(String artifactName, int webSocketPort) {
        super.init(artifactName, webSocketPort);
        writeLog("Inizializing Single Player Game");
        defineObsProperty("type", "singlePlayerGame");
    }

    @INTERNAL_OPERATION
    public void playGame() {
        signal("available", false);
        writeLog("Started playing " + artifactName);
        await_time(6000);
        writeLog("Finished playing " + artifactName);
        execInternalOp("signalAgentsByTick");
        signal("available", true);
    }

    @Override
    public void onMessageReceived(String message) {
        try{
            lock.lock();
            JSONObject msg = new JSONObject(message);
            if (msg.has("type")) {
                String type = msg.getString("type");
                if (type.equals("playGame")) {
                    execInternalOp("playGame");
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
