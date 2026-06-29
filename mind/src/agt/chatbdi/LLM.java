package chatbdi;

import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.net.ConnectException;
import java.net.URI;
import java.io.IOException;
import java.util.List;
import java.util.ArrayList;
import java.util.Set;
import java.util.HashSet;
import java.util.Map;
import java.util.HashMap;

import java.nio.file.Files;
import java.nio.file.Path;

import org.json.JSONObject;
import org.json.JSONArray;

import jason.asSyntax.*;
import jason.asSemantics.Message;
import static jason.asSyntax.ASSyntax.*;

import jason.asSyntax.parser.ParseException;
import jason.infra.local.RunLocalMAS;
import jason.runtime.Settings;
import jason.NoValueException;

import static chatbdi.Tools.*;

/**
 * The LLM class provides an API to call local (Ollama) or remote (OpenAI api style) models
 * @author Andrea Gatti
 */
public class LLM {
    
    private String GEN_MODEL;
    private String GEN_PROVIDER;
    private String GEN_KEY;
    private String GEN_URL;

    private String EMB_MODEL;
    private String EMB_PROVIDER;
    private String EMB_KEY;
    private String EMB_URL;

    /** The name that will be assigned to the model that translates KQML to NL */
    private final String LOG2NL_MODEL = "logic-to-nl";
    /** The name that will be assigned to the model that translates NL to KQML */
    private final String NL2LOG_MODEL = "nl-to-logic";
    /** The name that will be assigned to the model that classifies the Illocutionary Force */
    private final String CLASS_MODEL = "classify-ilf";

    /** The temperature for the generation models */
    private float TEMPERATURE = 0.0f;
    /** The seed for the generation models (to be reproducible) */
    private int SEED = 42;

    /** The Agent name (for log printing) */
    private String agName;

    /** Map to save system prompts in memory (used only for OpenAI virtual models) */
    private Map<String, String> SystemPrompts = new HashMap<>(); 

    private String NL2LOG_PROMPT;
    private String LOG2NL_PROMPT;
    private String NL2LOG_MODELFILE;
    private String LOG2NL_MODELFILE;
    private String CLASS_MODELFILE ;

    /**
     * List of the supported Illocutionary Forces
     */
    private final String[] SUPPORTED_ILF;
    /**
     * The HTTP client for the requests
     */
    private final HttpClient client = HttpClient.newHttpClient();

    /**
     * Creates a new LLM object
     * @param supportedIlfs the list of supported Illocuctionary Forces
     * @throws ConnectException if the LLM server is not available
     */
    public LLM( String[] supportedIlfs, String agName, Settings stts ) throws ConnectException {

        this.agName = agName;
        
        // System prompts and files
        NL2LOG_PROMPT = stts.getUserParameter("nl2log_prompt");
        LOG2NL_PROMPT = stts.getUserParameter("log2nl_prompt");
        NL2LOG_MODELFILE = stts.getUserParameter("nl2log_model");
        LOG2NL_MODELFILE = stts.getUserParameter("log2nl_model");
        CLASS_MODELFILE = stts.getUserParameter("class_model");

        // Generation parameters (Chat/Translation)
        GEN_MODEL = stts.getUserParameter( "gen_model" );
        GEN_KEY = stts.getUserParameter( "gen_key" );
        GEN_URL = stts.getUserParameter( "gen_url" );
        GEN_PROVIDER = stts.getUserParameter( "gen_provider" );
        if ( GEN_PROVIDER == null )
            GEN_PROVIDER = "openai";

        // Embedding parameters (Vectors)
        EMB_MODEL = stts.getUserParameter( "emb_model" );
        EMB_KEY = stts.getUserParameter( "emb_key" );
        EMB_URL = stts.getUserParameter( "emb_url" );
        if ( EMB_URL == null )
            EMB_URL = "http://localhost:11434/api/"; 
        EMB_PROVIDER = stts.getUserParameter( "emb_provider" );
        if ( EMB_PROVIDER == null )
            EMB_PROVIDER = "ollama";

        // Optional parameters
        String sttsTemperature = stts.getUserParameter( "temperature" );
        if ( sttsTemperature != null )
            TEMPERATURE = Float.parseFloat(sttsTemperature);
            
        String sttsSeed = stts.getUserParameter( "seed" );
        if ( sttsSeed != null )
            SEED = Integer.parseInt(sttsSeed);
 
        // Store the supported Illocutionary Forces
        SUPPORTED_ILF = new String[ supportedIlfs.length ];
        for ( int i = 0; i < supportedIlfs.length; i++ )
            SUPPORTED_ILF[i] = supportedIlfs[i];

        if ("groq".equalsIgnoreCase(GEN_PROVIDER)){
            GEN_PROVIDER = "openai"; // Use OpenAI provider logic for Groq, as they are API compatible
        }


        // Check if the server is online at the given address
        if ( !is_online() ) {
            throw new ConnectException( "The LLM Server is offline or the address is not correct." );
        }
        
        // Initialize the generation models with prompts
        System.out.println( "Initializing generation models" );
        create( GEN_MODEL, NL2LOG_MODEL, TEMPERATURE, NL2LOG_MODELFILE, SEED );
        create( GEN_MODEL, LOG2NL_MODEL, TEMPERATURE, LOG2NL_MODELFILE, SEED );
        create( GEN_MODEL, CLASS_MODEL, TEMPERATURE, CLASS_MODELFILE, SEED );
    }

    /**
     * Checks if LLM server is online
     * @return true if available and ready (status code: 200), false otherwise
     * Can handle:
     * <ul>
     * <li> ConnectExcept if the server is not reachable </li>
     * <li> IOException if the message cannot be send </li>
     * <li> InterruptedException if the connection is interrupted </li>
     * </ul>
     */
    private boolean is_online() {
        if("openai".equalsIgnoreCase(GEN_PROVIDER) || "gemini".equalsIgnoreCase(GEN_PROVIDER)) {
            return true; 
        }

        try {
            HttpRequest req = HttpRequest.newBuilder()
                .uri( URI.create( GEN_URL.replaceAll( "/api/", "" ) ) )
                .header( "Content-Type", "application/json" )
                .GET()
                .build();

            HttpResponse<String> res = client.send( req, HttpResponse.BodyHandlers.ofString() );
            return res.statusCode() == 200;
        } catch( ConnectException e ) {
            return false;
        } catch( IOException e ) {
            Interpreter ag = (Interpreter) RunLocalMAS.getRunner().getAg( agName ).getTS().getAgArch();
            ag.logSevere(e.getMessage());
        } catch( InterruptedException e ) {
            Interpreter ag = (Interpreter) RunLocalMAS.getRunner().getAg( agName ).getTS().getAgArch();
            ag.logSevere(e.getMessage());
        }
        return false;
    }

    /**
     * Generates the embedding of a string
     * @param str the string to embed
     * @return a list of double (the embedding vector). The size depends on the model used.
     */
    public List<Double> embed( String str ) {
        JSONObject json = new JSONObject();
        String endpoint = "";

        if ("openai".equals(EMB_PROVIDER) || "gemini".equals(EMB_PROVIDER)) {
            endpoint = "embeddings"; 
            json.put("model", EMB_MODEL);
            json.put("input", str);
        } else {
            endpoint = "embed"; 
            json.put("model", EMB_MODEL);
            json.put("input", str);
            json.put("stream", false);
        }

        HttpRequest.Builder builder = HttpRequest.newBuilder()
            .uri( URI.create( EMB_URL + endpoint ) )
            .header( "Content-Type", "application/json" );

        if (EMB_KEY != null && !EMB_KEY.isEmpty()) {
            builder.header("Authorization", "Bearer " + EMB_KEY);
        }

        HttpRequest req = builder.POST( HttpRequest.BodyPublishers.ofString( json.toString() ) ).build();

        try {
            HttpResponse<String> res = client.send( req, HttpResponse.BodyHandlers.ofString() );
            
            if (res.statusCode() != 200) {
                throw new RuntimeException("API Error " + res.statusCode() + " during Embed: " + res.body());
            }
            
            JSONObject emb_json = new JSONObject( res.body() );
            List<Double> list = new ArrayList<>();
            JSONArray vec = null;

            if (emb_json.has("data")) {
                vec = emb_json.getJSONArray("data").getJSONObject(0).getJSONArray("embedding");
            } else if (emb_json.has("embeddings")) {
                vec = emb_json.getJSONArray("embeddings").getJSONArray(0);
            } else if (emb_json.has("embedding") && emb_json.getJSONObject("embedding").has("values")) {
                vec = emb_json.getJSONObject("embedding").getJSONArray("values");
            } else {
                throw new RuntimeException("Struttura JSON non riconosciuta. Risposta: " + res.body());
            }

            for( int i = 0; i < vec.length(); i++ ) {
                list.add( vec.getDouble( i ) );
            }
            return list;
            
        } catch ( Exception e ) {
            e.printStackTrace();
        }
        return null;
    }

    /**
     * Computes the embedding of a Literal term
     * @param term the Literal to embed
     * @return a list of double (the embedding vector). The size depends on the model used.
     */
    public List<Double> embed( Literal term ) {
        return embed( preprocess( term ) );
    }

    /**
     * This function calls the GENERATE API for the model with input str
     */
    private String generate( String model, String str ) {
        JSONObject json = new JSONObject();
        String endpoint = "";

        if ("openai".equals(GEN_PROVIDER) || "gemini".equals(GEN_PROVIDER)) {
            endpoint = "chat/completions";
            json.put( "model", GEN_MODEL ); 
            
            JSONArray messages = new JSONArray();
            if (SystemPrompts.containsKey(model)) {
                messages.put(new JSONObject().put("role", "system").put("content", SystemPrompts.get(model)));
            }
            messages.put(new JSONObject().put("role", "user").put("content", str));
            json.put( "messages", messages );
        } else {
            endpoint = "generate";
            json.put( "model", model );
            json.put( "prompt", str );
            json.put( "stream", false );
        }

        HttpRequest.Builder builder = HttpRequest.newBuilder()
            .uri( URI.create( GEN_URL + endpoint ) )
            .header( "Content-Type", "application/json" );

        if (GEN_KEY != null && !GEN_KEY.isEmpty()) {
            builder.header("Authorization", "Bearer " + GEN_KEY);
        }

        HttpRequest req = builder.POST( HttpRequest.BodyPublishers.ofString( json.toString() ) ).build();

        try {
            HttpResponse<String> res = client.send( req, HttpResponse.BodyHandlers.ofString() );
            
            if (res.statusCode() != 200) {
                throw new RuntimeException("API Error " + res.statusCode() + " during Generate: " + res.body());
            }
            
            if ("openai".equals(GEN_PROVIDER) || "gemini".equals(GEN_PROVIDER) || "groq".equals(GEN_PROVIDER)) {
                String cleanBody = res.body().trim();
                JSONObject jsonRes = new JSONObject(cleanBody);
                String content = jsonRes.getJSONArray("choices").getJSONObject(0).getJSONObject("message").getString("content");
                
                JSONObject fakeOllamaRes = new JSONObject();
                fakeOllamaRes.put("response", content);
                return fakeOllamaRes.toString(); 
            }
            
            return res.body();
        } catch( Exception e ) {
            e.printStackTrace();
        }
        return null;
    }

    /**
     * This function translates a message using model and following format
     */
    private String generate( String model, String str, JSONObject format ) {
        JSONObject json = new JSONObject();
        String endpoint = "";

        if ("openai".equals(GEN_PROVIDER) || "gemini".equals(GEN_PROVIDER)) {
            endpoint = "chat/completions";
            json.put( "model", GEN_MODEL );
            
            JSONArray messages = new JSONArray();
            if (SystemPrompts.containsKey(model)) {
                messages.put(new JSONObject().put("role", "system").put("content", SystemPrompts.get(model)));
            }

            messages.put(new JSONObject().put("role", "user").put("content", str + "\nRespond in JSON format following this schema: " + format.toString()));
            json.put( "messages", messages );
            json.put("response_format", new JSONObject().put("type", "json_object"));
        } else {
            endpoint = "generate";
            json.put( "model", model );
            json.put( "prompt", str );
            json.put( "format", format );
            json.put( "stream", false );
        }

        HttpRequest.Builder builder = HttpRequest.newBuilder()
            .uri( URI.create( GEN_URL + endpoint ) )
            .header( "Content-Type", "application/json" );

        if (GEN_KEY != null && !GEN_KEY.isEmpty()) {
            builder.header("Authorization", "Bearer " + GEN_KEY);
        }

        HttpRequest req = builder.POST( HttpRequest.BodyPublishers.ofString( json.toString() ) ).build();

        try {
            HttpResponse<String> res = client.send( req, HttpResponse.BodyHandlers.ofString() );
            
            if (res.statusCode() != 200) {
                throw new RuntimeException("API Error " + res.statusCode() + " during Generate (Schema): " + res.body());
            }
            
            if ("openai".equals(GEN_PROVIDER) || "gemini".equals(GEN_PROVIDER)) {
                String cleanBody = res.body().trim();
                JSONObject jsonRes = new JSONObject(cleanBody); 
                String content = jsonRes.getJSONArray("choices").getJSONObject(0).getJSONObject("message").getString("content");
                
                JSONObject fakeOllamaRes = new JSONObject();
                fakeOllamaRes.put("response", content);
                return fakeOllamaRes.toString(); 
            }
            
            return res.body();
        } catch( Exception e ) {
            e.printStackTrace();
        }
        return null;
    }

    /**
     * This function classifies a message Illocutionary Force
     * @param msg the input message
     * @return the Literal correspondent to the Illocutionary Force
     */
    public Literal classify( String msg ) {
        // Build a JSON Schema with the available Illocutionary Forces
        JSONObject ilf = new JSONObject();
        ilf.put( "type", "string" );
        ilf.put( "enum", SUPPORTED_ILF );
        JSONObject properties = new JSONObject();
        properties.put( "Illocutionary Force", ilf );
        JSONObject json = new JSONObject();
        json.put( "type", "object" );
        json.put( "properties", properties );
        json.put( "required", new JSONArray().put( "Illocutionary Force" ) );

        // Generate the answer and load it inside a JSON Object
        JSONObject ans = new JSONObject( generate( CLASS_MODEL, msg, json ) );
        // Postprocess
        String ilfObjStr = ans.getString( "response" )
            .replaceAll( "```json\\s*", "" )
            .replaceAll( "```\\s*$", "" )
            .trim();
        JSONObject ilfObj = new JSONObject( ilfObjStr );
        // Return the literal
        return createLiteral( ilfObj.getString( "Illocutionary Force" ).trim() );
    }

    /**
     * This function translates a user message to a Literal
     * To get better results the terms are translated in Json, for example:
     * p(a, b, 1) -> { "functor": p, "arg0": "a", "arg1": "b", "arg2": 1 }
     * @param msg the input message
     * @param nearest the nearest term in the embedding space
     * @param ilf the classified Illocutionary Force
     * @param examples a list of all the terms with same functor and arity of the nearest
     * @return the nearest literal
     * @throws IOException if fails reading the NL2LOG_PROMPT file
     * @throws ParseException if the provided answer is not a valid Jason term
     */
    public Literal generate( String msg, Literal nearest, Literal ilf, List<Literal> examples ) throws IOException, ParseException {

        List<JSONObject> jsonExamples = new ArrayList<>();
        List<Map<String, Term>> mapExamples = new ArrayList<>();
        System.out.println("[LOG] nearest: " + nearest );
        // Translate the term in JSON
        Map<String, Term> nearestJson = termToMap( nearest );
        System.out.println("[LOG] nearest json: " + nearestJson );
        // Translate all the examples
        for ( Literal example : examples ) {
            try {
                Map<String, Term> mapExample = termToMap( example );
                mapExamples.add( mapExample );
                jsonExamples.add( mapToJson( mapExample ) );
            } catch ( NoValueException nve ) {
                nve.printStackTrace();
            }
        }
        // Generate a schema with types provided in the examples for each arg
        JSONObject schema = genJSONSchema( mapExamples );
        System.out.println( "[LOG] Sentence: " + msg + ", nearest: " + nearestJson + ", ilf: " + ilf + ", examples: " + jsonExamples );
        // Read the prompt and replace needed placeholders
        String prompt = Files.readString( Path.of( NL2LOG_PROMPT ) )
            .replace( "SENTENCE", msg )
            .replace( "NEAREST_JSON", nearestJson.toString() )
            .replace( "ILF", ilf.toString() )
            .replace( "EXAMPLES", jsonExamples.toString() );
        
        // Generate the new term
        JSONObject answer = new JSONObject( generate( GEN_MODEL, prompt, schema ) );
        JSONObject response = new JSONObject( answer.getString( "response" ) );
        try {
            Literal responseTerm = jsonToTerm( response );
            return responseTerm;
        } catch ( ParseException pe ) {
            throw new ParseException( "LLM error! Generated: " + response + ". It is not a valid Jason term." );
        }
    }

    /**
     * This function translates a Jason message in Natural Language
     * @param msg the KQML message
     * @return the natural language translation
     * @throws IOException if fails to open LOG2NL_PROMPT
     */
    public String generate( Message msg ) throws IOException {
        String prompt = Files.readString( Path.of( LOG2NL_PROMPT ) )
            .replace( "SENDER", msg.getSender() )
            .replace( "ILFORCE", msg.getIlForce() )
            .replace( "CONTENT", msg.getPropCont().toString() );
        JSONObject answer = new JSONObject( generate(LOG2NL_MODEL, prompt) );
        return answer.getString( "response" ).replaceAll( "(?s)<think>.*?</think>", "" );
    }

    /**
     * Creates a prompted model
     * @param from the starting model
     * @param model the final model name
     * @param t the temperature
     * @param sys_file the path to a system file
     */
    public void create( String from, String model, float t, String sys_file ) {
        create(from, model, t, sys_file, SEED);
    }

    /**
     * Creates a prompted model
     * @param from the starting model
     * @param model the final model name
     * @param t the temperature
     * @param sys_file the path to a system file
     * @param seed the seed for generation
     */
    public void create( String from, String model, float t, String sys_file, int seed ) {
        try {
            // Read system file content
            String systemContent = Files.readString( Path.of(sys_file ) );

            // FOR OPENAI: No HTTP call. Just save the prompt in memory.
            if ("openai".equals(GEN_PROVIDER) || "gemini".equals(GEN_PROVIDER)) {
                SystemPrompts.put(model, systemContent);
                System.out.println("[LOG] OpenAI: System prompt memorized for virtual model '" + model + "'");
                return; // Stop here for OpenAI
            }

            // FOR OLLAMA: Original logic
            JSONObject params = new JSONObject();
            params.put( "temperature", t );
            params.put( "seed", seed );
            JSONObject json = new JSONObject();
            json.put( "from", from );
            json.put( "model", model );
            json.put( "stream", false );
            json.put( "parameters", params );
            json.put( "system", systemContent );

            HttpRequest request = HttpRequest.newBuilder()
                .uri( URI.create( GEN_URL + "create" ) )
                .header( "Content-Type", "application/json" )
                .POST( HttpRequest.BodyPublishers.ofString( json.toString() ) )
                .build();

            client.send(request, HttpResponse.BodyHandlers.ofString());
        } catch ( IOException e ) {
            e.printStackTrace();
        } catch ( InterruptedException e ) {
            e.printStackTrace();
        }
    }
}