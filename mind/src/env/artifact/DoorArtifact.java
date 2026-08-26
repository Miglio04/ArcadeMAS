package artifact;

import artifact.lib.maselements.AbstractMasElementArtifact;
import artifact.lib.model.WsMessage;
import artifact.lib.utils.ObjectMapperUtils;
import cartago.OPERATION;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.core.type.TypeReference;
import java.util.LinkedHashMap;
import java.util.Map;

// ============================================================
// DoorArtifact.java
// ============================================================
// Jacamo artifact that represents a physical door.
// ============================================================
public class DoorArtifact extends AbstractMasElementArtifact {

    @OPERATION
    void init(String id, int port,
              String isOpen, String isLocked,
              float x, float y, float z) {
        try {
            super.init(id, port);
            defineObsProperty("isOpen",   Boolean.parseBoolean(isOpen));
            defineObsProperty("isLocked", Boolean.parseBoolean(isLocked));
            defineObsProperty("type",     "door");
            defineObsProperty("x", x);
            defineObsProperty("y", y);
            defineObsProperty("z", z);
        }catch(Exception e) {
            System.out.println("[envManager] error in door artifact creation: " + e.getMessage() + ", port: " + port);
        }
    }

   @Override
    public void onMessageReceived(String message) {
        try {
            WsMessage wsMsg = ObjectMapperUtils.convertJsonStringToObject(
                    message, new TypeReference<>() {});

            if ("update_status".equals(wsMsg.getMessageType())) {
                // param è una Map con isOpen e isLocked
                java.util.Map<String, Object> params = 
                    (java.util.Map<String, Object>) wsMsg.getParam();
                if (params != null) {
                    Object isOpen   = params.get("isOpen");
                    Object isLocked = params.get("isLocked");
                    if (isOpen   != null) getObsProperty("isOpen").updateValue(isOpen);
                    if (isLocked != null) getObsProperty("isLocked").updateValue(isLocked);
                    writeLog("[" + artifactName + "] isOpen=" + isOpen + " isLocked=" + isLocked);
                }
            }
            execInternalOp("signalAgentsByTick");
        } catch (Exception e) {
            logger.info("DoorArtifact error: " + e);
        }
    }

    // Agente chiede di aprire fisicamente la porta.
    // Unity risponde con update_status.
    @OPERATION
    void tryOpen() {
        try {
            Map<String, Object> payload = new LinkedHashMap<>();
            payload.put("operation",    "open_door");
            payload.put("artifactName", artifactName);
            send(new ObjectMapper().writeValueAsString(payload));
        } catch (Exception e) {
            writeLog("Errore tryOpen: " + e.getMessage());
        }
    }
}