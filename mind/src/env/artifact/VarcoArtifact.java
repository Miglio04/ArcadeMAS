package artifact;

import artifact.lib.maselements.AbstractMasElementArtifact;
import cartago.OPERATION;

// ============================================================
// VarcoArtifact.java
// ============================================================
// Jacamo artifact that represents an open passage.
// ============================================================
public class VarcoArtifact extends AbstractMasElementArtifact {

    @OPERATION
    void init(String id, int port, float x, float y, float z) {
        try {
        super.init(id, port);
        defineObsProperty("isOpen",   true);
        defineObsProperty("isLocked", false);
        defineObsProperty("type",     "varco");
        defineObsProperty("x", x);
        defineObsProperty("y", y);
        defineObsProperty("z", z);
        }catch(Exception e){
            System.out.println("[envManager] Error in varco artifact creation: " + e.getMessage() + ", port: " + port);
        }
    }

}