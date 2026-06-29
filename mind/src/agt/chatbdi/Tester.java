package chatbdi;

import org.json.JSONArray;
import org.json.JSONObject;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.LinkedBlockingQueue;
import java.util.concurrent.TimeUnit;

import jason.asSemantics.Message;
import jason.runtime.Settings;

public class Tester {
    private Interpreter interpreter;
    private LinkedBlockingQueue<String> agentResponse = new LinkedBlockingQueue<>();

    public Tester(Interpreter interpreter) {
        this.interpreter = interpreter;
    }

    public void runTest(String inputJsonPath, String outputJsonPath) {
        new Thread(() -> {
            try {
                // 1. Colleghiamo il tester all'interprete
                interpreter.setTester(this);

                String content = Files.readString(Path.of(inputJsonPath));
                JSONObject jsonObject = new JSONObject(content);
                JSONArray jsonArray = jsonObject.getJSONArray("testCases");
                JSONArray results = new JSONArray();

                interpreter.logInfo("[TESTER] input test cases: " + inputJsonPath);

                for (int i = 0; i < jsonArray.length(); i++) {
                    JSONObject testCase = jsonArray.getJSONObject(i);
                    
                    // right jason format check
                    if (!testCase.has("agent") || !testCase.has("nlInput")) continue;
                    
                    int id = testCase.getInt("id");
                    String agent = testCase.getString("agent");
                    String nlInput = testCase.getString("nlInput"); 
                    
                    interpreter.logInfo("[TESTER] Test case: " + id + ", agent: " + agent + ", nlInput: " + nlInput);
                    
                    List<String> receivers = new ArrayList<>();
                    receivers.add(agent);

                    agentResponse.clear(); // Clearing the queue before sending a new message
                    
                    JSONObject result = new JSONObject();
                    result.put("id", id);
                    result.put("agent", agent);
                    result.put("nlInput", nlInput);

                    // START!
                    long nl2logDurationStart = System.currentTimeMillis();

                    try {
                        // translation from NL to KQML
                        Message generatedMessage = interpreter.nl2kqml(receivers, nlInput);
                        
                        // END!
                        long afterNl2LogTime = System.currentTimeMillis();
                        long nl2logDuration = afterNl2LogTime - nl2logDurationStart;

                        if (generatedMessage != null) {
                            result.put("directive", generatedMessage.getIlForce());

                            // Litteral estraction and conversion to JSON
                            if (generatedMessage.getPropCont() instanceof jason.asSyntax.Literal) {
                                jason.asSyntax.Literal term = (jason.asSyntax.Literal) generatedMessage.getPropCont();
                                JSONObject termJson = chatbdi.Tools.mapToJson(chatbdi.Tools.termToMap(term));
                                for (String key : termJson.keySet()) {
                                    result.put(key, termJson.get(key));
                                }
                            }

                            String realResponse;
                            long log2nlDuration;
                            long totalDuration;

                            if(!generatedMessage.getIlForce().equals("tell")){
                                // sending message to the agent
                                generatedMessage.setReceiver(agent);
                                
                                // START!
                                long startlog2nlTime = System.currentTimeMillis();
                                interpreter.sendMsg(generatedMessage);
                                realResponse = agentResponse.poll(15, TimeUnit.SECONDS);
                                long endTime = System.currentTimeMillis();
                                // END!

                                log2nlDuration = endTime - startlog2nlTime;
                                totalDuration = nl2logDuration + log2nlDuration;
                            } else {
                                log2nlDuration = 0;
                                totalDuration = nl2logDuration;
                                realResponse = "null";
                            }

                            result.put("nlResponse", realResponse != null ? realResponse : "TIMEOUT");
                            result.put("nl2logDuration", nl2logDuration);
                            result.put("log2nlDuration", log2nlDuration);
                            result.put("totalDuration", totalDuration);

                        } else {
                            result.put("directive", "null");
                            result.put("generatedMessage", "null");
                            result.put("nl2logDuration", nl2logDuration);
                            result.put("totalDuration", nl2logDuration);
                        }

                        interpreter.logInfo("[TESTER] Test case " + id + " executed successfully");
                    } catch (Exception e) {
                        long endTime = System.currentTimeMillis();
                        result.put("error", e.getMessage());
                        result.put("totalDuration", endTime - nl2logDurationStart);
                        interpreter.logSevere("[TESTER] Test case " + id + " failed with error: " + e.getMessage());
                    }
                    
                    results.put(result);
                    interpreter.logInfo("[TESTER] Test case " + id  + " saved in ram");
                }

                // Get the model name from the settings
                Settings stts = interpreter.getTS().getSettings();
                String modelName = stts.getUserParameter("gen_model");

                // Creationg final output JSON
                JSONObject finalOutput = new JSONObject();
                finalOutput.put("modelUsed", modelName != null ? modelName : "unknown");
                finalOutput.put("testCases", results);
                
                saveResultsToFile(outputJsonPath, finalOutput.toString(4), modelName);

            } catch (Exception e) {
                interpreter.logSevere("[TESTER] Fatal Error: " + e.getMessage());
                e.printStackTrace();
            } finally {
                interpreter.setTester(null); 
            }
        }).start();
    }

    private void saveResultsToFile(String outputJsonPath, String jsonContent, String modelName) throws Exception {
        // Creation of directories if they don't exist
        Files.createDirectories(Path.of(outputJsonPath).getParent());
        
        // Construction of the safe file name by replacing "/" with "_" and handling null model names
        String safeModelName;
        if (modelName == null) {
            interpreter.logWarning("[TESTER] Model name is null, using 'unknown' as default.");
            safeModelName = "unknown";
        } else {
            safeModelName = modelName.replace("/", "_");
        }
        
        // If the outputJsonPath is "results/risultati.json", we add the model name before the extension
        String finalPath;
        if (outputJsonPath.endsWith(".json")) {
            finalPath = outputJsonPath.replace(".json", "_" + safeModelName + ".json");
        } else {
            finalPath = outputJsonPath + "_" + safeModelName + ".json";
        }
        
        Files.writeString(Path.of(finalPath), jsonContent);
        interpreter.logInfo("[TESTER] results saved in: " + finalPath);
    }

    public void onAgentResponse(String response){
        interpreter.logInfo("[TESTER] Received agent response: " + response);
        agentResponse.offer(response);
    }
}