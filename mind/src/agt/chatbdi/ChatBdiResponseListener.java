package chatbdi;

public interface ChatBdiResponseListener {
    void onAgentResponse(String agent, String message);
}